using System.Threading.Tasks;
using Xunit;

namespace MustNotAllocate.Tests;

public class SmokeTests
{
    [Fact]
    public async Task SourceWithNoAnnotations_ProducesNoDiagnostics()
    {
        var source = """
            using CallgraphClosure.Attributes;

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
