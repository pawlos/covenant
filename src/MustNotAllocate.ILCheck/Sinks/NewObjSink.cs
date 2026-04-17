using CallgraphClosure.ILCheck.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MustNotAllocate.ILCheck.Sinks;

public sealed class NewObjSink : IIlSink
{
    public string? Match(Instruction instruction)
    {
        if (instruction.OpCode != OpCodes.Newobj) return null;
        if (instruction.Operand is not MethodReference ctor) return null;

        // Struct construction via newobj on a value type does not heap-allocate.
        if (ctor.DeclaringType.IsValueType) return null;

        return "object";
    }
}
