# CallgraphClosure.MustNotAllocate

A Roslyn analyzer (plus optional Cecil IL post-pass) that enforces a `[MustNotAllocate]` contract on annotated methods. Flags allocations and unannotated calls reachable through the transitive call closure.

> **Pre-release.** This is an early-stage package. APIs and diagnostics may change. Virtual/interface dispatch is not yet covered — see the repo's `docs/ROADMAP.md`.

## Install

```sh
dotnet add package CallgraphClosure.MustNotAllocate --prerelease
```

## Use

```csharp
using CallgraphClosure.Attributes;

internal sealed class HotLoop
{
    [MustNotAllocate]
    public void Tick(int n)
    {
        Console.WriteLine(n);          // CGC002: external call, deferred to IL pass
        var buf = new int[16];         // CGC003: array allocation in annotated method
    }
}
```

Diagnostics:

| ID | Meaning |
|---|---|
| `CGC001` | Annotated method calls an unannotated method — annotate the callee or stop calling it |
| `CGC002` | Annotated method calls an external (BCL/3rd-party) method whose status the analyzer can't verify; resolved by the IL post-pass |
| `CGC003` | Annotated method directly performs a forbidden operation (here: an allocation) |

## How it works

The Roslyn analyzer walks the call graph from each `[MustNotAllocate]` method through whatever source it can see and reports any direct violations plus any forbidden operation found along an annotated transitive path. Calls into external assemblies (where source isn't available at edit time) are flagged as deferred — the optional IL post-pass (`CallgraphClosure.ILCheck`) resolves those at build time by walking the IL of the loaded assembly graph.

## Repo

[github.com/pawlos/covenant](https://github.com/pawlos/covenant) — full design notes, IL post-pass details, and the other property analyzers in the family (`MustNotThrow`, `MustNotBlock`, `MustNotRecurse`).

MIT licensed.
