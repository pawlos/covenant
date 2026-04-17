using System;
using System.IO;
using CallgraphClosure.ILCheck.Core;
using Mono.Cecil;
using MustNotAllocate.ILCheck;

namespace CallgraphClosure.ILCheck.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: cgc-ilcheck <path-to-assembly>");
            return 2;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            return 2;
        }

        using var assembly = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters
            {
                AssemblyResolver = AssemblyResolver.ForAssemblyPath(path),
            });

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate");

        var diagnostics = walker.Analyze(assembly);

        Console.Write(DiagnosticFormatter.Format(path, diagnostics));

        return diagnostics.Length == 0 ? 0 : 1;
    }
}
