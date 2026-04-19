# `[MustNotThrow]` — Second-Property Demo Design

**Status:** Draft (2026-04-19)
**Scope:** Ship a second propagating property using the existing M1/M2/M2.5 infrastructure. Attribute + Roslyn analyzer + IL walker binding + unit tests. Validates the "this generalizes" claim with a concrete commit rather than hand-wavy prose.

## Purpose

The long-form writeup's §9 ("Add your own property") shows a ~50-line `[MustNotThrow]` implementation as code snippets and promises "and you'd get it working just like `[MustNotAllocate]`." Shipping the feature turns that promise into a claim that can be checked: readers can `git checkout must-not-throw-complete` and run the test suite. It also gives the writeup a natural follow-up post ("Here's the second property built in a weekend") and proves the infrastructure is property-agnostic, not allocation-specific by accident.

**Secondary purpose:** exercise the existing analyzer from a position other than `[MustNotAllocate]`. Any hidden coupling between the core and its one shipping predicate gets flushed out here, cheaply, with a small blast radius.

## Inherited unchanged

- `CallgraphClosure.Attributes` — the shared attribute library from M2.5 adds `MustNotThrowAttribute` next to the existing `MustNotAllocateAttribute` and `AmortizedAllocationAttribute`.
- `CallgraphClosure.Core` — Roslyn analyzer base class + `Config` record + diagnostic descriptors. No changes.
- `CallgraphClosure.ILCheck.Core` — Cecil walker + `AmortizedSet` + diagnostic types. No changes.
- Three diagnostic IDs (`CGC001`/`CGC002`/`CGC003`) and their severity — same semantics as `[MustNotAllocate]`: CGC003 is ground-truth, CGC001/002 are call-site boundaries resolvable/unresolvable.
- JSON annotations file format — same `amortized-methods.json` schema is reusable if anyone ever wants a `[MustNotThrow]`-style amortized escape. None needed for M1 of this feature.
- Test harness (`CSharpAnalyzerVerifier`, `CompileFixture`) — both test projects gain new test files; neither needs harness changes.

## Decisions locked in

1. **Attribute name:** `[MustNotThrow]`. Matches the naming rhythm of `[MustNotAllocate]`, clear intent in one word.
2. **Single shared attribute location:** lives in `CallgraphClosure.Attributes`, next to the others. No separate package.
3. **Sinks:** `OpCodes.Throw` and `OpCodes.Rethrow` at IL level; `IThrowOperation` at Roslyn level (covers both `throw x;` and bare `throw;` inside catches). Label strings: `"throw"` and `"rethrow"` respectively.
4. **No amortized escape hatch for M1 of this feature.** Exceptions don't amortize semantically the way allocations do — if an exception happens, it happens. If users later discover a specific pattern that wants one (e.g., library methods that throw on misuse but are "never called with misuse in practice"), revisit. Deferred.
5. **No dedicated showcase project for M1 of this feature.** Tests prove correctness; the writeup's §9 code snippet is enough narrative. A parallel-to-the-HTTP-parser showcase (e.g., a naive-throws-internally vs optimized-pure-Result-channel error-handling pipeline) is listed as a follow-up in `docs/ROADMAP.md` item 5.
6. **Transitive coverage handles `ThrowHelper` automatically.** When a `[MustNotThrow]` method calls `ThrowHelper.ThrowArgumentNullException()`, the walker recurses, finds `throw new ArgumentNullException()` inside, emits CGC003 with the chain. This is the correct behavior — the caller *does* effectively throw when it calls `ThrowHelper`. No special casing required.

## Architecture

### Package layout

```
src/
  MustNotThrow/                          netstandard2.0 — Roslyn analyzer
    MustNotThrowAnalyzer.cs              concrete subclass of CallgraphClosureAnalyzer
    Sinks/
      ThrowOperationSink.cs              matches IThrowOperation

  MustNotThrow.ILCheck/                  net10.0 — IL walker binding
    MustNotThrowIlAnalyzer.cs            static class: AttributeFullName + Sinks
    Sinks/
      ThrowSink.cs                       matches OpCodes.Throw and OpCodes.Rethrow
```

Two new projects, mirroring the `MustNotAllocate` / `MustNotAllocate.ILCheck` pattern. No changes to existing projects.

### Attribute

Added to `src/CallgraphClosure.Attributes/MustNotThrowAttribute.cs`:

```csharp
using System;

namespace CallgraphClosure.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotThrowAttribute : Attribute { }
```

`Inherited = false` for the same reason as `[MustNotAllocate]`: virtual-override semantics for propagating attributes are their own design question.

### Roslyn sink

```csharp
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotThrow.Sinks;

public sealed class ThrowOperationSink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IThrowOperation throwOp) return null;
        // IThrowOperation covers both `throw x;` and bare `throw;` (rethrow).
        // .Exception is null for bare rethrow, non-null for throw-with-value.
        return throwOp.Exception is null ? "rethrow" : "throw";
    }
}
```

### Roslyn analyzer

```csharp
using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MustNotThrow.Sinks;

namespace MustNotThrow;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotThrowAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotThrowAnalyzer() : base(new Config(
        AttributeFullName: "CallgraphClosure.Attributes.MustNotThrowAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(new ThrowOperationSink()))) { }
}
```

No `AmortizedAttributeFullName` or `AmortizedFileName` — the escape-hatch machinery is there if anyone later wants it, but M1 of this feature ships without wiring it.

### IL sink

