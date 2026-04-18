using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class GenericUnwrapTests
{
    [Fact]
    public async Task AnnotatedGenericCallee_ConstructedForm_DoesNotFireBoundary()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee<int>(); }

                [MustNotAllocate]
                void Callee<T>() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task UnannotatedGenericCallee_ConstructedForm_FiresOnceAsCGC001()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee<int>(); }

                void Callee<T>() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Callee");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
