using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC003_ObjectCreationTests
{
    [Fact]
    public async Task AnnotatedMethod_CreatesSourceObject_FiresCGC003AndCGC001()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class Foo { public Foo() {} }

            class C
            {
                [MustNotAllocate]
                void Caller() { var x = new Foo(); }
            }
            """;

        var sink = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(8, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "object allocation");

        var ctorEdge = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(8, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Foo");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, sink, ctorEdge);
    }

    [Fact]
    public async Task AnnotatedMethod_CreatesExternalObject_FiresCGC003AndCGC002()
    {
        var source = """
            using System.Text;
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { var x = new StringBuilder(); }
            }
            """;

        var sink = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "object allocation");

        var ctorEdge = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "StringBuilder");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, sink, ctorEdge);
    }

    [Fact]
    public async Task AnnotatedMethod_CreatesStruct_DoesNotFireCGC003()
    {
        // Structs are stack-allocated; no CGC003. But the ctor still counts as a call
        // boundary if unannotated — so we expect CGC001 only.
        var source = """
            using CallgraphClosure.Attributes;

            struct Point { public Point(int x) {} }

            class C
            {
                [MustNotAllocate]
                void Caller() { var p = new Point(1); }
            }
            """;

        var ctorEdge = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(8, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Point");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, ctorEdge);
    }
}
