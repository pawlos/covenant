# `[MustNotBlock]` — Synchronous-Blocking Property

**Status:** Draft (2026-04-30)
**Scope:** Ship a third propagating property using the existing M1/M2/M2.5 infrastructure. Attribute + Roslyn analyzer + IL walker binding + unit tests. Demonstrates that the framework handles *named-method* sinks (matched by FQN against a known list), not just *operation-shape* sinks (`IThrowOperation`, `IObjectCreationOperation`).

## Purpose

The first two shipped properties (`[MustNotAllocate]`, `[MustNotThrow]`) both work by matching IL/Roslyn operations *structurally* — any newobj is an allocation, any throw is a throw. `[MustNotBlock]` is fundamentally different: blocking-ness isn't a property of the IL opcode, it's a property of *which method* is being called. A `Call` to `Thread.Sleep` is blocking; a `Call` to `Task.Run` isn't. The sink has to know its enemies by name.

This is the most common shape of real-world property analysis (think: trim-unsafe APIs, AOT-incompatible APIs, deprecated APIs). Shipping `[MustNotBlock]` proves the framework can express it without core changes — only the sinks differ.

**Secondary purpose:** real production value. "Don't block on this thread" is one of the most-wanted lints in .NET (the Roslyn analyzer host itself enforces a related rule manually). A turnkey `[MustNotBlock]` is genuinely useful for hot async paths, real-time threads, and the analyzer-host self-application story.

## Inherited unchanged

- `CallgraphClosure.Attributes` — the shared attribute library adds `MustNotBlockAttribute` next to the existing two.
- `CallgraphClosure.Core` — Roslyn analyzer base class + `Config` record + diagnostic descriptors. No changes.
- `CallgraphClosure.ILCheck.Core` — Cecil walker + `AmortizedSet` + diagnostic types. No changes.
- Diagnostic IDs (`CGC001`/`CGC002`/`CGC003`) and severity — same semantics.
- Test harness (`CSharpAnalyzerVerifier`, `CompileFixture`) — patterns reused unchanged.

## Decisions locked in

1. **Attribute name:** `[MustNotBlock]`. Matches the rhythm of the others, clear intent.
2. **Single shared attribute location:** lives in `CallgraphClosure.Attributes`, next to the others.
3. **No amortized escape hatch for v1.** Blocking is pass/fail per call site; no "amortized blocking" concept exists in practice. The escape hatch infrastructure stays available for any future need but isn't wired.
4. **Sink list lives in code, not JSON, for v1.** The BCL blocking primitives are a small, stable, well-known set. JSON-based extensibility (so users can add their own blocking methods) is a v2 enhancement, parallel to how `bcl-amortized.json` was added to `[MustNotAllocate]` after the initial code-only release.
5. **Match by `(declaring type, method name)` pair, not by full overload signature.** All overloads of `Thread.Sleep` block; all overloads of `Task.Wait` block. We don't need overload-level discrimination, and it keeps the FQN string-format problem (Roslyn's display vs Cecil's `FullName` vs generic-instantiation noise) from leaking into per-overload tedium.
6. **Sinks are deliberately conservative for v1.** The list is specific synchronous-wait BCL primitives. Excluded by design (and documented as such): `lock` statements (very common, mostly fine if uncontended), `GetAwaiter().GetResult()` chains (overlaps with compiler-generated async state machines, false-positive risk), generic synchronous I/O (`Stream.Read`, `Socket.Receive` — these are blocking but the user usually knows; needs a different opinion).
7. **Async state machine internals are NOT flagged.** A `[MustNotBlock] async Task F() { await G(); }` should be silent. The compiler-generated state machine calls `awaiter.GetResult()` on completed awaiters — those are explicitly *not* on the sink list to avoid flagging every async method.

## Sink list (v1)

Both Roslyn and IL sinks match this set, with each side using its own FQN format. Match key is `(declaring-type-FullName, method-name)`:

