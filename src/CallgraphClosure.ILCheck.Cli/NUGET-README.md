# CallgraphClosure.ILCheck

Cecil-based IL post-pass for the [CallgraphClosure](https://github.com/pawlos/covenant) property analyzers. Resolves the edit-time-deferred `CGC002` diagnostics by walking the IL of the built assembly graph — catches violations that cross assembly boundaries where the Roslyn analyzer can't see the source.

> **Pre-release.** This 0.1.0-preview1 release currently supports `[MustNotAllocate]` only. Support for `[MustNotThrow]`, `[MustNotBlock]`, and `[MustNotRecurse]` lands in subsequent releases. The Roslyn analyzers for those properties already exist as separate packages — they just don't have IL-pass coverage yet.

## Install

```sh
dotnet tool install --global CallgraphClosure.ILCheck --prerelease
```

## Use

```sh
cgc-ilcheck path/to/YourAssembly.dll
cgc-ilcheck --amortized-file bcl-amortized.json path/to/YourAssembly.dll
```

The `--amortized-file` flag accepts a JSON list of fully-qualified method names whose allocations are amortized and should not trigger `CGC003`. See the repo's `src/CallgraphClosure.ILCheck.Cli/bcl-amortized.json` for a starter list covering common BCL hot-path helpers.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | No diagnostics |
| `1` | Diagnostics emitted (build-failing in CI) |
| `2` | Bad arguments or input file missing |

## Repo

[github.com/pawlos/covenant](https://github.com/pawlos/covenant) — full design notes, sibling property analyzers, and the ROADMAP showing what's next.

MIT licensed.
