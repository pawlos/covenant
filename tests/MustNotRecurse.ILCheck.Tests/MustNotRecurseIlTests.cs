using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotRecurse.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotRecurse.ILCheck.Tests;

public class MustNotRecurseIlTests
{
    private static ClosureWalker BuildWalker() => new(
        attributeFullName: MustNotRecurseIlAnalyzer.AttributeFullName,
        sinks: MustNotRecurseIlAnalyzer.Sinks,
        propertyName: "MustNotRecurse",
        cycleSinkLabel: MustNotRecurseIlAnalyzer.CycleSinkLabel);

    [Fact]
    public void AnnotatedMethod_DirectSelfRecursion_FiresCGC003()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotRecurse]
                public int F(int n) => n <= 1 ? 1 : n * F(n - 1);
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "recursion")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal("F", sinkHits[0].AnnotatedCaller.Name);
        // Chain is [F, F] — the entry plus the cycle-closing call.
        Assert.Equal(2, sinkHits[0].Chain.Length);
        Assert.Equal("F", sinkHits[0].Chain[0].Name);
        Assert.Equal("F", sinkHits[0].Chain[1].Name);
    }

    [Fact]
    public void AnnotatedMethod_TransitiveCycleViaUnannotatedHelper_FiresCGC003()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotRecurse]
                public int A(int n) => n <= 0 ? 0 : B(n);

                public int B(int n) => A(n - 1);
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "recursion")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal("A", sinkHits[0].AnnotatedCaller.Name);
        // Chain is [A, B, A].
        Assert.Equal(3, sinkHits[0].Chain.Length);
        Assert.Equal("A", sinkHits[0].Chain[0].Name);
        Assert.Equal("B", sinkHits[0].Chain[1].Name);
        Assert.Equal("A", sinkHits[0].Chain[2].Name);
    }

    [Fact]
    public void AnnotatedMethod_ThreeMethodCycle_FiresCGC003WithFullChain()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotRecurse]
                public int A(int n) => n <= 0 ? 0 : B(n);

                public int B(int n) => Cm(n);
                public int Cm(int n) => A(n - 1);
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "recursion")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal(4, sinkHits[0].Chain.Length);
        Assert.Equal("A", sinkHits[0].Chain[0].Name);
        Assert.Equal("B", sinkHits[0].Chain[1].Name);
        Assert.Equal("Cm", sinkHits[0].Chain[2].Name);
        Assert.Equal("A", sinkHits[0].Chain[3].Name);
    }

    [Fact]
    public void AnnotatedMethod_NoCycle_Silent()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotRecurse]
                public int A() => B();

                public int B() => Cm();
                public int Cm() => 42;
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

        Assert.Empty(sinkHits);
    }

    [Fact]
    public void TwoAnnotatedMethods_MutualRecursion_FiresTwiceOncePerCaller()
    {
        // Validates that cycle-detection mode disables annotated-callee termination.
        // Without that, A's walk would stop at B (annotated) and B's walk would stop at A,
        // and the cycle would be invisible.
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotRecurse]
                public int A(int n) => n <= 0 ? 0 : B(n);

                [MustNotRecurse]
                public int B(int n) => A(n - 1);
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "recursion")
            .ToImmutableArray();

        Assert.Equal(2, sinkHits.Length);
        // One diagnostic from A's perspective, one from B's.
        var callers = sinkHits.Select(d => d.AnnotatedCaller.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "A", "B" }, callers);
    }

    [Fact]
    public void CycleSurvivesVisitedPrune_OneDiagnosticForOneCycle()
    {
        // A → B → D → A and A → C → D (D shared across two paths).
        // Walker DFS from A visits B → D → (back to A: cycle) first. Then A → C → D
        // is reached, but D is already in 'visited', so the second path doesn't re-emit
        // the same cycle. Net: one diagnostic, not two.
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotRecurse]
                public int A(int n) => n <= 0 ? 0 : B(n) + Cx(n);

                public int B(int n) => D(n);
                public int Cx(int n) => D(n);
                public int D(int n) => A(n - 1);
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "recursion")
            .ToImmutableArray();

        Assert.Single(sinkHits);
    }
}
