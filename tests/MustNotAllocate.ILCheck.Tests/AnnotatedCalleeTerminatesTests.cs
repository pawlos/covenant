using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class AnnotatedCalleeTerminatesTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedCallerTrustsAnnotatedHelper_CallerWalkStopsAtHelper()
    {
        // Helper is annotated but allocates. Caller trusts Helper — no diagnostic attributed
        // to Caller. Helper's own walk finds the allocation and attributes it to Helper.
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Helper(); }

                [MustNotAllocate]
                public void Helper() { var a = new int[10]; }
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

        // Only one diagnostic: attributed to Helper, not Caller.
        Assert.Single(sinkHits);
        Assert.Equal("Helper", sinkHits[0].AnnotatedCaller.Name);
    }
}
