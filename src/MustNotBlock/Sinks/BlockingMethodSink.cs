using System.Collections.Generic;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotBlock.Sinks;

public sealed class BlockingMethodSink : ISink
{
    private static readonly Dictionary<(string Type, string Method), string> Blocking = new()
    {
        { ("System.Threading.Thread", "Sleep"), "Thread.Sleep" },
        { ("System.Threading.Tasks.Task", "Wait"), "Task.Wait" },
        { ("System.Threading.Tasks.Task", "WaitAll"), "Task.WaitAll" },
        { ("System.Threading.Tasks.Task", "WaitAny"), "Task.WaitAny" },
        { ("System.Threading.WaitHandle", "WaitOne"), "WaitHandle.WaitOne" },
        { ("System.Threading.WaitHandle", "WaitAll"), "WaitHandle.WaitAll" },
        { ("System.Threading.WaitHandle", "WaitAny"), "WaitHandle.WaitAny" },
        { ("System.Threading.Monitor", "Wait"), "Monitor.Wait" },
        { ("System.Threading.SemaphoreSlim", "Wait"), "SemaphoreSlim.Wait" },
        { ("System.Threading.ManualResetEventSlim", "Wait"), "ManualResetEventSlim.Wait" },
        { ("System.Threading.CountdownEvent", "Wait"), "CountdownEvent.Wait" },
        { ("System.Threading.Barrier", "SignalAndWait"), "Barrier.SignalAndWait" },
    };

    public string? Match(IOperation op)
    {
        if (op is not IInvocationOperation inv) return null;
        var method = inv.TargetMethod.OriginalDefinition;
        var typeName = method.ContainingType.ConstructedFrom
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");
        return Blocking.TryGetValue((typeName, method.Name), out var label) ? label : null;
    }
}
