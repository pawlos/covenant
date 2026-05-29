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
