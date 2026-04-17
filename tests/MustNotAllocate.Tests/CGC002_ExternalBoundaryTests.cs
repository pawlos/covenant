using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC002_ExternalBoundaryTests
{
    [Fact]
    public async Task AnnotatedMethod_CallsExternalMethod_FiresCGC002()
    {
        var source = """
            using System;
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Console.WriteLine("hi"); }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "WriteLine");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
