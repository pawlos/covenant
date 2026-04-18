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
        string? assemblyPath = null;
        string? amortizedPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--amortized-file" && i + 1 < args.Length)
            {
                amortizedPath = args[++i];
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                assemblyPath = args[i];
            }
        }

        if (assemblyPath is null)
        {
            Console.Error.WriteLine("Usage: cgc-ilcheck [--amortized-file <path>] <assembly>");
            return 2;
        }

        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"Error: file not found: {assemblyPath}");
            return 2;
        }

        AmortizedSet amortized = AmortizedSet.Empty;
        if (amortizedPath is not null)
        {
            if (!File.Exists(amortizedPath))
            {
                Console.Error.WriteLine($"Error: amortized file not found: {amortizedPath}");
                return 2;
            }
            try
            {
                amortized = AmortizedSet.Parse(File.ReadAllText(amortizedPath));
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"Error parsing amortized file: {ex.Message}");
                return 2;
            }
        }

        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters
            {
                AssemblyResolver = AssemblyResolver.ForAssemblyPath(assemblyPath),
            });

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate",
            amortizedAttributeFullName: MustNotAllocateIlAnalyzer.AmortizedAttributeFullName,
            amortizedSet: amortized);

        var diagnostics = walker.Analyze(assembly);

        Console.Write(DiagnosticFormatter.Format(assemblyPath, diagnostics));

        return diagnostics.Length == 0 ? 0 : 1;
    }
}
