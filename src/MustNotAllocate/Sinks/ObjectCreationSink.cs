using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotAllocate.Sinks;

public sealed class ObjectCreationSink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IObjectCreationOperation oc) return null;
        // Struct construction is stack allocation, not a heap allocation.
        if (oc.Type is null || oc.Type.IsValueType) return null;
        return "object allocation";
    }
}
