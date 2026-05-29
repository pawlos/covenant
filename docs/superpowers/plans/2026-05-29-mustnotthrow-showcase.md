# `[MustNotThrow]` Validation Showcase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a `Showcase.Validation.{Common,Naive,Optimized}` triad plus a `bench/Showcase.Validation.Benchmarks` project that demonstrates `[MustNotThrow]` (composed with `[MustNotAllocate]`) on a quantity-validation hot path — naive throws-and-catches internally, optimized is a pure return channel — with committed analyzer snapshots and BenchmarkDotNet numbers.

**Architecture:** Pure consumer-side showcase. No analyzer/attribute/core changes. Mirrors the existing M2.5 HTTP showcase exactly: three `net10.0` libraries under `src/`, a BDN exe under `bench/`, analyzer wiring via `ProjectReference … OutputItemType="Analyzer"`, and verification by committed `.expected.txt` analyzer-output snapshots + a `baseline-results.md`. No unit-test project (the HTTP showcase has none, and we match it).

**Tech Stack:** C# / .NET 10, Roslyn analyzers (`CallgraphClosure.MustNotAllocate` + `CallgraphClosure.MustNotThrow`), BenchmarkDotNet 0.13.12, `.slnx` solution file.

**Spec:** `docs/superpowers/specs/2026-05-28-mustnotthrow-showcase-design.md`

---

## Notes for the implementer (read once before Task 1)

- **This is not classic TDD.** The "test" for each showcase project is its analyzer output at build time: the naive project must emit specific CGC warnings (captured into a committed `.expected.txt`), the optimized project must emit **zero**. There is no xUnit project. This deliberately matches the HTTP showcase.
- **`Directory.Build.props` sets `TreatWarningsAsErrors=true` globally** and `Nullable=enable`, `LangVersion=latest`. Every showcase `csproj` overrides `TreatWarningsAsErrors=false` so the intentional CGC warnings (and, in the naive project, the genuine warnings) don't fail the build. This override is already in every `Showcase.Http.*` csproj — copy it.
- **Diagnostic IDs:** `[MustNotThrow]` and `[MustNotAllocate]` share IDs CGC001 (calls an unannotated method whose source is in this compilation) and CGC003 (a sink operation — a `throw`, or a heap allocation — directly in the walked body). The Roslyn analyzer does **not** flag calls into opaque BCL (`int.Parse`, span members), which is why the optimized project comes out clean.
- **Capturing warnings reliably:** builds are incremental and only re-emit warnings when a project actually recompiles. Always capture with `--no-incremental` so the warnings are present in the output.
- **The `.slnx` `<Project>` entries are kept in alphabetical order within each `<Folder>`.** `Showcase.Validation.*` sorts after `Showcase.Http.*`.

---

## File structure

| File | Responsibility |
|---|---|
| `src/Showcase.Validation.Common/Showcase.Validation.Common.csproj` | net10.0 library, no analyzers |
| `src/Showcase.Validation.Common/QuantityValidation.cs` | `QuantityError` enum + `QuantityResult` readonly struct (shared, alloc-free) |
| `src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj` | net10.0 library + analyzer wiring |
| `src/Showcase.Validation.Naive/QuantityValidator.cs` | `Validate(string)` — throws-and-catches internally |
| `src/Showcase.Validation.Naive/Showcase.Validation.Naive.expected.txt` | committed analyzer snapshot (non-empty) |
| `src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.csproj` | net10.0 library + analyzer wiring |
| `src/Showcase.Validation.Optimized/QuantityValidator.cs` | `TryValidate(ReadOnlySpan<char>, out, out)` — pure return channel |
| `src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.expected.txt` | committed analyzer snapshot (empty) |
| `bench/Showcase.Validation.Benchmarks/Showcase.Validation.Benchmarks.csproj` | BDN exe referencing Naive + Optimized |
| `bench/Showcase.Validation.Benchmarks/Program.cs` | BDN switcher entrypoint |
| `bench/Showcase.Validation.Benchmarks/Benchmarks.cs` | `[MemoryDiagnoser]` parameterized bench |
| `bench/Showcase.Validation.Benchmarks/baseline-results.md` | committed BDN run |
| `CallgraphClosure.slnx` | add the 4 new projects |
| `docs/ROADMAP.md` | mark item #2 done, refresh state table |

---

## Task 1: Common project — shared result types

