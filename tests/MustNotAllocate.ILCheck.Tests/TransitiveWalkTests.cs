using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class TransitiveWalkTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedCaller_IndirectArrayAllocation_FiresCGC003WithChain()
    {
        // Caller → Helper → new int[]. Helper is unannotated, same assembly.
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Helper(); }

                public void Helper() { var a = new int[10]; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "array")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal(2, sinkHits[0].Chain.Length);
        Assert.Equal("Caller", sinkHits[0].Chain[0].Name);
        Assert.Equal("Helper", sinkHits[0].Chain[1].Name);
    }

    [Fact]
    public void AnnotatedCaller_IndirectViaTwoHops_FiresCGC003WithFullChain()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void A() { B(); }

                public void B() { C_(); }

                public void C_() { var a = new int[10]; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "array")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal(3, sinkHits[0].Chain.Length);
        Assert.Equal("A", sinkHits[0].Chain[0].Name);
        Assert.Equal("B", sinkHits[0].Chain[1].Name);
        Assert.Equal("C_", sinkHits[0].Chain[2].Name);
    }
}
