# Callgraph-Closure Lint for .NET — Milestone 2.5 Design

**Status:** Approved (2026-04-18)
**Scope:** Add `[AmortizedAllocation]` as a second propagating concept (a walk-terminator), plumb it through both Roslyn and IL passes, build an external-annotations file format for BCL methods, and ship a two-phase HTTP request-line parser showcase with BenchmarkDotNet numbers.

## Purpose

M1 and M2 demonstrate the two-pass architecture. M2.5 makes it **usable for real hot-loop .NET code** by acknowledging the one pattern that otherwise floods the output with false positives: pooled / amortized allocations.

The showcase exists to prove this claim with concrete numbers. "Here's naive code annotated `[MustNotAllocate]` → loud analyzer output + bad benchmark numbers. Here's the same code refactored to use `ReadOnlySpan<byte>` + `ArrayPool<T>` → silent analyzer + order-of-magnitude benchmark win." That before/after is the writeup's headline evidence.

## Inherited from M1 / M2

- Same three diagnostic IDs (CGC001 / CGC002 / CGC003).
- Same attribute-FQN-by-string lookup.
- Same property-agnostic core + property-specific sinks split.
- Same xUnit test harness patterns.
- M2's `ClosureWalker` transitive algorithm is preserved; M2.5 adds one new termination condition to it.

## Locked-in decisions (from brainstorm)

1. **Showcase = HTTP request-line parser with a reader wrapper.** Two-axis optimization: Span-vs-string parsing AND per-call-alloc-vs-pooled-buffer reading.
2. **Two phases:** `Showcase.Http.Naive` and `Showcase.Http.Optimized`, symmetric API surface, different implementations.
3. **Benchmarks in a separate `bench/Showcase.Http.Benchmarks` project** using `BenchmarkDotNet` with `[MemoryDiagnoser]`.
4. **`[AmortizedAllocation]` attribute applied to methods** that return pool-backed resources. Walker treats marked methods the same way it treats `[MustNotAllocate]` callees: **terminate the walk at this edge, don't recurse into the body.**
5. **External annotations file** in JSON, loaded via `<AdditionalFiles>` for Roslyn and `--amortized-file` CLI arg for IL. Required because BCL methods (`ArrayPool<T>.Rent`, `MemoryPool<T>.Rent`, `Channel<T>.Writer.TryWrite`) can't be modified to carry the attribute directly.

## `[AmortizedAllocation]` semantics

**The rule:** when the walker encounters a call whose resolved target carries `[AmortizedAllocation]` (directly in metadata or via the external annotations file), it **skips that call** — same branch as for `[MustNotAllocate]`-annotated callees, same `continue` at the call-site in `ClosureWalker.VisitMethodBody`.

**Why the same branch:** semantically the two attributes mean different things (`[MustNotAllocate]` = "I promise not to"; `[AmortizedAllocation]` = "My allocations don't count against callers"), but their effect on the walk is identical: stop here, trust the boundary. One code path, two reasons.

**What this prevents:**
- False-positive CGC003s from transitive walks into `ArrayPool<T>.Rent`'s internal buffer-bank allocation logic.
- Noise in the M2 output when any hot path legitimately uses pooling.

**What this does NOT prevent:**
- Directly allocating a byte array (`new byte[4096]`) still fires CGC003. The attribute is on the POOL method, not on the call site.
- Calls to unannotated methods that happen to use pools internally but aren't the pool's public surface. These still walk through and find whatever sinks exist.

## Attribute definition

```csharp
namespace CallgraphClosure.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AmortizedAllocationAttribute : Attribute { }
```

Lives in a new small library `src/CallgraphClosure.Attributes/` (netstandard2.0). This is a clean-up moment — M1's `MustNotAllocateAttribute` will **also** move here so all callgraph-closure attributes share a home. Migration: move the type, update the namespace consumers reference, and update the FQN string in `MustNotAllocateAnalyzer` / `MustNotAllocateIlAnalyzer`. One breaking rename, all caught by the compiler.

## External annotations file

### Format

JSON, strict schema:

```json
{
  "amortized_methods": [
    "System.Buffers.ArrayPool`1.Rent(Int32)",
    "System.Buffers.ArrayPool`1.Rent(Int32, Boolean)",
    "System.Buffers.MemoryPool`1.Rent(Int32)"
  ]
}
```

Method names use the Cecil/Roslyn-compatible FQN format: `Namespace.Type.Method(ParamType1, ParamType2)`. Generic type parameters appear as backtick-count (`ArrayPool`1`). Overloads are distinguishable because parameter types are in the key.

