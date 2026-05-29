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
