# CallgraphClosure.MustNotRecurse

A Roslyn analyzer (plus optional Cecil IL post-pass) that enforces a `[MustNotRecurse]` contract on annotated methods. Flags direct recursion plus any cycle reachable through the transitive call closure.

> **Pre-release.** Early-stage package; APIs may change. See the repo's `docs/ROADMAP.md` for known limits.

## Install

```sh
dotnet add package CallgraphClosure.MustNotRecurse --prerelease
```

## Use

```csharp
using CallgraphClosure.Attributes;

internal sealed class Demo
{
    [MustNotRecurse]
    public int Compute(int n) => n <= 1 ? 1 : n * Helper(n);   // CGC001: unannotated callee

    // IL post-pass detects the transitive cycle: Compute -> Helper -> Compute (CGC003 "recursion")
    private int Helper(int n) => Compute(n - 1);
}
```

Diagnostics:

| ID | Meaning |
|---|---|
| `CGC001` | Annotated method calls an unannotated method — annotate the callee or stop calling it |
| `CGC002` | Annotated method calls an external method whose status the analyzer can't verify; resolved by the IL post-pass |
| `CGC003` | Annotated method participates in a cycle (direct or transitive) |

## Repo

[github.com/pawlos/covenant](https://github.com/pawlos/covenant) — full design notes, IL post-pass details, and sibling property analyzers (`MustNotAllocate`, `MustNotThrow`, `MustNotBlock`).

MIT licensed.
