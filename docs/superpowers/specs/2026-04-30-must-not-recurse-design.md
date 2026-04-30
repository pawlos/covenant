# `[MustNotRecurse]` — Cycle-Free Call Graph Property

**Status:** Draft (2026-04-30)
**Scope:** Ship a fourth propagating property whose "sink" is a *structural property of the walker itself* — a cycle in the DFS path — rather than a per-instruction or per-method-call match. Validates that the framework's core can express graph-level properties, not just instruction-level ones, and exposes one architectural change to the IL walker that future graph-property analyzers (e.g. `[MustHaveBoundedDepth]`, `[MustBeTreeShaped]`) can also use.

## Purpose

The first three shipped properties (`[MustNotAllocate]`, `[MustNotThrow]`, `[MustNotBlock]`) all match per-operation patterns: a single IL instruction or Roslyn operation either is or is not the sink. Cycles aren't expressible that way — there's no opcode for "this call closes a loop." The cycle is a property of the call graph as a whole, observable only when the walker is mid-traversal.

The current walker *already* detects cycles internally — that's how the `visited` set prevents infinite recursion. It just silently absorbs the back-edge and moves on. Surfacing it as a diagnostic is a one-line change in spirit, but it requires a small architectural extension: the walker needs to know whether to *report* the cycle (for `[MustNotRecurse]`) or *silently swallow* it (for everything else).

**Secondary purpose:** real value. Bounded-stack guarantees matter in finalizers, signal handlers, real-time threads, and trampoline-style state machines. "This method does not recurse, transitively, even through helpers" is a property worth proving.

## What's structurally different from prior properties

`[MustNotRecurse]` breaks two assumptions baked into the existing walker:

1. **Sinks are no longer tied to a single instruction.** A cycle is detected at a *call site* by examining the *chain*. The current `IIlSink.Match(Instruction)` interface can't express "this call would close the chain back to a method already in it." Instead, cycle detection lives in the walker itself, gated by a per-analyzer config flag.

2. **"Annotated callee terminates the walk" is wrong for graph properties.** When A `[MustNotRecurse]` calls B `[MustNotRecurse]` and B calls back to A, neither A nor B individually violates the property — each, considered in isolation, doesn't recurse. The cycle exists *between* them. The existing trust-and-stop optimization would mask this. So in cycle-detection mode, the walker must keep walking through annotated callees, relying only on the cycle check + visited set to terminate.

These are coupled: cycle-detection mode is on iff annotated-callee termination is off. We model it as a single constructor parameter, `cycleSinkLabel: string?`. Null = traditional mode. Non-null = cycle-detection mode.

## Inherited unchanged

- `CallgraphClosure.Attributes` — adds `MustNotRecurseAttribute` next to the existing three.
- `CallgraphClosure.Core` (Roslyn analyzer base + `Config` record) — no changes. The `[MustNotRecurse]` Roslyn analyzer ships with an empty `Sinks` array; only boundary diagnostics (CGC001/CGC002) fire from Roslyn. Direct self-recursion at edit time is a documented Roslyn-side gap (see "Non-goals").
- Diagnostic IDs and severity — same.
- Test harness patterns — same.

## What changes in `CallgraphClosure.ILCheck.Core`

`ClosureWalker` gains one optional constructor parameter and a small modification to its DFS loop. The change is strictly additive — every existing analyzer (`MustNotAllocate.ILCheck`, `MustNotThrow.ILCheck`, `MustNotBlock.ILCheck`) continues to work unchanged because the new parameter defaults to null.

```csharp
public ClosureWalker(
    string attributeFullName,
    ImmutableArray<IIlSink> sinks,
    string propertyName,
    string? amortizedAttributeFullName = null,
    AmortizedSet? amortizedSet = null,
    string? cycleSinkLabel = null)   // NEW — null = traditional mode
```

In `VisitMethodBody`, before recursing into a resolved callee, add a chain-membership check:

```csharp
if (resolved is not null && resolved.Body is not null)
{
    // Cycle check: is the target already in the active DFS path?
    var isCycle = false;
    foreach (var chainMethod in chain)
    {
        if (chainMethod.FullName == resolved.FullName) { isCycle = true; break; }
    }

    if (isCycle)
    {
        if (_cycleSinkLabel is not null)
        {
            diagnostics.Add(new Diagnostic(
                Id: DiagnosticIds.SinkHit,
                PropertyName: _propertyName,
                AnnotatedCaller: annotatedCaller,
                Chain: chain.Add(target),
                SinkLabel: _cycleSinkLabel,
                UnresolvedTarget: null));
        }
        // Don't recurse — would loop forever.
        continue;
    }

    // In cycle-detection mode, annotated/amortized callees do NOT terminate the walk.
    // Without that change, mutual recursion between two annotated methods would be invisible.
    if (_cycleSinkLabel is null)
    {
        if (HasAttributeByFullName(resolved, _attributeFullName)) continue;
        if (_amortizedAttributeFullName is not null &&
            HasAttributeByFullName(resolved, _amortizedAttributeFullName)) continue;
        if (_amortizedSet.Contains(resolved.FullName)) continue;
    }

    VisitMethodBody(resolved, annotatedCaller, chain.Add(target), visited, diagnostics);
    continue;
}
```

