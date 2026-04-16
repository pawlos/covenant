using System.Collections.Immutable;

namespace CallgraphClosure.Core;

public sealed record Config(
    string AttributeFullName,
    PropagationDirection Direction,
    ImmutableArray<ISink> Sinks);
