using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class AmortizedAllocationTests
{
    [Fact]
    public async Task AnnotatedCaller_CallsAmortizedMethod_FiresNothing()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { Rent(); }

                [AmortizedAllocation]
                byte[] Rent() => new byte[4096];
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedCaller_CallsUnannotatedMethod_StillFiresCGC001()
    {
        // Regression: non-amortized unannotated callees still produce CGC001.
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { NotPooled(); }

                byte[] NotPooled() => new byte[4096];
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "NotPooled");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
