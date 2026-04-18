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
    public void AnnotatedMethod_CallsUnannotatedEmptyCallee_WalksThroughFiresNothing()
    {
        // With transitive walking, an empty unannotated callee is walked through. No sinks,
        // no diagnostics. The M1 Roslyn pass treats this differently (emits CGC001) — that's
        // the intended M1/M2 semantic divergence.
        var source = """
            using CallgraphClosure.Attributes;

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

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AnnotatedMethod_CallsAnnotatedSameAssembly_FiresNothing()
    {
        var source = """
            using CallgraphClosure.Attributes;

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
