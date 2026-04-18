using CallgraphClosure.ILCheck.Core;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class AmortizedSetTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsSetWithEntries()
    {
        var json = """
            {
              "amortized_methods": [
                "System.Buffers.ArrayPool`1.Rent(Int32)",
                "System.Buffers.MemoryPool`1.Rent(Int32)"
              ]
            }
            """;

        var set = AmortizedSet.Parse(json);

        Assert.True(set.Contains("System.Buffers.ArrayPool`1.Rent(Int32)"));
        Assert.True(set.Contains("System.Buffers.MemoryPool`1.Rent(Int32)"));
        Assert.False(set.Contains("Something.Else.Method()"));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptySet()
    {
        var set = AmortizedSet.Parse("""{"amortized_methods": []}""");
        Assert.False(set.Contains("anything"));
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsFormatException()
    {
        Assert.Throws<System.FormatException>(
            () => AmortizedSet.Parse("not json at all"));
    }

    [Fact]
    public void Parse_MissingKey_ReturnsEmptySet()
    {
        // Valid JSON but no amortized_methods key — treat as empty, don't throw.
        var set = AmortizedSet.Parse("""{"other": "stuff"}""");
        Assert.False(set.Contains("anything"));
    }

    [Fact]
    public void Empty_IsAlwaysDefinedAndContainsNothing()
    {
        Assert.False(AmortizedSet.Empty.Contains("anything"));
    }
}
