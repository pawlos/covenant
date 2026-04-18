using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class AmortizedFileTests
{
    [Fact]
    public void MethodInFile_IsTreatedAsAmortized()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Rent(); }

                public byte[] Rent() => new byte[4096];
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var amortized = AmortizedSet.Parse("""{"amortized_methods": ["System.Byte[] C::Rent()"]}""");

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate",
            amortizedAttributeFullName: MustNotAllocateIlAnalyzer.AmortizedAttributeFullName,
            amortizedSet: amortized);

        var diagnostics = walker.Analyze(assembly);

        Assert.Empty(diagnostics);
    }
}