| Type | Method | Label |
|---|---|---|
| `System.Threading.Thread` | `Sleep` | `Thread.Sleep` |
| `System.Threading.Tasks.Task` | `Wait` | `Task.Wait` |
| `System.Threading.Tasks.Task` | `WaitAll` | `Task.WaitAll` |
| `System.Threading.Tasks.Task` | `WaitAny` | `Task.WaitAny` |
| `System.Threading.Tasks.Task<T>` | `get_Result` (property getter) | `Task.Result` |
| `System.Threading.WaitHandle` | `WaitOne` | `WaitHandle.WaitOne` |
| `System.Threading.WaitHandle` | `WaitAll` | `WaitHandle.WaitAll` |
| `System.Threading.WaitHandle` | `WaitAny` | `WaitHandle.WaitAny` |
| `System.Threading.Monitor` | `Wait` | `Monitor.Wait` |
| `System.Threading.SemaphoreSlim` | `Wait` (NOT `WaitAsync`) | `SemaphoreSlim.Wait` |
| `System.Threading.ManualResetEventSlim` | `Wait` (NOT `WaitAsync`) | `ManualResetEventSlim.Wait` |
| `System.Threading.CountdownEvent` | `Wait` | `CountdownEvent.Wait` |
| `System.Threading.Barrier` | `SignalAndWait` | `Barrier.SignalAndWait` |

The label is the user-visible string in the CGC003 message: *"Method 'F' is annotated [MustNotBlockAttribute] but contains a Thread.Sleep"*.

## Architecture

### Package layout

```
src/
  MustNotBlock/                          netstandard2.0 — Roslyn analyzer
    MustNotBlockAnalyzer.cs              concrete subclass of CallgraphClosureAnalyzer
    Sinks/
      BlockingMethodSink.cs              matches IInvocationOperation against the FQN list
      BlockingPropertySink.cs            matches IPropertyReferenceOperation for Task<T>.Result

  MustNotBlock.ILCheck/                  net10.0 — IL walker binding
    MustNotBlockIlAnalyzer.cs            static class: AttributeFullName + Sinks
    Sinks/
      BlockingCallSink.cs                matches Call/Callvirt against the FQN list
                                         (handles get_Result via the same path — at IL,
                                         property getters are just methods)
```

Two new projects, mirroring the existing pattern.

### Attribute

```csharp
using System;

namespace CallgraphClosure.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotBlockAttribute : Attribute { }
```

### Roslyn sinks

```csharp
public sealed class BlockingMethodSink : ISink
{
    private static readonly Dictionary<(string Type, string Method), string> Blocking = new()
    {
        { ("System.Threading.Thread", "Sleep"), "Thread.Sleep" },
        { ("System.Threading.Tasks.Task", "Wait"), "Task.Wait" },
        { ("System.Threading.Tasks.Task", "WaitAll"), "Task.WaitAll" },
        { ("System.Threading.Tasks.Task", "WaitAny"), "Task.WaitAny" },
        { ("System.Threading.WaitHandle", "WaitOne"), "WaitHandle.WaitOne" },
        { ("System.Threading.WaitHandle", "WaitAll"), "WaitHandle.WaitAll" },
        { ("System.Threading.WaitHandle", "WaitAny"), "WaitHandle.WaitAny" },
        { ("System.Threading.Monitor", "Wait"), "Monitor.Wait" },
        { ("System.Threading.SemaphoreSlim", "Wait"), "SemaphoreSlim.Wait" },
        { ("System.Threading.ManualResetEventSlim", "Wait"), "ManualResetEventSlim.Wait" },
        { ("System.Threading.CountdownEvent", "Wait"), "CountdownEvent.Wait" },
        { ("System.Threading.Barrier", "SignalAndWait"), "Barrier.SignalAndWait" },
    };

    public string? Match(IOperation op)
    {
        if (op is not IInvocationOperation inv) return null;
        var method = inv.TargetMethod.OriginalDefinition;
        var typeName = method.ContainingType.ConstructedFrom
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");
        return Blocking.TryGetValue((typeName, method.Name), out var label) ? label : null;
    }
}

public sealed class BlockingPropertySink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IPropertyReferenceOperation pr) return null;
        // Only Task<T>.Result on the read side.
        var prop = pr.Property.OriginalDefinition;
        if (prop.Name != "Result") return null;
        var typeName = prop.ContainingType.ConstructedFrom
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");
        return typeName == "System.Threading.Tasks.Task<TResult>" ? "Task.Result" : null;
    }
}
```

