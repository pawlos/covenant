using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotAllocate.Sinks;

public sealed class BoxingConversionSink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IConversionOperation conv) return null;

        // IConversionOperation.Conversion is CommonConversion which lacks IsBoxing.
        // Use the C#-specific extension that returns a Microsoft.CodeAnalysis.CSharp.Conversion.
        var csharpConversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(conv);
        return csharpConversion.IsBoxing ? "boxing conversion" : null;
    }
}
