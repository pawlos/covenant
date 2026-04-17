using System.IO;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class SmokeTests
{
    [Fact]
    public void CompileFixture_ProducesReadableDll()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { }
            }
            """;

        var dllPath = CompileFixture.Emit(source);

        Assert.True(File.Exists(dllPath), $"Expected DLL at {dllPath}");
        Assert.True(new FileInfo(dllPath).Length > 0, "Expected non-empty DLL");
    }
}
