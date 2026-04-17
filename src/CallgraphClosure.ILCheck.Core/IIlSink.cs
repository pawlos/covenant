using Mono.Cecil.Cil;

namespace CallgraphClosure.ILCheck.Core;

public interface IIlSink
{
    // Returns a label (e.g. "object", "array", "boxing") if this sink matches the instruction,
    // otherwise null.
    string? Match(Instruction instruction);
}
