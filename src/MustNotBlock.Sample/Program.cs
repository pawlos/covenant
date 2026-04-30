using System.Threading;
using System.Threading.Tasks;
using CallgraphClosure.Attributes;

// Toy "tick handler" — two intentional [MustNotBlock] violations for demonstration.

var ticker = new Ticker();
ticker.Tick();

internal sealed class Ticker
{
    [MustNotBlock]
    public void Tick()
    {
        // Violation 1: CGC003 — Thread.Sleep is a synchronous block.
        Thread.Sleep(10);

        // Violation 2: CGC003 — sync-over-async via Task<T>.Result.
        var result = ComputeAsync().Result;
        _ = result;
    }

    private Task<int> ComputeAsync() => Task.FromResult(42);
}
