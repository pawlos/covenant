using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class CrossAssemblyBoundaryTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedMethod_CallsBCLMethod_FiresCGC001Or002()
    {
        // This test asserts the walker reports SOMETHING when calling into the BCL —
        // either CGC002 (external, opaque) or CGC001-style (transitive, resolved).
        // Task 14 tightens this to prefer upgraded CGC003 when the BCL is walkable.
        var source = """
            using System;
            using MustNotAllocate;

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

        Assert.NotEmpty(diagnostics);

        var hasBoundaryOrSink = diagnostics.Any(d =>
            d.Id == DiagnosticIds.ExternalBoundary ||
            d.Id == DiagnosticIds.SourceBoundary ||
            d.Id == DiagnosticIds.SinkHit);
        Assert.True(hasBoundaryOrSink);
    }
}
