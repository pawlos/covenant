# `[MustNotThrow]` Showcase — Exception-Free Validation Design

**Status:** Approved (2026-05-28)
**Scope:** Ship a second before/after showcase — parallel to the M2.5 HTTP request-line parser — that demonstrates `[MustNotThrow]` (composed with `[MustNotAllocate]`) on an input-validation hot path. Naive variant uses exceptions as internal control flow; optimized variant is a pure return channel. BenchmarkDotNet numbers show the throw + unwind + exception-allocation cost on the failure path.

## Purpose

The HTTP showcase proves the infrastructure on **one** predicate (`[MustNotAllocate]`). This showcase proves the infrastructure is genuinely **property-agnostic** by re-running the identical Naive-vs-Optimized format against a *different* predicate (`[MustNotThrow]`) and a *different* domain (validation, not parsing).

The narrative hook for the writeup sequel: the naive validator's public signature returns a result and **looks** exception-free, but internally it throws-and-catches as control flow. `[MustNotThrow]` sees through the `try`/`catch` — the throw sink has no catch exemption — and flags the internal throw anyway. The point: **callgraph closure enforces the internal contract, not just the surface signature.**

## Inherited from the HTTP showcase (M2.5)

- Same three-project shape: `*.Common` (shared types), `*.Naive`, `*.Optimized`, plus a `bench/*.Benchmarks` project.
- Same analyzer wiring in each consumer csproj: `ProjectReference` to `Attributes` + `Core` (analyzer) + `MustNotAllocate` (analyzer) + `MustNotThrow` (analyzer), per `Showcase.Http.Optimized.csproj`.
- Same verification artifacts: committed `.expected.txt` analyzer-output snapshots per variant + a BDN `baseline-results.md`. **No unit-test project** — the HTTP showcase has none, and we match it.
- Same diagnostic IDs (CGC001 / CGC003), same attribute-FQN-by-string lookup, same property-agnostic core.

## Domain

A **quantity-field validator** — the per-row check a CSV importer or form handler runs. Input is a raw quantity string (e.g. `"42"`). Valid means: non-empty, parses as an integer, and in range `[1, 10000]`.

Outcomes (`enum QuantityError`): `None`, `NotANumber`, `OutOfRange`. Empty input is classified `NotANumber` by both variants (an empty string is not a number) — this keeps the two implementations in agreement on every input, which `int.Parse`'s `FormatException` happens to give us for free on the naive side.

## Analyzer-behavior facts this design relies on

Verified against the current analyzer + the empty `Showcase.Http.Optimized.expected.txt`:

1. The Roslyn analyzer flags **sink operations directly in the annotated method's own body** (a `throw` for `[MustNotThrow]`; a `newobj`/`newarr`/box for `[MustNotAllocate]`).
2. It flags **CGC001** when an annotated method calls an unannotated method **whose source is in the same compilation** (you could annotate it). It does **not** flag calls into opaque metadata/BCL (`int.Parse`, span members) — that is why the HTTP optimized snapshot is empty despite calling `span.IndexOf`/`Slice`.
3. The throw sink (`ThrowOperationSink`) matches any `IThrowOperation` with **no exemption for being inside a `try`/`catch`**.
4. Constructing a `struct` (`new QuantityResult(...)`) is not a heap allocation and does not fire `[MustNotAllocate]`; constructing an exception object (`new FormatException()`, a class) does.

Consequence for the design: to make the analyzer surface a *throw* diagnostic (not merely "calls unannotated method"), the throws live in the annotated method's **own body**, exactly as the HTTP naive parser's `new` operations do.

## Project layout

```
src/Showcase.Validation.Common         QuantityError enum + QuantityResult readonly struct
src/Showcase.Validation.Naive          Validate(string) — throws-and-catches internally; loud under CGC
src/Showcase.Validation.Optimized      TryValidate(ReadOnlySpan<char>, out int, out QuantityError) — pure return channel; silent
bench/Showcase.Validation.Benchmarks   [MemoryDiagnoser], [Params] valid vs invalid, Naive baseline vs Optimized
```

All three `src` projects target `net10.0`, set `TreatWarningsAsErrors=false`, and are added to `CallgraphClosure.slnx`. The bench project references Naive + Optimized and `BenchmarkDotNet` 0.13.12 (matching the HTTP bench).

## Shared types (`Showcase.Validation.Common`)

```csharp
public enum QuantityError { None, NotANumber, OutOfRange }

public readonly struct QuantityResult
{
    public bool IsValid { get; }
    public int Value { get; }
    public QuantityError Error { get; }
    public QuantityResult(bool isValid, int value, QuantityError error) { ... }
}
```

A `struct` so that both variants can return it without a heap allocation. `Value` is meaningful only when `IsValid`.

## Naive variant (`Showcase.Validation.Naive`)

