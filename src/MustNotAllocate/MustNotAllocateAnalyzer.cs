using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MustNotAllocate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotAllocateAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotAllocateAnalyzer() : base(new Config(
        AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray<ISink>.Empty)) { }
}
