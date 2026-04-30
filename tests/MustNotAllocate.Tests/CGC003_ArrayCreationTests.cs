using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC003_ArrayCreationTests
{
    [Fact]
    public async Task AnnotatedMethod_CreatesArrayWithSize_FiresCGC003()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { var a = new int[10]; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "array allocation");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CreatesArrayWithInitializer_FiresCGC003()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { var a = new int[] { 1, 2, 3 }; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "array allocation");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
