using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotBlock.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotBlock.ILCheck.Tests;

public class MustNotBlockIlTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotBlockIlAnalyzer.AttributeFullName,
        MustNotBlockIlAnalyzer.Sinks,
        propertyName: "MustNotBlock");

    [Fact]
    public void AnnotatedMethod_DirectThreadSleep_FiresCGC003()
    {
        var source = """
            using System.Threading;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotBlock]
                public void F() { Thread.Sleep(100); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "Thread.Sleep")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal("F", sinkHits[0].AnnotatedCaller.Name);
        Assert.Single(sinkHits[0].Chain);
    }

    [Fact]
    public void AnnotatedMethod_TransitiveThreadSleep_FiresCGC003WithChain()
    {
        var source = """
            using System.Threading;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotBlock]
                public void F() { Helper(); }

                public void Helper() { Thread.Sleep(100); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "Thread.Sleep")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal(2, sinkHits[0].Chain.Length);
        Assert.Equal("F", sinkHits[0].Chain[0].Name);
        Assert.Equal("Helper", sinkHits[0].Chain[1].Name);
    }

    [Fact]
    public void AnnotatedMethod_TaskTResultGetter_FiresCGC003()
    {
        // Verifies generic-instantiation handling: Task<int>::get_Result()
        // should resolve to the generic Task`1::get_Result() entry in the sink list.
        var source = """
            using System.Threading.Tasks;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotBlock]
                public int F(Task<int> t) { return t.Result; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "Task.Result")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal("F", sinkHits[0].AnnotatedCaller.Name);
    }

    [Fact]
    public void AnnotatedCallee_TerminatesWalk()
    {
        var source = """
            using System.Threading;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotBlock]
                public void F() { Helper(); }

                [MustNotBlock]
                public void Helper() { Thread.Sleep(100); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "Thread.Sleep")
            .ToImmutableArray();

        // Only Helper's Thread.Sleep fires; F's walk stops at the annotated Helper.
        Assert.Single(sinkHits);
        Assert.Equal("Helper", sinkHits[0].AnnotatedCaller.Name);
    }

    [Fact]
    public void AnnotatedAsyncMethod_AwaitsTaskYield_NoSinkHits()
    {
        // Canary: compiler-generated state machine plumbing transitively reaches a lot of
        // BCL code. None of it should match our blocking sink list.
        var source = """
            using System.Threading.Tasks;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotBlock]
                public async Task F() { await Task.Yield(); }
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
}
