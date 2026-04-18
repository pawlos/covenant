using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace MustNotAllocate.ILCheck.Tests;

public class CrossAssemblyBoundaryTests
{
    private readonly ITestOutputHelper _output;

    public CrossAssemblyBoundaryTests(ITestOutputHelper output) => _output = output;

    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedMethod_CallsConsoleWriteLine_ProducesCGC002OrUpgradedCGC003()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Console.WriteLine("hi"); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        // Dump everything for the writeup / for investigation on CI.
        foreach (var d in diagnostics)
        {
            var chainStr = string.Join(" -> ", d.Chain.Select(m => m.Name));
            _output.WriteLine($"{d.Id} ({d.SinkLabel ?? "-"}): {chainStr}");
        }

        // Outcome A: walk reached a sink inside the BCL → CGC003 upgraded from CGC002.
        // Outcome B: walk hit a ref-only body or an unresolvable call → CGC002.
        var hasCGC003Upgrade = diagnostics.Any(d =>
            d.Id == DiagnosticIds.SinkHit && d.Chain.Length > 1);
        var hasCGC002 = diagnostics.Any(d => d.Id == DiagnosticIds.ExternalBoundary);

        Assert.True(
            hasCGC003Upgrade || hasCGC002,
            "Expected either a transitively-found sink (CGC003) or an unresolved external (CGC002).");
    }
}
