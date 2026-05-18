# CallgraphClosure.MustNotThrow

A Roslyn analyzer (plus optional Cecil IL post-pass) that enforces a `[MustNotThrow]` contract on annotated methods. Flags `throw` statements and unannotated calls reachable through the transitive call closure.

> **Pre-release.** Early-stage package; APIs may change. See the repo's `docs/ROADMAP.md` for known limits.

## Install

```sh
dotnet add package CallgraphClosure.MustNotThrow --prerelease
```

## Use

```csharp
using CallgraphClosure.Attributes;

internal static class Validator
{
    [MustNotThrow]
    public static bool TryValidate(string input, out string error)
    {
        if (input is null) throw new ArgumentNullException(nameof(input)); // CGC003: throw in annotated method
        error = "";
        return true;
    }
}
```

Diagnostics:

| ID | Meaning |
|---|---|
| `CGC001` | Annotated method calls an unannotated method — annotate the callee or stop calling it |
| `CGC002` | Annotated method calls an external method whose status the analyzer can't verify; resolved by the IL post-pass |
| `CGC003` | Annotated method directly performs a `throw` |

## Repo

[github.com/pawlos/covenant](https://github.com/pawlos/covenant) — full design notes, IL post-pass details, and sibling property analyzers (`MustNotAllocate`, `MustNotBlock`, `MustNotRecurse`).

MIT licensed.
