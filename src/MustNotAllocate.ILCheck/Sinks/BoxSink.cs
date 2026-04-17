using CallgraphClosure.ILCheck.Core;
using Mono.Cecil.Cil;

namespace MustNotAllocate.ILCheck.Sinks;

public sealed class BoxSink : IIlSink
{
    public string? Match(Instruction instruction) =>
        instruction.OpCode == OpCodes.Box ? "boxing" : null;
}
