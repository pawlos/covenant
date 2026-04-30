using System.Collections.Immutable;
using CallgraphClosure.ILCheck.Core;
using MustNotBlock.ILCheck.Sinks;

namespace MustNotBlock.ILCheck;

public static class MustNotBlockIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotBlockAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(new BlockingCallSink());
}