**Files:**
- Create: `src/Showcase.Validation.Common/Showcase.Validation.Common.csproj`
- Create: `src/Showcase.Validation.Common/QuantityValidation.cs`
- Modify: `CallgraphClosure.slnx`

- [ ] **Step 1: Create the csproj**

`src/Showcase.Validation.Common/Showcase.Validation.Common.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the shared types**

`src/Showcase.Validation.Common/QuantityValidation.cs`:

```csharp
namespace Showcase.Validation.Common;

// Classification of a quantity-field validation. Empty input is reported as
// NotANumber by both variants, so the naive and optimized paths agree on
// every input (int.Parse throws FormatException on "" on the naive side).
public enum QuantityError
{
    None,
    NotANumber,
    OutOfRange,
}

// A struct so both variants can return it without a heap allocation.
// Value is meaningful only when IsValid.
public readonly struct QuantityResult
{
    public bool IsValid { get; }
    public int Value { get; }
    public QuantityError Error { get; }

    public QuantityResult(bool isValid, int value, QuantityError error)
    {
        IsValid = isValid;
        Value = value;
        Error = error;
    }
}
```

- [ ] **Step 3: Add the project to the solution**

In `CallgraphClosure.slnx`, inside `<Folder Name="/src/">`, immediately after the `Showcase.Http.Optimized` line, add:

```xml
    <Project Path="src/Showcase.Validation.Common/Showcase.Validation.Common.csproj" />
```

- [ ] **Step 4: Build the project**

Run: `dotnet build src/Showcase.Validation.Common/Showcase.Validation.Common.csproj`
Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/Showcase.Validation.Common/ CallgraphClosure.slnx
git commit -m "feat(validation-showcase): add Common result types"
```

---

## Task 2: Naive project — throws-and-catches internally

**Files:**
- Create: `src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj`
- Create: `src/Showcase.Validation.Naive/QuantityValidator.cs`
- Create: `src/Showcase.Validation.Naive/Showcase.Validation.Naive.expected.txt`
- Modify: `CallgraphClosure.slnx`

- [ ] **Step 1: Create the csproj (with analyzer wiring)**

`src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Showcase.Validation.Common\Showcase.Validation.Common.csproj" />
    <ProjectReference Include="..\CallgraphClosure.Attributes\CallgraphClosure.Attributes.csproj" />
    <ProjectReference Include="..\CallgraphClosure.Core\CallgraphClosure.Core.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\MustNotAllocate\MustNotAllocate.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\MustNotThrow\MustNotThrow.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the validator**

`src/Showcase.Validation.Naive/QuantityValidator.cs`:

```csharp
using System;
using CallgraphClosure.Attributes;
using Showcase.Validation.Common;

namespace Showcase.Validation.Naive;

public static class QuantityValidator
{
    // Exceptions as internal control flow. The public signature returns a
    // result and LOOKS exception-free — but the body throws and catches.
    // [MustNotThrow] sees the throw through the try/catch (the throw sink has
    // no catch exemption); [MustNotAllocate] sees the exception object.
    [MustNotAllocate]
    [MustNotThrow]
    public static QuantityResult Validate(string input)
    {
        try
        {
            var value = int.Parse(input);          // BCL: opaque to the analyzer; throws at runtime
            if (value < 1 || value > 10000)
                throw new ArgumentOutOfRangeException(nameof(input));
            return new QuantityResult(true, value, QuantityError.None);
        }
        catch (FormatException)
        {
            return new QuantityResult(false, 0, QuantityError.NotANumber);
        }
        catch (OverflowException)
        {
            return new QuantityResult(false, 0, QuantityError.OutOfRange);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new QuantityResult(false, 0, QuantityError.OutOfRange);
        }
    }
}
```

(The `OverflowException` catch is there so very long numeric strings don't escape as an unhandled exception — `int.Parse` throws `OverflowException`, not `FormatException`, for those. It is not exercised by the benchmark inputs but keeps the naive path honest.)

- [ ] **Step 3: Add the project to the solution**

In `CallgraphClosure.slnx`, inside `<Folder Name="/src/">`, immediately after the `Showcase.Validation.Common` line you added in Task 1, add:

```xml
    <Project Path="src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj" />
