using CallgraphClosure.ILCheck.Core;
using Mono.Cecil.Cil;

namespace MustNotThrow.ILCheck.Sinks;

public sealed class ThrowSink : IIlSink
{
    public string? Match(Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Throw) return "throw";
        if (instruction.OpCode == OpCodes.Rethrow) return "rethrow";
        return null;
    }
}
