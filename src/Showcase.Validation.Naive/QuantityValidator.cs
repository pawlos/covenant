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
