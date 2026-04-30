using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotBlock.Sinks;

public sealed class BlockingPropertySink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IPropertyReferenceOperation pr) return null;
        var prop = pr.Property.OriginalDefinition;
        if (prop.Name != "Result") return null;

        var typeName = prop.ContainingType.ConstructedFrom
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");
        return typeName == "System.Threading.Tasks.Task<TResult>" ? "Task.Result" : null;
    }
}
