# Showcase.Validation.Benchmarks — Baseline Results

Captured: 2026-05-29 on .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2, 12th Gen Intel Core i9-12900K, Ubuntu 22.04.5 LTS (WSL). BenchmarkDotNet v0.13.12, SDK 11.0.100-preview.2.

## How this was run

```bash
dotnet run -c Release --project bench/Showcase.Validation.Benchmarks/ -- --filter '*'
```

- **Release configuration is mandatory** — Debug numbers are misleading (both variants slower, and the ratio shifts because Debug suppresses inlining).
- **`--filter '*'`** runs every `[Benchmark]`. `[MemoryDiagnoser]` produces the `Allocated` / `Gen0` columns.
- **`[Params]`** runs each benchmark for both `"42"` (valid) and `"999999"` (out-of-range), so the table has four rows.
- **No custom job** — default BDN job (full warmup, multiple iterations). Total wall time ~1.5 minutes.

## ValidateBenchmarks

| Method    | Input  | Mean          | Error      | StdDev     | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------- |------- |--------------:|-----------:|-----------:|------:|-------:|----------:|------------:|
| Naive     | 42     |     4.7718 ns |  0.0825 ns |  0.0772 ns |  1.00 |      - |         - |          NA |
| Optimized | 42     |     0.9118 ns |  0.0392 ns |  0.0347 ns |  0.19 |      - |         - |          NA |
|           |        |               |            |            |       |        |           |             |
| Naive     | 999999 | 1,151.1143 ns | 22.7345 ns | 23.3467 ns | 1.000 | 0.0134 |     232 B |        1.00 |
| Optimized | 999999 |     2.5542 ns |  0.0574 ns |  0.0537 ns | 0.002 |      - |         - |        0.00 |

## Summary

- **Valid input (`"42"`):** neither variant allocates (the naive path never throws on valid input). Optimized is ~5.2x faster (0.91 ns vs 4.77 ns), but in absolute terms both are sub-5-nanosecond — the happy path is cheap either way.
- **Out-of-range input (`"999999"`):** this is where the cost lives. The naive validator throws `ArgumentOutOfRangeException` and catches it internally — paying **1,151 ns** and allocating **232 B** (the exception object) per call. The optimized validator returns an error code: **2.55 ns, 0 B**. That is ~450x slower and 232 B vs nothing, entirely from using an exception as a control-flow signal.
- This is exactly the throw that `[MustNotThrow]` flagged in `Showcase.Validation.Naive` (CGC003 throw + CGC003 allocation, see `Showcase.Validation.Naive.expected.txt`). The analyzer caught at build time the cost this benchmark measures at runtime.
