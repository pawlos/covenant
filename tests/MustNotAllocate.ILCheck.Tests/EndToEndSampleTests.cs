using System.IO;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace MustNotAllocate.ILCheck.Tests;

public class EndToEndSampleTests
{
    private readonly ITestOutputHelper _output;

    public EndToEndSampleTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ILCheck_OnCompiledSample_FindsTheArrayAllocation()
    {
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(
            repoRoot,
            "src", "MustNotAllocate.Sample", "bin", "Debug", "net10.0",
            "MustNotAllocate.Sample.dll");

        Assert.True(
            File.Exists(samplePath),
            $"Compiled sample not found at {samplePath} — build the solution first.");

        using var assembly = AssemblyDefinition.ReadAssembly(
            samplePath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(samplePath) });

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate");

        var diagnostics = walker.Analyze(assembly);

        // Log first 20 for the writeup (there will be many from the Console.WriteLine transitive walk).
        foreach (var d in diagnostics.Take(20))
        {
            var chainStr = string.Join(" -> ", d.Chain.Select(m => m.Name));
            _output.WriteLine($"{d.Id} ({d.SinkLabel ?? "-"}, chain={d.Chain.Length}): {chainStr}");
        }

        _output.WriteLine($"... {diagnostics.Length} total diagnostics");

        // Must find at least the direct array allocation inside Tick.
        var directArray = diagnostics.FirstOrDefault(d =>
            d.Id == DiagnosticIds.SinkHit &&
            d.SinkLabel == "array allocation" &&
            d.AnnotatedCaller.Name == "Tick" &&
            d.Chain.Length == 1);

        Assert.NotNull(directArray);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CallgraphClosure.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new FileNotFoundException("Could not locate repo root (CallgraphClosure.sln)");
        return dir.FullName;
    }
}