### Roslyn analyzer

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotBlockAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotBlockAnalyzer() : base(new Config(
        AttributeFullName: "CallgraphClosure.Attributes.MustNotBlockAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(
            new BlockingMethodSink(),
            new BlockingPropertySink()))) { }
}
```

### IL sink

At IL level, property getters are just methods (`get_Result`), so a single sink covers both call kinds.

```csharp
public sealed class BlockingCallSink : IIlSink
{
    private static readonly Dictionary<(string Type, string Method), string> Blocking = new()
    {
        { ("System.Threading.Thread", "Sleep"), "Thread.Sleep" },
        { ("System.Threading.Tasks.Task", "Wait"), "Task.Wait" },
        { ("System.Threading.Tasks.Task", "WaitAll"), "Task.WaitAll" },
        { ("System.Threading.Tasks.Task", "WaitAny"), "Task.WaitAny" },
        { ("System.Threading.Tasks.Task`1", "get_Result"), "Task.Result" },
        { ("System.Threading.WaitHandle", "WaitOne"), "WaitHandle.WaitOne" },
        { ("System.Threading.WaitHandle", "WaitAll"), "WaitHandle.WaitAll" },
        { ("System.Threading.WaitHandle", "WaitAny"), "WaitHandle.WaitAny" },
        { ("System.Threading.Monitor", "Wait"), "Monitor.Wait" },
        { ("System.Threading.SemaphoreSlim", "Wait"), "SemaphoreSlim.Wait" },
        { ("System.Threading.ManualResetEventSlim", "Wait"), "ManualResetEventSlim.Wait" },
        { ("System.Threading.CountdownEvent", "Wait"), "CountdownEvent.Wait" },
        { ("System.Threading.Barrier", "SignalAndWait"), "Barrier.SignalAndWait" },
    };

    public string? Match(Instruction instruction)
    {
        if (instruction.OpCode != OpCodes.Call &&
            instruction.OpCode != OpCodes.Callvirt) return null;
        if (instruction.Operand is not MethodReference m) return null;

        // Strip generic instantiation: Task<int> -> Task`1
        var decl = m.DeclaringType;
        var typeName = decl is GenericInstanceType git
            ? git.ElementType.FullName
            : decl.FullName;

        return Blocking.TryGetValue((typeName, m.Name), out var label) ? label : null;
    }
}
```

### IL analyzer binding

```csharp
public static class MustNotBlockIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotBlockAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(new BlockingCallSink());
}
```

## Testing strategy

### Roslyn tests (`tests/MustNotBlock.Tests/MustNotBlockTests.cs`)

- **Direct `Thread.Sleep`:** `[MustNotBlock] void F() { Thread.Sleep(100); }` → CGC003 with label `"Thread.Sleep"`.
- **Direct `Task.Wait`:** `[MustNotBlock] void F(Task t) { t.Wait(); }` → CGC003 with label `"Task.Wait"`.
- **Direct `Task<T>.Result`:** `[MustNotBlock] int F(Task<int> t) { return t.Result; }` → CGC003 with label `"Task.Result"`.
- **`SemaphoreSlim.Wait` (sync overload):** `[MustNotBlock] void F(SemaphoreSlim s) { s.Wait(); }` → CGC003 with label `"SemaphoreSlim.Wait"`.
- **`SemaphoreSlim.WaitAsync` (async overload):** `[MustNotBlock] async Task F(SemaphoreSlim s) { await s.WaitAsync(); }` → silent. Verifies the sink discriminates `Wait` from `WaitAsync` (separate methods, only one blocks).
- **No blocking:** `[MustNotBlock] void F() { }` → silent.
- **Call to unannotated method (CGC001):** `[MustNotBlock] void F() { Helper(); }` where `Helper` is unannotated → CGC001 (boundary diagnostic).
- **Call to annotated method (silent):** `[MustNotBlock] void F() { Helper(); }` where `Helper` is also `[MustNotBlock]` → silent (annotated callee is trusted).
- **Async method awaiting another async method:** `[MustNotBlock] async Task F() { await Task.Yield(); }` → silent. Verifies the compiler-generated state machine plumbing (`awaiter.GetResult()` on completed awaiters, `AsyncTaskMethodBuilder` calls, etc.) does not leak as a false positive.

Baseline target: 9 tests.

### IL tests (`tests/MustNotBlock.ILCheck.Tests/MustNotBlockIlTests.cs`)

- **Direct `Thread.Sleep`:** CGC003 attributed to caller, chain length 1, label `"Thread.Sleep"`.
- **Transitive blocking via helper:** `[MustNotBlock] F → Helper → Thread.Sleep`. Chain length 2.
- **`Task<T>.Result` at IL (callvirt to `get_Result`):** verifies generic-instantiation handling — `Task<int>::get_Result()` resolves to the generic `Task`1::get_Result()` entry.
- **Annotated callee terminates walk:** `[MustNotBlock] F → [MustNotBlock] G → Thread.Sleep`. G's diagnostic fires against G, not F.
- **Async method (sanity):** annotated `async Task F() { await G(); }` produces no diagnostics from state machine plumbing.

