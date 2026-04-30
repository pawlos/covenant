using CallgraphClosure.Attributes;

// Toy "factorial via helper" — demonstrates a transitive recursion that the IL pass
// catches even though the source-level Roslyn analyzer cannot.

var demo = new Demo();
_ = demo.Compute(3);

internal sealed class Demo
{
    [MustNotRecurse]
    public int Compute(int n) => n <= 1 ? 1 : n * Helper(n);

    // Edit time (Roslyn): CGC001 — calls unannotated 'Helper'. Either annotate Helper
    // or stop calling it.
    //
    // Build time (IL pass): CGC003 with label "recursion" — Helper transitively closes
    // the loop back to Compute. Chain: [Compute, Helper, Compute].
    private int Helper(int n) => Compute(n - 1);
}