```csharp
using CallgraphClosure.ILCheck.Core;
using Mono.Cecil.Cil;

namespace MustNotThrow.ILCheck.Sinks;

public sealed class ThrowSink : IIlSink
{
    public string? Match(Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Throw) return "throw";
        if (instruction.OpCode == OpCodes.Rethrow) return "rethrow";
        return null;
    }
}
```

### IL analyzer binding

```csharp
using System.Collections.Immutable;
using CallgraphClosure.ILCheck.Core;
using MustNotThrow.ILCheck.Sinks;

namespace MustNotThrow.ILCheck;

public static class MustNotThrowIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotThrowAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(new ThrowSink());
}
```

## Testing strategy

Two test files, one per analyzer, following the patterns from `MustNotAllocate.Tests` and `MustNotAllocate.ILCheck.Tests`.

### Roslyn tests (`tests/MustNotThrow.Tests/MustNotThrowTests.cs`)

- **Direct throw:** `[MustNotThrow] void F() { throw new Exception(); }` → CGC003 on the throw, label `"throw"`.
- **Rethrow in catch:** `[MustNotThrow] void F() { try { ... } catch { throw; } }` → CGC003 on the bare throw, label `"rethrow"`.
- **No throw:** `[MustNotThrow] void F() { }` → silent.
- **Call to unannotated method:** `[MustNotThrow] void F() { Helper(); }` where `Helper()` has no attribute → CGC001.
- **Call to `[MustNotThrow]`-annotated method:** `[MustNotThrow] void F() { Helper(); }` where `Helper()` is also annotated → silent.
- **Call to external method:** `[MustNotThrow] void F() { Console.WriteLine("x"); }` → CGC002 (Info severity).
- **Throw inside a nested expression:** `[MustNotThrow] int F() { return x ?? throw new InvalidOperationException(); }` → CGC003, label `"throw"`.
- **Silent when attribute package not referenced:** source that uses `[MustNotThrow]` but whose project doesn't reference `CallgraphClosure.Attributes` → no diagnostics (attribute not resolvable, analyzer silently no-ops).

Baseline target: 8 tests.

### IL tests (`tests/MustNotThrow.ILCheck.Tests/MustNotThrowIlTests.cs`)

- **Direct throw:** compiles source with a `[MustNotThrow]` method that throws; Cecil walker finds the throw opcode; CGC003 with chain length 1.
- **Transitive throw via unannotated helper:** `[MustNotThrow] F → Helper → throw`; walker recurses through unannotated Helper, finds the throw, emits CGC003 with chain length 2.
- **Transitive throw via `ThrowHelper` pattern:** `[MustNotThrow] F → ThrowIfNull(x) → throw new ArgumentNullException()`; CGC003 emitted against F with the chain showing ThrowIfNull in the middle.
- **Annotated callee terminates walk:** `[MustNotThrow] F → [MustNotThrow] G → throw`; G's throw fires CGC003 against G, not against F. F's walk stops at G.
- **Rethrow detection:** IL-level `rethrow` opcode matched with label `"rethrow"`.

Baseline target: 5 tests.

### Suite target

After M1 of this feature: 42 + 8 + 5 = **55 tests**.

### Integration with showcase (optional)

A trivial `[MustNotThrow] bool TryValidate(ReadOnlySpan<byte> input, out Error error)` applied to the existing HTTP showcase's Optimized parser would demonstrate the feature in the context readers already understand. Scope TBD — include as an 8th task if cheap, defer if it complicates the plan.

## Non-goals (this milestone)

| Case | Behavior | Deferred to |
|---|---|---|
| Amortized-throw escape hatch | No attribute, no JSON file | Revisit if a specific use case emerges |
| `[MustNotThrow]`-specific showcase project (analog to HTTP parser) | Not in this milestone | `docs/ROADMAP.md` item 5 |
| `try/catch` that fully swallows the exception | Walker still emits CGC003 on the `throw` inside the `try`; caller must refactor to not throw at all | Correct behavior — catching doesn't uncommit from the `[MustNotThrow]` promise at the caller boundary |
| Finalizer semantics (`~Foo()` can throw) | Out of scope; finalizers are their own design question | Someday |
| Distinguishing "throws only under contract violation" vs "throws unconditionally" | No distinction — any reachable `throw` fires | Would require explicit `[ContractViolation]` or similar marker — separate design |

## Success criteria

M1 of this feature is done when:

1. `CallgraphClosure.Attributes.MustNotThrowAttribute` exists, referenced by source tree.
2. `src/MustNotThrow/` and `src/MustNotThrow.ILCheck/` projects build clean.
3. All 13 new tests pass (8 Roslyn + 5 IL).
4. Full suite: 55 tests passing, zero failures.
5. Tag `must-not-throw-complete` applied.
6. Writeup's §9 code snippet matches the shipped code (minor textual alignment if necessary).

## Open uncertainties (flagged, not blocking)

**`IThrowOperation.Exception` null-check for rethrow detection.** The Roslyn docs say `Exception` is null for bare `throw;` inside a `catch` and non-null for `throw e;`. Verify empirically during Task 1 of the plan. If the API shape has changed, adjust the sink; fallback is checking `op.Syntax` is a `ThrowStatementSyntax` with no expression.

**IL `Rethrow` opcode vs throw-inside-catch.** At IL level, `rethrow` is only emitted for bare `throw;` statements inside catch blocks. `throw e;` even inside a catch emits `throw`. The `ThrowSink` correctly handles both.

**Analyzer-on-analyzer interaction.** If a consumer references both `MustNotAllocate` and `MustNotThrow` analyzers in the same project, each analyzer's `CompilationStartAction` will resolve its own attribute. No shared state, no conflicts expected — but worth a quick manual verification by applying both attributes to the same method and confirming both sets of diagnostics appear independently.