The chain check is O(chain.Length). In practice chains are short (call-graph depth from any annotated method); the overhead is negligible for traditional-mode users and intrinsic for cycle-mode users.

## Decisions locked in

1. **Attribute name:** `[MustNotRecurse]`. Matches the rhythm of the others.
2. **Single shared attribute location:** `CallgraphClosure.Attributes`, alongside the others.
3. **No Roslyn-side direct self-recursion detection in v1.** Direct self-recursion (`F() { F(); }`) is detectable in Roslyn (`IInvocationOperation` whose target equals the enclosing method), but doing so requires the `ISink` interface to take both `op` and `caller` — a breaking change to all six existing sinks. Deferred. The IL pass detects all recursion (direct and transitive); the trade-off is users see CGC003 at build time rather than edit time. Documented as a known limit.
4. **No amortized escape hatch.** Cycles are pass/fail; "amortized recursion" doesn't make semantic sense.
5. **Cycle detection mode disables annotated-callee/amortized termination.** As argued above, this is necessary for correctness on mutual recursion between annotated methods. The two modes are coupled into one flag (`cycleSinkLabel`).
6. **Each annotated method's analysis reports cycles independently.** If A `[MustNotRecurse]` and B `[MustNotRecurse]` form a cycle A→B→A, both A's analysis (chain=[A,B,A]) and B's analysis (chain=[B,A,B]) emit CGC003. Two diagnostics, one cycle. This matches existing-property behavior (a sink in shared code fires once per annotated caller that reaches it).
7. **The single label is `"recursion"`.** The CGC003 message reads: *"Method 'F' is annotated [MustNotRecurseAttribute] but contains a recursion"*. Slightly awkward grammar ("a recursion" reads like "a cycle"); accept it for consistency with the existing `"contains a {2}"` template.

## Architecture

### Package layout

```
src/
  MustNotRecurse/                        netstandard2.0 — Roslyn analyzer
    MustNotRecurseAnalyzer.cs            concrete subclass of CallgraphClosureAnalyzer
                                         with EMPTY Sinks; only fires CGC001/CGC002
                                         via the standard boundary flow

  MustNotRecurse.ILCheck/                net10.0 — IL walker binding
    MustNotRecurseIlAnalyzer.cs          static class: AttributeFullName + (empty) Sinks
                                         + the cycleSinkLabel = "recursion" wiring
```

No sink classes. The Roslyn project ships with empty Sinks. The IL project ships with empty Sinks plus the `cycleSinkLabel` configured. The cycle-detection logic lives entirely in the walker.

### Attribute

```csharp
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotRecurseAttribute : Attribute { }
```

### Roslyn analyzer

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotRecurseAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotRecurseAnalyzer() : base(new Config(
        AttributeFullName: "CallgraphClosure.Attributes.MustNotRecurseAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray<ISink>.Empty)) { }
}
```

Empty sinks. Roslyn produces only CGC001/CGC002 for this property.

### IL analyzer binding

```csharp
public static class MustNotRecurseIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotRecurseAttribute";
    public const string CycleSinkLabel = "recursion";

    public static ImmutableArray<IIlSink> Sinks { get; } = ImmutableArray<IIlSink>.Empty;
}
```

The test fixture and CLI then construct the walker with `cycleSinkLabel: MustNotRecurseIlAnalyzer.CycleSinkLabel`.

## Testing strategy

### Roslyn tests (`tests/MustNotRecurse.Tests/MustNotRecurseTests.cs`)

The Roslyn analyzer has no CGC003 sinks. Tests cover boundary diagnostics and the documented direct-self-recursion gap.

- **Empty body:** silent.
- **Calls unannotated source method:** fires CGC001.
- **Calls external method:** fires CGC002.
- **Calls annotated method:** silent (annotated callee trusted at edit time; IL pass walks transitively).
- **Direct self-recursion:** silent at Roslyn level (documented limit). The IL pass catches it.

Baseline target: 5 tests.

### IL tests (`tests/MustNotRecurse.ILCheck.Tests/MustNotRecurseIlTests.cs`)

- **Direct self-recursion:** `[MustNotRecurse] F → F` → CGC003 with label `"recursion"`, chain=[F, F].
- **Mutual recursion (one annotated):** `[MustNotRecurse] A → B → A` (B unannotated). CGC003 attributed to A, chain=[A, B, A].
- **Three-method cycle:** `[MustNotRecurse] A → B → C → A`. CGC003 attributed to A, chain=[A, B, C, A].
- **No cycle:** linear call chain `[MustNotRecurse] A → B → C` (no back-edge). Silent.
- **Mutual recursion between two annotated methods:** A `[MustNotRecurse]` and B `[MustNotRecurse]`, A→B→A. Both A's and B's analyses detect the cycle. Two CGC003 diagnostics. Validates that cycle-detection mode disables annotated-callee termination.
- **Cycle survives visited prune:** `A → B → D → A` and `A → C → D` (shared D node, only one back-edge to A). Walker detects exactly one cycle via the B path; the C path's re-entry into D is short-circuited by the visited set. Validates that visited-prune doesn't mask cycles, only deduplicates path-equivalent reports.

Baseline target: 6 tests.

### Cross-property regression tests

The walker change is strictly additive but touches code path used by every property. Run the full existing suite (69 tests) to confirm nothing regresses.

### Suite target

After M1 of this feature: 69 + 5 + 6 = **80 tests**.

### Sample (`src/MustNotRecurse.Sample/`)

```csharp
using CallgraphClosure.Attributes;

