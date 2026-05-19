using System.Threading.Tasks;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Xunit;

namespace MustNotAllocate.Tests;

public class LocalFunctionTests
{
    [Fact]
    public async Task TopLevelStatements_FileScopeAnnotatedStaticMethod_FiresCGC003()
    {
        // File-scope methods in a top-level-statements program lower to local functions
        // of the synthesized <Main>$. The attribute on the local function must still
        // be inspected and its body walked for sinks.
        var source = """
            using CallgraphClosure.Attributes;

            Tick(5);

            [MustNotAllocate]
            static void Tick(int n) { var a = new int[n]; }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 35)
            .WithArguments("Tick", "MustNotAllocateAttribute", "array allocation");

        var test = new CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>.Test
        {
            TestCode = source,
        };
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task RegularMethod_AnnotatedLocalFunction_FiresCGC003()
    {
        // Local function nested inside an unannotated outer method.
        // Isolates the local-function aspect from the top-level-statements aspect.
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                void Outer()
                {
                    Tick(5);

                    [MustNotAllocate]
                    static void Tick(int n) { var a = new int[n]; }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(10, 43)
            .WithArguments("Tick", "MustNotAllocateAttribute", "array allocation");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
