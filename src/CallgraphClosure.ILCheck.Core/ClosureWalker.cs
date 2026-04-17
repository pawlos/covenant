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
        // Implemented in later tasks.
        return ImmutableArray<Diagnostic>.Empty;
    }

    public bool HasPropagatingAttribute(MethodDefinition method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName == _attributeFullName)
                return true;
        }
        return false;
    }
}
