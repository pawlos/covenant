using System.Threading.Tasks;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace MustNotAllocate.Tests;

public class AmortizedAnnotationsFileTests
{
    private const string AnnotationsFile = """
        {
          "amortized_methods": [
            "C.Rent()"
          ]
        }
        """;

    [Fact]
    public async Task MethodListedInAnnotationsFile_IsTreatedAsAmortized()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { Rent(); }

                byte[] Rent() => new byte[4096];
            }
            """;

        var test = new CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>.Test
        {
            TestCode = source,
        };
        test.TestState.AdditionalFiles.Add(("amortized-methods.json", AnnotationsFile));
        await test.RunAsync();
    }

    [Fact]
    public async Task MethodNotInFileAndNotAnnotated_StillFiresCGC001()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { UnannotatedUnlisted(); }

                byte[] UnannotatedUnlisted() => new byte[4096];
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "UnannotatedUnlisted");

        var test = new CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>.Test
        {
            TestCode = source,
        };
        test.TestState.AdditionalFiles.Add(("amortized-methods.json", AnnotationsFile));
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }
}
