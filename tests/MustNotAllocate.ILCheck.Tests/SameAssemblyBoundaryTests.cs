using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class SameAssemblyBoundaryTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedMethod_CallsUnannotatedSameAssembly_FiresCGC001()
    {
        // Callee is empty so there are no transitive sinks. Only the boundary fires.
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Callee(); }

                public void Callee() { }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var boundaryHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SourceBoundary)
            .ToImmutableArray();

        Assert.Single(boundaryHits);
        Assert.Equal("Callee", boundaryHits[0].Chain.Last().Name);
    }

    [Fact]
    public void AnnotatedMethod_CallsAnnotatedSameAssembly_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Callee(); }

                [MustNotAllocate]
                public void Callee() { }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        Assert.Empty(diagnostics);
    }
}
