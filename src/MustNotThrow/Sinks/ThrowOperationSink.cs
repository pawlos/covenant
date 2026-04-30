using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotThrow.Sinks;

public sealed class ThrowOperationSink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IThrowOperation throwOp) return null;
        // .Exception is null for bare `throw;` (rethrow inside catch), non-null for `throw e;`.
        return throwOp.Exception is null ? "rethrow" : "throw";
    }
}
