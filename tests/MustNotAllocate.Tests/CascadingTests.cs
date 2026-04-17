using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CascadingTests
{
    [Fact]
    public async Task BeforeAnnotatingMiddle_DiagnosticOnOuterCall()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void A() { B(); }

                void B() { C_(); }

                void C_() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 16)
            .WithArguments("A", "MustNotAllocateAttribute", "B");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AfterAnnotatingMiddle_DiagnosticShiftsToInnerCall()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void A() { B(); }

                [MustNotAllocate]
                void B() { C_(); }

                void C_() { }
            }
            """;

        // A→B is now fine (both annotated); B→C_ is now the violation.
        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(9, 16)
            .WithArguments("B", "MustNotAllocateAttribute", "C_");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
