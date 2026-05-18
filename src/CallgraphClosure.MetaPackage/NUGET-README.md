# CallgraphClosure

Meta-package for the [CallgraphClosure](https://github.com/pawlos/covenant) family of Roslyn property analyzers. Installing this pulls in all four current properties at once.

> **Pre-release.** Early-stage. The properties enforce contracts across the transitive call closure of annotated methods.

## Install

```sh
dotnet add package CallgraphClosure --prerelease
```

## What you get

| Package | Attribute | Catches |
|---|---|---|
| `CallgraphClosure.MustNotAllocate` | `[MustNotAllocate]` | Allocations (newobj/newarr/box) anywhere in the transitive closure |
| `CallgraphClosure.MustNotThrow` | `[MustNotThrow]` | Throw statements anywhere in the transitive closure |
| `CallgraphClosure.MustNotBlock` | `[MustNotBlock]` | Thread.Sleep, Task.Wait/.Result, sync-over-async anywhere in the transitive closure |
| `CallgraphClosure.MustNotRecurse` | `[MustNotRecurse]` | Direct recursion plus transitive cycles |

Each property also reports `CGC001` for calls into unannotated user methods (the missing-annotation contract violation) and `CGC002` for calls into external/BCL methods (which can be resolved by the optional `CallgraphClosure.ILCheck` post-pass).

## Want only one property?

If you don't want all four, install the specific package(s) you need instead — each works standalone.

```sh
dotnet add package CallgraphClosure.MustNotAllocate --prerelease
```

## Repo

[github.com/pawlos/covenant](https://github.com/pawlos/covenant) — full design notes, IL post-pass details, ROADMAP, and the writeup explaining the technique.

MIT licensed.
