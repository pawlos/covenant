using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC001_SourceBoundaryTests
{
    [Fact]
    public async Task AnnotatedMethod_CallsUnannotatedSourceMethod_FiresCGC001()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee(); }

                void Callee() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Callee");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsAnnotatedSourceMethod_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee(); }

                [MustNotAllocate]
                void Callee() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task UnannotatedMethod_CallsUnannotatedSourceMethod_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                void Caller() { Callee(); }
                void Callee() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
