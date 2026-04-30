using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace MustNotBlock.Tests;

public static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public sealed class Test : CSharpAnalyzerTest<TAnalyzer, XUnitVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = new ReferenceAssemblies(
                "net10.0",
                new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
                System.IO.Path.Combine("ref", "net10.0"));
            TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(
                    typeof(CallgraphClosure.Attributes.MustNotBlockAttribute).Assembly.Location));
        }
    }

    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor) =>
        new(descriptor);

    public static async Task VerifyAnalyzerAsync(
        string source,
        params DiagnosticResult[] expected)
    {
        var test = new Test { TestCode = source };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
