using System.Collections.Immutable;
using CallgraphClosure.ILCheck.Core;

namespace MustNotRecurse.ILCheck;

public static class MustNotRecurseIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotRecurseAttribute";
    public const string CycleSinkLabel = "recursion";

    // No instruction-level sinks. The walker's cycle-detection mode (configured via
    // cycleSinkLabel) is the entire detection mechanism for this property.
    public static ImmutableArray<IIlSink> Sinks { get; } = ImmutableArray<IIlSink>.Empty;
}