```

- [ ] **Step 4: Build and observe the CGC diagnostics**

Run:
```bash
dotnet build src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj --no-incremental 2>&1 | grep CGC
```
Expected: at least two warning lines for the `throw new ArgumentOutOfRangeException(nameof(input));` line (around line 21) — one `CGC003 … throw` (from `[MustNotThrow]`) and one `CGC003 … object allocation` (from `[MustNotAllocate]`), both naming method `Validate`. Example shape (exact line/col will vary):

```
src/Showcase.Validation.Naive/QuantityValidator.cs(21,17): warning CGC003: Method 'Validate' is annotated [MustNotThrowAttribute] but contains a throw [src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj]
src/Showcase.Validation.Naive/QuantityValidator.cs(21,23): warning CGC003: Method 'Validate' is annotated [MustNotAllocateAttribute] but contains a object allocation [src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj]
```

If you see **zero** CGC lines, the analyzers aren't wired — recheck the three `OutputItemType="Analyzer"` ProjectReferences in Step 1.

- [ ] **Step 5: Capture the snapshot**

Run (this writes the committed snapshot — sorted, CGC lines only):
```bash
dotnet build src/Showcase.Validation.Naive/Showcase.Validation.Naive.csproj --no-incremental 2>&1 \
  | grep CGC | sort > src/Showcase.Validation.Naive/Showcase.Validation.Naive.expected.txt
cat src/Showcase.Validation.Naive/Showcase.Validation.Naive.expected.txt
```
Expected: the file is **non-empty** and contains the throw + allocation lines from Step 4.

- [ ] **Step 6: Commit**

```bash
git add src/Showcase.Validation.Naive/ CallgraphClosure.slnx
git commit -m "feat(validation-showcase): add Naive validator with intentional throw-and-catch"
```

---

## Task 3: Optimized project — pure return channel

**Files:**
- Create: `src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.csproj`
- Create: `src/Showcase.Validation.Optimized/QuantityValidator.cs`
- Create: `src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.expected.txt`
- Modify: `CallgraphClosure.slnx`

- [ ] **Step 1: Create the csproj (with analyzer wiring)**

`src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Showcase.Validation.Common\Showcase.Validation.Common.csproj" />
    <ProjectReference Include="..\CallgraphClosure.Attributes\CallgraphClosure.Attributes.csproj" />
    <ProjectReference Include="..\CallgraphClosure.Core\CallgraphClosure.Core.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\MustNotAllocate\MustNotAllocate.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\MustNotThrow\MustNotThrow.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the validator**

`src/Showcase.Validation.Optimized/QuantityValidator.cs`:

```csharp
using System;
using CallgraphClosure.Attributes;
using Showcase.Validation.Common;

namespace Showcase.Validation.Optimized;

public static class QuantityValidator
{
    // Pure return channel: classify without ever throwing or allocating.
    // Manual digit scan over the span; the running accumulator is range-capped
    // every iteration, which also guards against int overflow.
    [MustNotAllocate]
    [MustNotThrow]
    public static bool TryValidate(ReadOnlySpan<char> input, out int value, out QuantityError error)
    {
        value = 0;
        if (input.IsEmpty)
        {
            error = QuantityError.NotANumber;
            return false;
        }

        int acc = 0;
        foreach (var c in input)
        {
            if (c < '0' || c > '9')
            {
                error = QuantityError.NotANumber;
                return false;
            }
            acc = acc * 10 + (c - '0');
            if (acc > 10000)
            {
                error = QuantityError.OutOfRange;
                return false;
            }
        }

        if (acc < 1)
        {
            error = QuantityError.OutOfRange;
            return false;
        }

        value = acc;
        error = QuantityError.None;
        return true;
    }
}
```

- [ ] **Step 3: Add the project to the solution**

In `CallgraphClosure.slnx`, inside `<Folder Name="/src/">`, immediately after the `Showcase.Validation.Naive` line you added in Task 2, add:

```xml
    <Project Path="src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.csproj" />
```

- [ ] **Step 4: Build and verify ZERO CGC diagnostics**

Run:
```bash
dotnet build src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.csproj --no-incremental 2>&1 | grep CGC
```
Expected: **no output** (zero CGC lines). The build itself reports `Build succeeded.`

