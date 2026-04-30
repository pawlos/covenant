using CallgraphClosure.ILCheck.Core;
using Mono.Cecil.Cil;

namespace MustNotAllocate.ILCheck.Sinks;

public sealed class NewArrSink : IIlSink
{
    public string? Match(Instruction instruction) =>
        instruction.OpCode == OpCodes.Newarr ? "array allocation" : null;
}
