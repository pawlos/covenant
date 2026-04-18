using System;

namespace CallgraphClosure.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AmortizedAllocationAttribute : Attribute { }
