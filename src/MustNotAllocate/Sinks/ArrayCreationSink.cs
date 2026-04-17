using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotAllocate.Sinks;

public sealed class ArrayCreationSink : ISink
{
    public string? Match(IOperation op) =>
        op is IArrayCreationOperation ? "array" : null;
}
