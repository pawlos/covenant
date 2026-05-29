using System;
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
