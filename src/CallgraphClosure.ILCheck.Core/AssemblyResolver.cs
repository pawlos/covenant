using System;
using System.IO;
using Mono.Cecil;

namespace CallgraphClosure.ILCheck.Core;

public sealed class AssemblyResolver : BaseAssemblyResolver
{
    public AssemblyResolver(params string[] searchDirectories)
    {
        foreach (var dir in searchDirectories)
        {
            if (Directory.Exists(dir))
                AddSearchDirectory(dir);
        }

        // Always include the current runtime's base directory as a fallback so BCL
        // reference resolution works out of the box for tests and sample walks.
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(runtimeDir))
            AddSearchDirectory(runtimeDir);
    }

    public static AssemblyResolver ForAssemblyPath(string assemblyPath)
    {
        var assemblyDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath)) ?? ".";
        return new AssemblyResolver(assemblyDir);
    }
}
