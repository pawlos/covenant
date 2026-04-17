using System.Collections.Generic;
using System.Collections.Immutable;
using Mono.Cecil;

namespace CallgraphClosure.ILCheck.Core;

public sealed class ClosureWalker
{
    private readonly string _attributeFullName;
    private readonly ImmutableArray<IIlSink> _sinks;
    private readonly string _propertyName;

    public ClosureWalker(
        string attributeFullName,
        ImmutableArray<IIlSink> sinks,
        string propertyName)
    {
        _attributeFullName = attributeFullName;
        _sinks = sinks;
        _propertyName = propertyName;
    }

    public ImmutableArray<Diagnostic> Analyze(AssemblyDefinition assembly)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!HasPropagatingAttribute(method)) continue;

                VisitMethodBody(
                    method,
                    annotatedCaller: method,
                    chain: ImmutableArray.Create<MethodReference>(method),
                    diagnostics);
            }
        }

        return diagnostics.ToImmutable();
    }

    private void VisitMethodBody(
        MethodDefinition method,
        MethodDefinition annotatedCaller,
        ImmutableArray<MethodReference> chain,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (method.Body is null) return;

        foreach (var instruction in method.Body.Instructions)
        {
            foreach (var sink in _sinks)
            {
                var label = sink.Match(instruction);
                if (label is null) continue;

                diagnostics.Add(new Diagnostic(
                    Id: DiagnosticIds.SinkHit,
                    PropertyName: _propertyName,
                    AnnotatedCaller: annotatedCaller,
                    Chain: chain,
                    SinkLabel: label,
                    UnresolvedTarget: null));
            }
        }
    }

    private bool HasPropagatingAttribute(MethodDefinition method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName == _attributeFullName)
                return true;
        }
        return false;
    }
}
