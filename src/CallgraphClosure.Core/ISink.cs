using Microsoft.CodeAnalysis;

namespace CallgraphClosure.Core;

public interface ISink
{
    // Returns a label (e.g. "object", "array", "boxing") if this sink matches the op,
    // otherwise null.
    string? Match(IOperation op);
}
