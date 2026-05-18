# CallgraphClosure.Attributes

Attribute types for the [CallgraphClosure](https://github.com/pawlos/covenant) family of Roslyn property analyzers. You normally don't install this directly — it's pulled in transitively when you add one of the analyzer packages.

> **Pre-release.**

Provides:

- `[MustNotAllocate]` — see [CallgraphClosure.MustNotAllocate](https://www.nuget.org/packages/CallgraphClosure.MustNotAllocate/)
- `[MustNotThrow]` — see [CallgraphClosure.MustNotThrow](https://www.nuget.org/packages/CallgraphClosure.MustNotThrow/)
- `[MustNotBlock]` — see [CallgraphClosure.MustNotBlock](https://www.nuget.org/packages/CallgraphClosure.MustNotBlock/)
- `[MustNotRecurse]` — see [CallgraphClosure.MustNotRecurse](https://www.nuget.org/packages/CallgraphClosure.MustNotRecurse/)
- `[AmortizedAllocation]` — escape hatch for explicitly-amortized allocations (used by `MustNotAllocate`)

MIT licensed.
