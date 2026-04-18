using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MustNotAllocate.ILCheck.Tests;

// Compiles a C# source string to a DLL in a fresh temp directory,
// copying MustNotAllocate.dll alongside so Cecil can resolve the attribute.
public static class CompileFixture
{
    public static string Emit(string source, string assemblyName = "Fixture")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cgc-il-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var outputPath = Path.Combine(tempDir, assemblyName + ".dll");

        var references = GetStandardReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(outputPath);
        if (!result.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException(
                "Fixture compilation failed:" + Environment.NewLine + errors);
        }

        // Copy MustNotAllocate.dll next to the fixture so Cecil's resolver finds it.
        var mustNotAllocateDllPath = typeof(global::CallgraphClosure.Attributes.MustNotAllocateAttribute)
            .Assembly.Location;
        File.Copy(
            mustNotAllocateDllPath,
            Path.Combine(tempDir, Path.GetFileName(mustNotAllocateDllPath)),
            overwrite: true);

        return outputPath;
    }

    private static IEnumerable<MetadataReference> GetStandardReferences()
    {
        // Reference the same assemblies the test host loaded — covers BCL plus our own projects.
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES not found");

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (File.Exists(path))
                yield return MetadataReference.CreateFromFile(path);
        }

        // Make sure MustNotAllocate is referenced so fixtures can use [MustNotAllocate].
        yield return MetadataReference.CreateFromFile(
            typeof(global::CallgraphClosure.Attributes.MustNotAllocateAttribute).Assembly.Location);
    }
}
