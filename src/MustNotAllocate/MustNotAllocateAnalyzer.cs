using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MustNotAllocate.Sinks;

namespace MustNotAllocate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotAllocateAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotAllocateAnalyzer() : base(new Config(
        AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(
            new ObjectCreationSink(),
            new ArrayCreationSink()))) { }
}
