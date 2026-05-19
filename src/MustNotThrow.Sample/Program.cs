using System;
using CallgraphClosure.Attributes;

// Toy "request validator" — demonstrates the Validate-throws vs TryValidate-bool
// dichotomy from the writeup. The [MustNotThrow] HandleRequest is loud on the
// naive Validate path and silent on TryValidate.

var validator = new Validator();
validator.HandleRequest("hello");

internal sealed class Validator
{
    [MustNotThrow]
    public void HandleRequest(string input)
    {
        // Violation 1: CGC003 "throw" — direct throw at the sink.
        if (input is null) throw new ArgumentNullException(nameof(input));

        // Violation 2: CGC001 — Validate is unannotated. The IL post-pass walks
        // into Validate and surfaces its inner throw transitively.
        Validate(input);

        // No diagnostic — TryValidate is [MustNotThrow] and returns a bool channel
        // instead of throwing. This is the path a hot-path caller would take.
        if (!TryValidate(input, out _))
        {
            // recover without throwing
        }
    }

    private void Validate(string input)
    {
        if (input.Length == 0) throw new ArgumentException("empty", nameof(input));
    }

    [MustNotThrow]
    private bool TryValidate(string input, out string? error)
    {
        if (input.Length == 0) { error = "empty"; return false; }
        error = null;
        return true;
    }
}
