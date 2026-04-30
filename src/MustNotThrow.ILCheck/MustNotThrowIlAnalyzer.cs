using System.Collections.Immutable;
using CallgraphClosure.ILCheck.Core;
using MustNotThrow.ILCheck.Sinks;

namespace MustNotThrow.ILCheck;

public static class MustNotThrowIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotThrowAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(new ThrowSink());
}
