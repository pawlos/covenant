using System.Collections.Immutable;
using Mono.Cecil;

namespace CallgraphClosure.ILCheck.Core;

public sealed record Diagnostic(
    string Id,
    string PropertyName,
    MethodDefinition AnnotatedCaller,
    ImmutableArray<MethodReference> Chain,
    string? SinkLabel,
    MethodReference? UnresolvedTarget);
