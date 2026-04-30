using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotThrow.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotThrow.ILCheck.Tests;

public class MustNotThrowIlTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotThrowIlAnalyzer.AttributeFullName,
        MustNotThrowIlAnalyzer.Sinks,
        propertyName: "MustNotThrow");

    [Fact]
    public void AnnotatedMethod_DirectThrow_FiresCGC003()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotThrow]
                public void F() { throw new Exception(); }
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
        Assert.Equal("throw", sinkHits[0].SinkLabel);
        Assert.Equal("F", sinkHits[0].AnnotatedCaller.Name);
        Assert.Single(sinkHits[0].Chain);
    }

    [Fact]
    public void AnnotatedMethod_TransitiveThrowViaHelper_FiresCGC003WithChain()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotThrow]
                public void F() { Helper(); }

                public void Helper() { throw new Exception(); }
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
        Assert.Equal(2, sinkHits[0].Chain.Length);
        Assert.Equal("F", sinkHits[0].Chain[0].Name);
        Assert.Equal("Helper", sinkHits[0].Chain[1].Name);
    }

    [Fact]
    public void AnnotatedMethod_TransitiveThrowViaThrowHelper_FiresCGC003WithChain()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotThrow]
                public void F(string? x) { ThrowIfNull(x); }

                public static void ThrowIfNull(string? x)
                {
                    if (x is null) throw new ArgumentNullException(nameof(x));
                }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        // The walker recurses into BCL code (ArgumentNullException ctor calls into BCL which
        // contains throw opcodes). Assert the expected chain is present rather than asserting
        // a total count.
        var chainHit = diagnostics
            .Where(d =>
                d.Id == DiagnosticIds.SinkHit &&
                d.Chain.Length >= 2 &&
                d.Chain[0].Name == "F" &&
                d.Chain[1].Name == "ThrowIfNull")
            .ToImmutableArray();

        Assert.NotEmpty(chainHit);
    }

    [Fact]
    public void AnnotatedCallee_WalkStopsAtAnnotatedHelper()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotThrow]
                public void F() { Helper(); }

                [MustNotThrow]
                public void Helper() { throw new Exception(); }
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

        // Only Helper's throw fires; F's walk stops at the annotated Helper.
        Assert.Single(sinkHits);
        Assert.Equal("Helper", sinkHits[0].AnnotatedCaller.Name);
    }

    [Fact]
    public void AnnotatedMethod_Rethrow_FiresCGC003Rethrow()
    {
        var source = """
            using System;
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotThrow]
                public void F()
                {
                    try { }
                    catch { throw; }
                }
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
        Assert.Equal("rethrow", sinkHits[0].SinkLabel);
    }
}
