using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MustNotThrow.Sinks;

namespace MustNotThrow;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotThrowAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotThrowAnalyzer() : base(new Config(
        AttributeFullName: "CallgraphClosure.Attributes.MustNotThrowAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(new ThrowOperationSink()))) { }
}
