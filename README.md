# dotnet-callgraph-closure

A .NET analog of Ferrocene's callgraph-closure lint. Four Roslyn analyzers (plus an optional Cecil IL post-pass) that enforce method-level contracts across the **transitive call closure** of every annotated method: if you mark a method `[MustNotAllocate]`, the analyzer walks everything it reaches and flags allocations, throws, blocking calls, or recursion — depending on which property you picked.

Published as `0.1.0-preview2` on nuget.org under the `CallgraphClosure.*` prefix.

## Install

Pick **one** of these — they're not additive:

```sh
# Everything — pulls in all four properties via the meta-package.
dotnet add package CallgraphClosure --prerelease

# Or just the one property you want:
dotnet add package CallgraphClosure.MustNotAllocate --prerelease
dotnet add package CallgraphClosure.MustNotThrow    --prerelease
dotnet add package CallgraphClosure.MustNotBlock    --prerelease
dotnet add package CallgraphClosure.MustNotRecurse  --prerelease
```

The `CallgraphClosure.Attributes` package (which carries the `[MustNotAllocate]` etc. attribute symbols) is pulled in automatically as a dependency. You don't need to add it explicitly unless you're splitting attribute declarations into a separate project.

## Minimum working example

```csharp
using CallgraphClosure.Attributes;

internal sealed class HotLoop
{
    [MustNotAllocate]
    public void Tick(int sample)
    {
        Console.WriteLine(sample);   // CGC002: external call, resolved by IL pass
        var scratch = new int[16];   // CGC003: array allocation in annotated method
    }
}
```

`dotnet build` will print:

```
warning CGC002: Method 'Tick' is annotated [MustNotAllocateAttribute] but calls external method 'WriteLine'…
warning CGC003: Method 'Tick' is annotated [MustNotAllocateAttribute] but contains a array allocation
```

## Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| `CGC001` | Warning | Annotated method calls an unannotated method in your own source. Annotate the callee or drop the call. |
| `CGC002` | Info | Annotated method calls an external (BCL / third-party) method whose status can't be checked at edit time. Resolved by the IL post-pass. |
| `CGC003` | Warning | Annotated method directly performs a forbidden operation (allocation / throw / blocking call / recursion). |

## The four properties

| Package | Attribute | What CGC003 catches |
|---|---|---|
| `CallgraphClosure.MustNotAllocate` | `[MustNotAllocate]` | `newobj`, `newarr`, boxing, string concat, params arrays |
| `CallgraphClosure.MustNotThrow`    | `[MustNotThrow]`    | `throw` statements and `throw new …` expressions |
| `CallgraphClosure.MustNotBlock`    | `[MustNotBlock]`    | `Thread.Sleep`, `Task.Wait` / `.Result`, sync-over-async |
| `CallgraphClosure.MustNotRecurse`  | `[MustNotRecurse]`  | Direct recursion plus transitive cycles |

Each property also reports `CGC001` (missing annotation on user-code callee) and `CGC002` (external boundary).

## Optional: IL post-pass

The Roslyn analyzer can't see across assembly boundaries. The Cecil-based post-pass walks the IL of the built assembly graph and resolves the deferred `CGC002` diagnostics:

```sh
dotnet tool install --global CallgraphClosure.ILCheck --prerelease
cgc-ilcheck path/to/YourAssembly.dll
cgc-ilcheck --amortized-file bcl-amortized.json path/to/YourAssembly.dll
```

The current preview IL pass covers `[MustNotAllocate]` only; the other three properties land in subsequent releases.

## Known gotchas

- **Virtual / interface dispatch is not resolved.** An `IFoo.Bar()` call only inspects the declared interface method — implementations are invisible to the analyzer. Tracked as M3 on `docs/ROADMAP.md`.

- **`CGC002` noise.** External calls are reported as info-level by design. They're the IL pass's job to resolve at build time; the edit-time analyzer flags them so nothing slips through silently.

- **`0.1.0-preview1` only.** Methods at file scope (next to top-level statements) compile to local functions of the synthesized `<Main>$`, and `preview1`'s analyzer skipped them. Fixed in `0.1.0-preview2`. If you're stuck on `preview1`, wrap the method in a class.

## Layout

```
src/
  CallgraphClosure.Attributes/    — attribute types only, netstandard2.0
  CallgraphClosure.Core/          — shared analyzer base + diagnostic descriptors
  MustNotAllocate/                — Roslyn analyzer (property-specific sinks)
  MustNotAllocate.Sample/         — minimal consumer that intentionally violates the contract
  MustNotAllocate.ILCheck/        — Cecil IL post-pass for MustNotAllocate
  MustNot{Throw,Block,Recurse}/   — analogous trios for the other three properties
  CallgraphClosure.ILCheck.Cli/   — `cgc-ilcheck` global tool
  CallgraphClosure.MetaPackage/   — `CallgraphClosure` meta package
  Showcase.Http.{Naive,Optimized,Common}/ — HTTP request-line parser comparison with BDN
tests/
  MustNot*.Tests/                 — Roslyn analyzer unit tests per property
  MustNot*.ILCheck.Tests/         — IL post-pass tests per property
docs/
  ROADMAP.md                      — what's next, what's deferred, what's deliberately out of scope
  writeup/                        — long-form article draft + social variants
  superpowers/                    — specs and plans per milestone
```

## License

MIT. See `LICENSE`.
