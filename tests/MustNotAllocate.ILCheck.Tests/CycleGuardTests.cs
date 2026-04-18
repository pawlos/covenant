using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class CycleGuardTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void MutualRecursion_TerminatesWithoutHanging_FiresOneSinkHit()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void A() { B(); var a = new int[10]; }

                public void B() { A(); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit)
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal("array", sinkHits[0].SinkLabel);
    }
}