### Loading

- **Roslyn:** analyzer consumer adds `<AdditionalFiles Include="amortized-methods.json" />` to their csproj. Analyzer reads via `CompilationStartAnalysisContext.Options.AdditionalFiles`, parses once per compilation start, builds an `ImmutableHashSet<string>` of amortized method FQNs, and injects it into the analyzer config.
- **IL CLI:** new `--amortized-file path/to/file.json` argument. Parsed once at startup, same hashset structure.
- **Both:** before recursing into a resolved callee, check the hashset. If present → treat identically to an `[AmortizedAllocation]`-marked method.

### Default file

`src/MustNotAllocate/bcl-amortized.json` ships with the attribute package. Contains the common BCL amortization methods. Users reference it via `<AdditionalFiles Include="$(MSBuildThisFileDirectory)...bcl-amortized.json" />` pattern, or copy-paste into their own file.

Initial contents: `ArrayPool<T>.Rent` (both overloads), `MemoryPool<T>.Rent`, nothing else. Grows as patterns are encountered.

## Showcase architecture

### Layout

```
src/
  Showcase.Http.Common/            net10.0 library, shared ParsedRequest ref struct, input/output contracts
  Showcase.Http.Naive/             net10.0 library, strings-and-classes implementation
  Showcase.Http.Optimized/         net10.0 library, Span-and-pool implementation
bench/
  Showcase.Http.Benchmarks/        net10.0 exe, BenchmarkDotNet
```

Both Naive and Optimized reference the analyzer package (same way the M1 sample does) and apply `[MustNotAllocate]` to their parse methods. The analyzer output on each is part of the showcase — committed as a `.expected.txt` file so reviewers can diff the before/after without running the tool.

### Symmetric API surface

Both expose:

```csharp
public static class RequestLineParser
{
    [MustNotAllocate]
    public static ParsedRequest Parse(/* naive: string, optimized: ReadOnlySpan<byte> */);
}

public sealed class RequestReader
{
    [MustNotAllocate]
    public ParsedRequest ReadNext(Stream input);  // manages the buffer
}
```

Signatures differ (string vs ReadOnlySpan<byte>, `ParsedRequest` class vs `readonly ref struct`) but the surface is isomorphic. Benchmarks call `ReadNext` on each.

### Parser behavior (both)

- Input: HTTP/1.1 request line, e.g. `"GET /users?id=42&sort=asc HTTP/1.1\r\n"`
- Output: parsed `(method, path, query, version)` with query separated from path.
- Errors: malformed input throws `FormatException`. No structured result type — keeps the narrative focused on allocations, not error-handling ergonomics.

### Reader behavior (both)

- Input: `Stream` providing request bytes.
- Reads one request line into a 4096-byte buffer.
- **Naive:** `new byte[4096]` per call → CGC003.
- **Optimized:** `ArrayPool<byte>.Shared.Rent(4096)` with try/finally Return → 0 allocations once bcl-amortized.json is wired, analyzer silent.

### Expected analyzer outputs

Naive (committed as `Showcase.Http.Naive.expected.txt`):
- Multiple CGC003s in `RequestLineParser.Parse`: `string.Split`, substring creation, `new ParsedRequest`, possibly boxing
- CGC003 in `RequestReader.ReadNext`: `new byte[4096]`

