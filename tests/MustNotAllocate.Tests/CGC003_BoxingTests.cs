using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC003_BoxingTests
{
    [Fact]
    public async Task AnnotatedMethod_ImplicitBoxing_FiresCGC003()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { object o = 42; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 32)
            .WithArguments("Caller", "MustNotAllocateAttribute", "boxing");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_ExplicitBoxing_FiresCGC003()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { object o = (object)42; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 32)
            .WithArguments("Caller", "MustNotAllocateAttribute", "boxing");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_NoBoxing_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { int x = 42; }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
