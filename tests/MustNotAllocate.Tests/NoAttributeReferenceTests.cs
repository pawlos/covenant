using System.Threading.Tasks;
using Xunit;

namespace MustNotAllocate.Tests;

public class NoAttributeReferenceTests
{
    [Fact]
    public async Task UserDefinedLikeNamedAttribute_IsNotMatched()
    {
        // User defined their own [MustNotAllocate] in the wrong namespace.
        // The analyzer looks for MustNotAllocate.MustNotAllocateAttribute by FQN,
        // so this source should produce no diagnostics.
        var source = """
            namespace Other
            {
                class MustNotAllocateAttribute : System.Attribute { }

                class C
                {
                    [MustNotAllocate]
                    void Caller() { Callee(); }

                    void Callee() { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
