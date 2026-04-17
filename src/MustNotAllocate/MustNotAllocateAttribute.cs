using System;

namespace MustNotAllocate;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotAllocateAttribute : Attribute { }
