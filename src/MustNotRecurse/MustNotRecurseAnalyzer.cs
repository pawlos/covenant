using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MustNotRecurse;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotRecurseAnalyzer : CallgraphClosureAnalyzer
{
    // No CGC003 sinks at the Roslyn level — recursion is a graph property and detection
    // requires the IL pass. Roslyn still produces CGC001/CGC002 for unannotated/external
    // call boundaries via the standard flow.
    public MustNotRecurseAnalyzer() : base(new Config(
        AttributeFullName: "CallgraphClosure.Attributes.MustNotRecurseAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray<ISink>.Empty)) { }
}
