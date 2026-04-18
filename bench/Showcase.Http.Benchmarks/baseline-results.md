# Showcase.Http.Benchmarks — Baseline Results

Captured: 2026-04-17 on .NET 10.0.3 (10.0.326.7603), X64 RyuJIT AVX2, WSL2 (Linux 6.6.87.1-microsoft-standard-WSL2)

## ParseBenchmarks

| Method    | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| Naive     | 53.121 ns | 1.5120 ns | 4.1645 ns |  1.00 | 0.0178 |     280 B |        1.00 |
| Optimized |  6.890 ns | 0.0780 ns | 0.0729 ns |  0.12 |      - |         - |        0.00 |

## ReadBenchmarks

| Method    | Mean      | Error    | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------- |----------:|---------:|----------:|------:|-------:|----------:|------------:|
| Naive     | 257.45 ns | 7.708 ns | 22.363 ns |  1.00 | 0.2999 |    4704 B |        1.00 |
| Optimized |  26.52 ns | 0.452 ns |  0.422 ns |  0.10 | 0.0041 |      64 B |        0.01 |

## Summary

- Parse: Optimized is ~7.7x faster (6.9 ns vs 53.1 ns) and allocates 0 B vs Naive's 280 B per call
- Read: Optimized is ~9.7x faster (26.5 ns vs 257.5 ns) and allocates 64 B (ArrayPool overhead) vs Naive's 4704 B per call
