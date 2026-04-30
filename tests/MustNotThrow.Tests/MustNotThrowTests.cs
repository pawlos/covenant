using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotThrow.Tests;

public class MustNotThrowTests
{
    [Fact]
    public async Task AnnotatedMethod_DirectThrow_FiresCGC003()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotThrow]
                void F() { throw new Exception(); }
            }
            """;

        var sinkHit = CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 16)
            .WithArguments("F", "MustNotThrowAttribute", "throw");

        // `new Exception()` inside the throw is also an external ctor call → CGC002.
        var externalCtor = CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 22)
            .WithArguments("F", "MustNotThrowAttribute", "Exception");

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source, sinkHit, externalCtor);
    }

    [Fact]
    public async Task AnnotatedMethod_RethrowInCatch_FiresCGC003Rethrow()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotThrow]
                void F()
                {
                    try { }
                    catch { throw; }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(9, 17)
            .WithArguments("F", "MustNotThrowAttribute", "rethrow");

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_NoThrow_Silent()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotThrow]
                void F() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsUnannotatedMethod_FiresCGC001()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotThrow]
                void F() { Helper(); }

                void Helper() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 16)
            .WithArguments("F", "MustNotThrowAttribute", "Helper");

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsAnnotatedMethod_Silent()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotThrow]
                void F() { Helper(); }

                [MustNotThrow]
                void Helper() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsExternalMethod_FiresCGC002()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotThrow]
                void F() { Console.WriteLine("x"); }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 16)
            .WithArguments("F", "MustNotThrowAttribute", "WriteLine");

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_ThrowInNullCoalescing_FiresCGC003()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotThrow]
                string F(string? x) { return x ?? throw new InvalidOperationException(); }
            }
            """;

        var sinkHit = CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 39)
            .WithArguments("F", "MustNotThrowAttribute", "throw");

        // `new InvalidOperationException()` inside the throw expression is also an external ctor call.
        var externalCtor = CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 45)
            .WithArguments("F", "MustNotThrowAttribute", "InvalidOperationException");

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source, sinkHit, externalCtor);
    }

    [Fact]
    public async Task UserDefinedLikeNamedAttribute_IsNotMatched()
    {
        var source = """
            namespace Other
            {
                class MustNotThrowAttribute : System.Attribute { }

                class C
                {
                    [MustNotThrow]
                    void F() { throw new System.Exception(); }
                }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotThrowAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
