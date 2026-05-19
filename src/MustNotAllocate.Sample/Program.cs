using CallgraphClosure.Attributes;

// Toy "audio tick" loop — two intentional violations for the writeup.

var runner = new HotLoop();
while (true)
{
    runner.Tick(42);
}

internal sealed class HotLoop
{
    [MustNotAllocate]
    public void Tick(int sample)
    {
        // Violation 1: CGC002 (external boundary) — Console.WriteLine is external.
        System.Console.WriteLine(sample);

        // Violation 2: CGC003 (array allocation).
        var scratch = new int[16];
        _ = scratch;
    }
}
