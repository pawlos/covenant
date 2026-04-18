using System.Collections.Immutable;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck.Sinks;

namespace MustNotAllocate.ILCheck;

public static class MustNotAllocateIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotAllocateAttribute";

    public const string AmortizedAttributeFullName = "CallgraphClosure.Attributes.AmortizedAllocationAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(
            new NewObjSink(),
            new NewArrSink(),
            new BoxSink());
}
