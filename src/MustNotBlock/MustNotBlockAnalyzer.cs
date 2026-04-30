using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MustNotBlock.Sinks;

namespace MustNotBlock;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotBlockAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotBlockAnalyzer() : base(new Config(
        AttributeFullName: "CallgraphClosure.Attributes.MustNotBlockAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(
            new BlockingMethodSink(),
            new BlockingPropertySink()))) { }
}
