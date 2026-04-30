using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotRecurse.Tests;

public class MustNotRecurseTests
{
    [Fact]
    public async Task AnnotatedMethod_NoCalls_Silent()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotRecurse]
                void F() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotRecurseAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsUnannotatedMethod_FiresCGC001()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotRecurse]
                void F() { Helper(); }

                void Helper() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotRecurseAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 16)
            .WithArguments("F", "MustNotRecurseAttribute", "Helper");

        await CSharpAnalyzerVerifier<MustNotRecurseAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsExternalMethod_FiresCGC002()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotRecurse]
                void F() { Console.WriteLine("x"); }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotRecurseAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 16)
            .WithArguments("F", "MustNotRecurseAttribute", "WriteLine");

        await CSharpAnalyzerVerifier<MustNotRecurseAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsAnnotatedMethod_Silent()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotRecurse]
                void F() { Helper(); }

                [MustNotRecurse]
                void Helper() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotRecurseAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedMethod_DirectSelfRecursion_RoslynSilent()
    {
        // Documented limit: Roslyn-side detection of direct self-recursion would require
        // changing ISink.Match to take the enclosing-method context. Deferred. The IL pass
        // detects this case at build time. See spec for rationale.
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotRecurse]
                int F(int n) => n <= 1 ? 1 : n * F(n - 1);
            }
            """;

        await CSharpAnalyzerVerifier<MustNotRecurseAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
