using System;
using System.Collections.Immutable;
using System.Text.Json;

namespace CallgraphClosure.ILCheck.Core;

public sealed class AmortizedSet
{
    private readonly ImmutableHashSet<string> _methods;

    private AmortizedSet(ImmutableHashSet<string> methods) => _methods = methods;

    public static AmortizedSet Empty { get; } = new(ImmutableHashSet<string>.Empty);

    public bool Contains(string methodFullName) => _methods.Contains(methodFullName);

    public static AmortizedSet Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Amortized annotations file is not valid JSON", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("amortized_methods", out var arr))
                return Empty;

            if (arr.ValueKind != JsonValueKind.Array)
                throw new FormatException("'amortized_methods' must be a JSON array");

            var builder = ImmutableHashSet.CreateBuilder<string>();
            foreach (var element in arr.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    throw new FormatException("'amortized_methods' entries must be strings");

                var name = element.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    builder.Add(name);
            }

            return new AmortizedSet(builder.ToImmutable());
        }
    }
}