```csharp
[MustNotAllocate]
[MustNotThrow]
public static QuantityResult Validate(string input)
{
    try
    {
        var value = int.Parse(input);                 // BCL: opaque to analyzer; throws FormatException at runtime
        if (value < 1 || value > 10000)
            throw new ArgumentOutOfRangeException(nameof(input));  // explicit throw IN body
        return new QuantityResult(true, value, QuantityError.None);
    }
    catch (FormatException)
    {
        return new QuantityResult(false, 0, QuantityError.NotANumber);
    }
    catch (ArgumentOutOfRangeException)
    {
        return new QuantityResult(false, 0, QuantityError.OutOfRange);
    }
}
```

Exceptions as control flow: out-of-range input is signalled by a `throw` that the method's own `catch` converts to a result. **Analyzer output** (captured into `Showcase.Validation.Naive.expected.txt`): a CGC003 *throw* and a CGC003 *allocation* for the explicit `throw new ArgumentOutOfRangeException(...)`, both attributed to `Validate`. The exact line/column set is captured empirically from a clean build, not asserted here.

(`int.Parse`'s own `FormatException` is invisible to the analyzer — BCL is opaque — but it is real at runtime and exercised by the `"abc"`-style case. We do not lean on the IL post-pass here: per `CallgraphClosure.ILCheck.Cli/NUGET-README.md` the packaged CLI is `[MustNotAllocate]`-only today.)

## Optimized variant (`Showcase.Validation.Optimized`)

```csharp
[MustNotAllocate]
[MustNotThrow]
public static bool TryValidate(ReadOnlySpan<char> input, out int value, out QuantityError error)
{
    value = 0;
    if (input.IsEmpty) { error = QuantityError.NotANumber; return false; }

    int acc = 0;
    foreach (var c in input)
    {
        if (c < '0' || c > '9') { error = QuantityError.NotANumber; return false; }
        acc = acc * 10 + (c - '0');
        if (acc > 10000) { error = QuantityError.OutOfRange; return false; }   // also guards overflow
    }
    if (acc < 1) { error = QuantityError.OutOfRange; return false; }

    value = acc;
    error = QuantityError.None;
    return true;
}
```

Manual digit scan over the span, range checked inline. Never throws, never allocates → **empty** `Showcase.Validation.Optimized.expected.txt`. Only BCL touchpoints are the span enumerator / `IsEmpty`, which are opaque metadata and do not fire CGC001 (same as the HTTP optimized parser). No `amortized-methods.json` is expected to be needed; if a clean build surprises us with a diagnostic, add one mirroring the HTTP optimized project.

## Benchmark (`bench/Showcase.Validation.Benchmarks`)

```csharp
[MemoryDiagnoser]
public class ValidateBenchmarks
{
    [Params("42", "999999")] public string Input;   // valid vs out-of-range

    [Benchmark(Baseline = true)]
    public QuantityError Naive() => Naive.QuantityValidator.Validate(Input).Error;

    [Benchmark]
    public QuantityError Optimized()
    {
        Optimized.QuantityValidator.TryValidate(Input.AsSpan(), out _, out var error);
        return error;
    }
}
```

`[Params]` over a valid input (`"42"`) and an out-of-range input (`"999999"`). The out-of-range case is chosen so the benchmarked throw is exactly the `throw new ArgumentOutOfRangeException` the analyzer flagged — tight linkage between "what CGC caught" and "what it costs."

Expected shape: comparable on the valid input; on the invalid input, Naive pays throw + stack-unwind time and allocates the exception object, Optimized is flat and **0 B**. Numbers written to `bench/Showcase.Validation.Benchmarks/baseline-results.md`, parallel to the HTTP bench's results doc.

## Verification artifacts

1. `src/Showcase.Validation.Naive/Showcase.Validation.Naive.expected.txt` — non-empty, regenerated from a clean build (`dotnet build … | grep CGC | sort`).
2. `src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.expected.txt` — empty (zero diagnostics for both properties).
3. `bench/Showcase.Validation.Benchmarks/baseline-results.md` — committed BDN run.
4. `docs/ROADMAP.md` — item #2 marked done, state table updated, per the project's roadmap-update convention.

## Success criteria

1. `Showcase.Validation.Optimized` builds with **zero** CGC diagnostics.
2. `Showcase.Validation.Naive` builds with the expected CGC003 throw + allocation diagnostic(s), matching its committed `.expected.txt`.
3. Both variants agree on the verdict for valid (`"42"` → `None`) and invalid (`"999999"` → `OutOfRange`) inputs.
4. BDN run shows the failure-path gap: Naive allocates and is materially slower on `"999999"`; Optimized is 0 B.
5. All projects build as part of the solution; the bench runs.

## Out of scope

- No unit-test project (matches the HTTP showcase).
- No IL-post-pass coverage for `[MustNotThrow]` (the packaged CLI is allocate-only today; tracked separately in the roadmap).
- No changes to the analyzers, attributes, or core walker — this is purely a consumer-side showcase.
- Writing the actual writeup sequel section — this design delivers the artifacts that sequel will cite, not the prose.
