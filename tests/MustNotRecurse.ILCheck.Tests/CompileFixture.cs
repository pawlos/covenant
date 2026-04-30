using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MustNotRecurse.ILCheck.Tests;

public static class CompileFixture
{
    public static string Emit(string source, string assemblyName = "Fixture")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cgc-il-fixtures-mnr", Guid.NewGuid().ToString("N"));
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

        var attributesDllPath = typeof(global::CallgraphClosure.Attributes.MustNotRecurseAttribute)
            .Assembly.Location;
        File.Copy(
            attributesDllPath,
            Path.Combine(tempDir, Path.GetFileName(attributesDllPath)),
            overwrite: true);

        return outputPath;
    }

    private static IEnumerable<MetadataReference> GetStandardReferences()
    {
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES not found");

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (File.Exists(path))
                yield return MetadataReference.CreateFromFile(path);
        }

        yield return MetadataReference.CreateFromFile(
            typeof(global::CallgraphClosure.Attributes.MustNotRecurseAttribute).Assembly.Location);
    }
}
