# CallgraphClosure.MustNotBlock

A Roslyn analyzer (plus optional Cecil IL post-pass) that enforces a `[MustNotBlock]` contract on annotated methods. Flags `Thread.Sleep`, `Task.Wait`/`.Result`, sync-over-async patterns, and unannotated calls reachable through the transitive call closure.

> **Pre-release.** Early-stage package; APIs may change. See the repo's `docs/ROADMAP.md` for known limits.

## Install

```sh
dotnet add package CallgraphClosure.MustNotBlock --prerelease
```

## Use

```csharp
using CallgraphClosure.Attributes;

internal sealed class Ticker
{
    [MustNotBlock]
    public int Tick()
    {
        Thread.Sleep(10);              // CGC003: synchronous block
        return ComputeAsync().Result;  // CGC003: sync-over-async + CGC001: unannotated callee
    }

    private async Task<int> ComputeAsync() => await Task.FromResult(42);
}
```

Diagnostics:

| ID | Meaning |
|---|---|
| `CGC001` | Annotated method calls an unannotated method — annotate the callee or stop calling it |
| `CGC002` | Annotated method calls an external method whose status the analyzer can't verify; resolved by the IL post-pass |
| `CGC003` | Annotated method directly performs a blocking operation |

## Repo

[github.com/pawlos/covenant](https://github.com/pawlos/covenant) — full design notes, IL post-pass details, and sibling property analyzers (`MustNotAllocate`, `MustNotThrow`, `MustNotRecurse`).

MIT licensed.