If any CGC line appears, identify the offending call. If it is an opaque BCL helper that you trust (mirroring the HTTP optimized project's `amortized-methods.json`), add an `amortized-methods.json` listing it and an `<AdditionalFiles Include="amortized-methods.json" />` item to the csproj, then rebuild. Do not suppress a genuine throw/allocation — restructure the code instead.

- [ ] **Step 5: Create the empty snapshot**

Run:
```bash
dotnet build src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.csproj --no-incremental 2>&1 \
  | grep CGC | sort > src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.expected.txt
wc -c src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.expected.txt
```
Expected: `0` bytes (empty file, matching the HTTP optimized snapshot).

- [ ] **Step 6: Commit**

```bash
git add src/Showcase.Validation.Optimized/ CallgraphClosure.slnx
git commit -m "feat(validation-showcase): add Optimized pure-return-channel validator"
```

---

## Task 4: Verify naive/optimized verdict parity

This task has no code — it confirms success criterion #3 (both variants agree on every input) by review, since there is no test project. The two implementations are small enough to check against the truth table by reading them.

- [ ] **Step 1: Check each input against both implementations**

Confirm by reading `Naive.Validate` and `Optimized.TryValidate` that they produce the same `QuantityError` for each input:

| Input | Naive path | Optimized path | Agree? |
|---|---|---|---|
| `"42"` | parse ok, in range → `None` | digits, acc=42, ≥1 → `None` | ✓ |
| `"999999"` | parse ok, >10000 → `throw AOORE` → `OutOfRange` | acc exceeds 10000 → `OutOfRange` | ✓ |
| `"abc"` | `int.Parse` throws `FormatException` → `NotANumber` | `'a' < '0'` → `NotANumber` | ✓ |
| `""` | `int.Parse` throws `FormatException` → `NotANumber` | `IsEmpty` → `NotANumber` | ✓ |
| `"0"` | parse=0, `0 < 1` → `throw AOORE` → `OutOfRange` | acc=0, `acc < 1` → `OutOfRange` | ✓ |

- [ ] **Step 2: Confirm no commit needed**

This is a verification-only task. If any row diverges, fix the implementation in the relevant project and amend that project's commit's follow-up with a new commit. Otherwise proceed.

---

## Task 5: Benchmark project

**Files:**
- Create: `bench/Showcase.Validation.Benchmarks/Showcase.Validation.Benchmarks.csproj`
- Create: `bench/Showcase.Validation.Benchmarks/Program.cs`
- Create: `bench/Showcase.Validation.Benchmarks/Benchmarks.cs`
- Modify: `CallgraphClosure.slnx`

- [ ] **Step 1: Create the csproj**

`bench/Showcase.Validation.Benchmarks/Showcase.Validation.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
    <ProjectReference Include="..\..\src\Showcase.Validation.Naive\Showcase.Validation.Naive.csproj" />
    <ProjectReference Include="..\..\src\Showcase.Validation.Optimized\Showcase.Validation.Optimized.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the entrypoint**

`bench/Showcase.Validation.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;

namespace Showcase.Validation.Benchmarks;

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
```

- [ ] **Step 3: Create the benchmarks**

`bench/Showcase.Validation.Benchmarks/Benchmarks.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using Showcase.Validation.Common;

namespace Showcase.Validation.Benchmarks;

[MemoryDiagnoser]
public class ValidateBenchmarks
{
    // Valid vs out-of-range. The out-of-range case is exactly the throw that
    // [MustNotThrow] flagged in the naive validator, so the benchmarked cost
    // lines up with what the analyzer caught.
    [Params("42", "999999")]
    public string Input = "";

    [Benchmark(Baseline = true)]
    public QuantityError Naive() =>
        Showcase.Validation.Naive.QuantityValidator.Validate(Input).Error;

    [Benchmark]
    public QuantityError Optimized()
    {
        Showcase.Validation.Optimized.QuantityValidator.TryValidate(Input.AsSpan(), out _, out var error);
        return error;
    }
}
```

- [ ] **Step 4: Add the project to the solution**

In `CallgraphClosure.slnx`, inside `<Folder Name="/bench/">`, immediately after the `Showcase.Http.Benchmarks` line, add:

```xml
    <Project Path="bench/Showcase.Validation.Benchmarks/Showcase.Validation.Benchmarks.csproj" />
```

- [ ] **Step 5: Build the bench project**

Run: `dotnet build bench/Showcase.Validation.Benchmarks/Showcase.Validation.Benchmarks.csproj`
Expected: `Build succeeded.` (CGC warnings may appear from the referenced Naive project — that is expected and harmless; the bench csproj is not analyzed itself.)

- [ ] **Step 6: Commit**

```bash
git add bench/Showcase.Validation.Benchmarks/ CallgraphClosure.slnx
git commit -m "feat(validation-showcase): add BenchmarkDotNet project"
```

---

## Task 6: Run benchmarks and capture baseline results

**Files:**
- Create: `bench/Showcase.Validation.Benchmarks/baseline-results.md`

- [ ] **Step 1: Run the benchmarks in Release**

Run:
```bash
dotnet run -c Release --project bench/Showcase.Validation.Benchmarks/ -- --filter '*'
```
Expected: BDN runs both benchmarks across both `[Params]` values (4 rows total) and prints a summary table with `Mean`, `Ratio`, `Gen0`, and `Allocated` columns. Wall time ~1-3 minutes. **Release is mandatory** — Debug numbers are misleading.

Expected shape (your exact numbers will differ): on `Input="42"` the two are comparable; on `Input="999999"` Naive is materially slower and shows a non-zero `Allocated` (the exception object), while Optimized is flat with `-` / `0 B`.

- [ ] **Step 2: Write the results doc**

Create `bench/Showcase.Validation.Benchmarks/baseline-results.md` using the BDN summary table you just captured. Follow the structure of `bench/Showcase.Http.Benchmarks/baseline-results.md`:

```markdown
# Showcase.Validation.Benchmarks — Baseline Results

Captured: <DATE> on <runtime/arch/OS line from the BDN host block>

## How this was run

\```bash
dotnet run -c Release --project bench/Showcase.Validation.Benchmarks/ -- --filter '*'
\```

- **Release configuration is mandatory** — Debug numbers are misleading.
- **`--filter '*'`** runs every `[Benchmark]`. `[MemoryDiagnoser]` produces the `Allocated` / `Gen0` columns.
- **`[Params]`** runs each benchmark for both `"42"` (valid) and `"999999"` (out-of-range).

## ValidateBenchmarks

<paste the BDN markdown table here — 4 rows: Naive/Optimized × "42"/"999999">

## Summary

- Valid input ("42"): <one line — Naive vs Optimized timing, both 0 B>
- Out-of-range input ("999999"): <one line — Naive throws+unwinds and allocates N B; Optimized is flat and 0 B>
```

Fill every `<…>` placeholder with the real captured values. Do not commit the doc with placeholders left in.

- [ ] **Step 3: Commit**

```bash
git add bench/Showcase.Validation.Benchmarks/baseline-results.md
git commit -m "docs(validation-showcase): capture BenchmarkDotNet baseline results"
```

---

## Task 7: Full-solution build + roadmap update

**Files:**
- Modify: `docs/ROADMAP.md`

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build CallgraphClosure.slnx`
Expected: `Build succeeded.` All four new projects compile. CGC warnings from `Showcase.Validation.Naive` (and the existing `Showcase.Http.Naive`) are expected; no errors.

- [ ] **Step 2: Update the roadmap state table**

In `docs/ROADMAP.md`, in the "Where we are" table, add a row marking the showcase done (place it after the existing showcase / four-properties rows):

```markdown
| `[MustNotThrow]` validation showcase (Naive throw-and-catch vs Optimized return channel) with BenchmarkDotNet | ✅ |
```

- [ ] **Step 3: Mark medium-term item #2 as done**

In `docs/ROADMAP.md`, under "### 2. `[MustNotThrow]` showcase: exception-free error-handling pipeline", prepend a status line directly under the heading:

```markdown
**Status:** ✅ Done (2026-05-29). Implemented as the quantity-validation showcase: `src/Showcase.Validation.{Common,Naive,Optimized}` + `bench/Showcase.Validation.Benchmarks`. See `docs/superpowers/specs/2026-05-28-mustnotthrow-showcase-design.md`.
```

Also update the "As of" date and tag list at the top of the file if that is the project convention (check the first line).

- [ ] **Step 4: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs(roadmap): mark [MustNotThrow] showcase (item #2) done"
```

---

## Done criteria (verify all before declaring complete)

- [ ] `dotnet build CallgraphClosure.slnx` succeeds.
- [ ] `src/Showcase.Validation.Optimized/Showcase.Validation.Optimized.expected.txt` is empty (0 bytes).
- [ ] `src/Showcase.Validation.Naive/Showcase.Validation.Naive.expected.txt` is non-empty and contains a CGC003 throw + a CGC003 allocation for `Validate`.
- [ ] Parity truth table (Task 4) holds for all five inputs.
- [ ] `bench/Showcase.Validation.Benchmarks/baseline-results.md` exists with real numbers (no placeholders) showing the failure-path gap.
- [ ] All four new projects are in `CallgraphClosure.slnx`.
- [ ] `docs/ROADMAP.md` item #2 marked done.