Optimized (committed as `Showcase.Http.Optimized.expected.txt`):
- Empty. Zero diagnostics. (If any appear, they're real violations worth fixing.)

### Expected benchmark numbers (rough guidance for verification)

- Naive parse: ~300-500 ns, ~150-250 B allocated per call
- Optimized parse: ~20-40 ns, 0 B allocated per call
- Naive reader: ~400-700 ns, ~4150 B per call (parse + 4096-byte buffer)
- Optimized reader: ~30-50 ns, 0 B per call

Order-of-magnitude differences. If numbers are much tighter than expected, the naive version probably isn't naive enough — check for hidden optimizations the JIT added.

## Milestone sub-structure

For execution, four ordered sub-milestones:

1. **M2.5a — attribute plumbing.** Create `CallgraphClosure.Attributes` library; move `MustNotAllocateAttribute` there; add `AmortizedAllocationAttribute`. Update analyzer projects and their FQN strings. Tests: fixture with a method marked `[AmortizedAllocation]`, caller annotated `[MustNotAllocate]`, no diagnostic fires.

2. **M2.5b — external annotations file support.** JSON schema + parser, Roslyn integration via `AdditionalFiles`, IL CLI flag. Ship `bcl-amortized.json` with `ArrayPool<T>.Rent` entries. Tests: fixture with `Caller() { ArrayPool<byte>.Shared.Rent(4096); }`, analyzer silent only when the annotations file is configured.

3. **M2.5c — showcase projects.** Three new projects (Common / Naive / Optimized). Parser + reader in each. Commit expected-output text files for diffing.

4. **M2.5d — benchmark project.** BenchmarkDotNet + 4 benchmarks (NaiveParse / OptimizedParse / NaiveRead / OptimizedRead). Commit one run's baseline output so the writeup can reference specific numbers.

Each sub-milestone is independently testable and buildable. If we stop after any of them, the repo is in a coherent state.

## Testing strategy additions

**Unit tests (add to existing test projects):**
- `[AmortizedAllocation]` on source method → walker terminates at that edge (M1 Roslyn + M2 IL, parallel test cases)
- Method listed in external annotations file → walker terminates at that edge (both passes)
- Method NOT in annotations, NOT marked → walker walks through as before (regression test for M2 behavior)
- Missing annotations file → analyzer silent no-op (treat as empty amortized set)
- Malformed annotations file → single diagnostic CGC099 (Info severity) "Amortized annotations file failed to parse"; analyzer continues with empty set. Don't crash, don't block the build

**Integration tests (new):**
- Showcase.Http.Naive compiled with analyzer → produces the exact expected `.expected.txt` content (string-match the diagnostic list)
- Showcase.Http.Optimized compiled with analyzer → produces empty diagnostic output

Benchmarks are not run in CI (too slow, environment-sensitive). Manually invoked; baseline output committed.

## Non-goals (M2.5)

| Case | Behavior | Deferred |
|---|---|---|
| Complex pool patterns (pool-per-thread, bounded pools) | Annotate via the file like any other | User's responsibility |
| Stream reading beyond request line (headers, body) | Out of scope; showcase is request-line only | M3 if we extend the demo |
| Error handling in the parser (malformed input) | Simple throw; no structured result type | Not central to the narrative |
| .editorconfig-integrated annotations | File-based only | Future if the JSON format proves awkward |
| Annotation via `[assembly: ...]` or external XML like ReSharper | Not supported | Explicit decision; keep format minimal |

## Success criteria

M2.5 is done when:

1. All existing tests (31 from M1+M2) still pass.
2. New unit tests for `[AmortizedAllocation]` pass in both Roslyn and IL passes.
3. Running the analyzer against `Showcase.Http.Naive` produces the committed `.expected.txt` output.
4. Running the analyzer against `Showcase.Http.Optimized` produces zero diagnostics (empty `.expected.txt`).
5. BenchmarkDotNet results committed, showing an order-of-magnitude gap between Naive and Optimized on both parse and read benchmarks.
6. All four sub-milestones independently verifiable as green.

## Open uncertainties (flagged, not blocking)

**ArrayPool generic-type FQN match.** BCL's `ArrayPool<T>.Rent` is generic; the FQN is `System.Buffers.ArrayPool`1.Rent(Int32)`. The walker resolves call targets to `MethodReference` / `IMethodSymbol` with their constructed form; for name matching we strip to `OriginalDefinition` / `GenericInstanceMethod.ElementMethod.FullName`. This matches how M1 already handles generic callees (see existing `OriginalDefinition` unwrap tests). Should Just Work, but worth an explicit test in M2.5a.

**Return + try/finally pattern.** The optimized reader uses `try { ... } finally { Pool.Return(buffer); }`. The `Pool.Return` call is unannotated (and shouldn't need `[AmortizedAllocation]` — returning to a pool doesn't allocate). If the walker goes deep into `Return` and finds sinks inside (e.g., contention-path fallbacks), we get noise on the optimized variant. Mitigation: add `Pool.Return` to `bcl-amortized.json` if observed.

**M2's unbounded walk meets M2.5's attribute.** Even with `[AmortizedAllocation]` terminating walks at pool methods, the M2 CLI's raw output on the full sample may still be noisy because most BCL calls still transitively reach sinks. The attribute addresses the *showcase's* noise cleanly; the tool's general hot-path output volume remains a separate concern (tracked in `known_issues.md`, unresolved).

## Post-M2.5 roadmap (not binding)

1. **M3:** virtual/interface dispatch (CHA) + expression trees + async state machines.
2. **Writeup draft:** the concrete story is now ready — M1 analyzer, M2 IL pass, M2.5 amortized escape hatch, showcase with numbers. This is the "ship something" moment.
3. **NativeAOT integration** (was M4) still open.
4. **Differential fuzzing** still open.