var f = new Demo();
f.Compute(5);

internal sealed class Demo
{
    [MustNotRecurse]
    public int Compute(int n) => n <= 1 ? 1 : n * Helper(n);

    // Helper unannotated → CGC001 at edit time, prompting annotation or removal.
    // Helper recurses back into Compute → IL pass detects the transitive cycle.
    private int Helper(int n) => Compute(n - 1);
}
```

Two diagnostics fire:
- Edit time (Roslyn): CGC001 — calls unannotated `Helper`. The fix prompt is "annotate Helper or remove the call."
- Build time (IL pass): CGC003 — recursion, chain=[Compute, Helper, Compute].

Demonstrates both that the framework still produces useful boundary feedback at edit time AND that the IL pass catches the transitive cycle that Roslyn-side analysis cannot.

## Non-goals (this milestone)

| Case | Behavior | Deferred to |
|---|---|---|
| Roslyn-side direct self-recursion (CGC003 at edit time) | Not detected. IL pass handles it. | v2 — requires extending `ISink` to take `caller`, which is a breaking change to all existing sinks |
| Cycles via virtual dispatch (`callvirt` on interface) | Walker only resolves to the declared method; subtype implementations are invisible | M3 (virtual dispatch milestone) handles this for all properties |
| "Bounded depth" property (e.g., `[MaxStackDepth(N)]`) | Different graph-property; uses chain length but with a numeric threshold rather than membership | Future graph-property milestone |
| Distinguishing direct self-recursion from indirect cycles in the diagnostic message | All are labeled `"recursion"`; chain length tells the user which kind | Cosmetic; could split labels later |
| Reporting all distinct cycle paths through the same SCC | Only one path per cycle reported (via visited prune) | Probably fine forever — extra diagnostics would be noise |

## Success criteria

M1 of this feature is done when:

1. `MustNotRecurseAttribute` exists in `CallgraphClosure.Attributes`.
2. `ClosureWalker` accepts `cycleSinkLabel` parameter; existing analyzers pass null and behave identically (regression-tested via the 69-test existing suite).
3. `src/MustNotRecurse/` and `src/MustNotRecurse.ILCheck/` projects build clean.
4. All 11 new tests pass (5 Roslyn + 6 IL).
5. Full suite: 80 tests passing, zero failures.
6. `MustNotRecurse.Sample` builds with the expected 2 diagnostics (CGC001 + CGC003 cycle).
7. Tag `must-not-recurse-complete` applied.

## Open uncertainties (flagged, not blocking)

**`MethodReference.FullName` equivalence across instantiations.** The chain-membership check uses `chainMethod.FullName == resolved.FullName`. For non-generic methods this is straightforward. For generic methods or methods on generic types, Cecil's `FullName` may include type arguments. In practice, recursion through generic specializations should be rare; if it surfaces as a problem, switch to a normalized comparison (declaring-type element + method name + parameter element types). Verify with a generic-recursive test if the simple comparison fails.

**Visited-prune masking distinct cycles in the same SCC.** As traced in the spec, multiple paths through the same SCC produce one diagnostic, not many. This is intentional but worth confirming with the dedicated test (#6 above).

**Annotated-callee mode-switching read.** The walker check `if (_cycleSinkLabel is null)` is correct but a little subtle on read. Consider extracting to a named local/method (`bool TerminatesAtAnnotatedCallees => _cycleSinkLabel is null`) for clarity if the implementation feels gnarly.

**Async state machines.** An annotated `async Task F() { ... }` method's compiler-generated state machine has a structure where the `MoveNext` method (in a different class) self-references. This is technically a cycle in the call graph but isn't user-recursion. Verify whether the walker fires false positives on plain async methods. If it does, the fix is similar to `[MustNotBlock]`'s sanity test — possibly excluding `<>d__N` state machine method names or the `MoveNext` family from cycle detection. Add a dedicated test if this materializes.
