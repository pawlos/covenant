using System.Collections.Generic;
using CallgraphClosure.ILCheck.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MustNotBlock.ILCheck.Sinks;

public sealed class BlockingCallSink : IIlSink
{
    private static readonly Dictionary<(string Type, string Method), string> Blocking = new()
    {
        { ("System.Threading.Thread", "Sleep"), "Thread.Sleep" },
        { ("System.Threading.Tasks.Task", "Wait"), "Task.Wait" },
        { ("System.Threading.Tasks.Task", "WaitAll"), "Task.WaitAll" },
        { ("System.Threading.Tasks.Task", "WaitAny"), "Task.WaitAny" },
        { ("System.Threading.Tasks.Task`1", "get_Result"), "Task.Result" },
        { ("System.Threading.WaitHandle", "WaitOne"), "WaitHandle.WaitOne" },
        { ("System.Threading.WaitHandle", "WaitAll"), "WaitHandle.WaitAll" },
        { ("System.Threading.WaitHandle", "WaitAny"), "WaitHandle.WaitAny" },
        { ("System.Threading.Monitor", "Wait"), "Monitor.Wait" },
        { ("System.Threading.SemaphoreSlim", "Wait"), "SemaphoreSlim.Wait" },
        { ("System.Threading.ManualResetEventSlim", "Wait"), "ManualResetEventSlim.Wait" },
        { ("System.Threading.CountdownEvent", "Wait"), "CountdownEvent.Wait" },
        { ("System.Threading.Barrier", "SignalAndWait"), "Barrier.SignalAndWait" },
    };

    public string? Match(Instruction instruction)
    {
        if (instruction.OpCode != OpCodes.Call &&
            instruction.OpCode != OpCodes.Callvirt) return null;
        if (instruction.Operand is not MethodReference m) return null;

        // Strip generic instantiation: Task<int> -> Task`1
        var decl = m.DeclaringType;
        var typeName = decl is GenericInstanceType git
            ? git.ElementType.FullName
            : decl.FullName;

        return Blocking.TryGetValue((typeName, m.Name), out var label) ? label : null;
    }
}