Baseline target: 5 tests.

### Suite target

After M1 of this feature: 55 (existing) + 9 + 5 = **69 tests**.

### Showcase integration (deferred)

`[MustNotBlock]` doesn't have a natural fit on the existing HTTP parser showcase — `TryParse` is allocation-free and exception-free but doesn't call any of the blocking sinks regardless. A purpose-built showcase (sync-over-async wrapper vs proper async pipeline) is more appropriate but out of scope for v1. Listed in `docs/ROADMAP.md` as a follow-up if `[MustNotBlock]` graduates to a writeup post.

## Non-goals (this milestone)

| Case | Behavior | Deferred to |
|---|---|---|
| `lock` statements (`Monitor.Enter`/`Monitor.Exit`) | Not flagged | v2 — too noisy for v1, needs an opt-in mode |
| `GetAwaiter().GetResult()` outside compiler-generated state machines | Not flagged | v2 — needs syntactic-context analysis to avoid false positives in valid async methods |
| Generic synchronous I/O (`Stream.Read`, `Socket.Receive`, `File.ReadAllText`, etc.) | Not flagged | v2 — different opinion (these *do* block but the user usually intends to) |
| User-extensible blocking list via JSON | Not in v1 | v2 — parallel to how `bcl-amortized.json` was added to `[MustNotAllocate]` after the initial release |
| Async-over-sync (`Task.Run(() => SyncCall())` to escape) | Not analyzed; the body of the lambda gets walked separately if annotated | Out of scope — different problem |

## Success criteria

M1 of this feature is done when:

1. `CallgraphClosure.Attributes.MustNotBlockAttribute` exists, referenced by source tree.
2. `src/MustNotBlock/` and `src/MustNotBlock.ILCheck/` projects build clean.
3. All 14 new tests pass (9 Roslyn + 5 IL).
4. Full suite: 69 tests passing, zero failures.
5. Tag `must-not-block-complete` applied.
6. The async-state-machine sanity test is green — no false positives on plain `await` chains.

## Open uncertainties (flagged, not blocking)

**Roslyn FQN format for generic types.** `Task<TResult>.Result` may display as `System.Threading.Tasks.Task<TResult>.Result` or `System.Threading.Tasks.Task<T>.Result` depending on `SymbolDisplayFormat`. Verify empirically during Task 1; the spec uses `<TResult>` based on observed Roslyn output but adjust if needed.

**Cecil generic-instantiation FullName.** `Task<int>.get_Result()` at IL: confirm that `MethodReference.DeclaringType` is a `GenericInstanceType` whose `ElementType.FullName` is `System.Threading.Tasks.Task`1`. If Cecil exposes this differently, adjust the strip-instantiation logic in `BlockingCallSink`.

**Async state-machine plumbing — false-positive surface.** The compiler-generated state machine for an async method calls into `AsyncTaskMethodBuilder`, `TaskAwaiter.GetResult()`, `ConfiguredTaskAwaiter.GetResult()`, etc. None of those are on the v1 sink list, so the plumbing should be silent. The "async sanity" test (Roslyn test 9 and IL test 5) is the canary. If it fires, the sink list is too aggressive; trim until it's silent.

**`SemaphoreSlim.Wait` overload disambiguation.** `SemaphoreSlim.Wait(int)` and `SemaphoreSlim.WaitAsync(int)` have different *method names* (`Wait` vs `WaitAsync`), so the `(type, method-name)` matcher correctly discriminates without needing per-overload logic. Confirmed: no overload of `Wait` is non-blocking; no overload of `WaitAsync` is blocking. Verify.

**Property getter at IL.** `Task<T>.Result` at IL is `callvirt instance !0 ...::get_Result()`. The `BlockingCallSink` matches `Call`/`Callvirt` and inspects `MethodReference`, which works uniformly for property getters and regular methods. No special-casing needed at the IL layer (unlike Roslyn, where `IPropertyReferenceOperation` is a distinct operation kind).
