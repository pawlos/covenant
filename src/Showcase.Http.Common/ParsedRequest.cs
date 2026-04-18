using System;

namespace Showcase.Http.Common;

// Naive variant: class-wrapped strings, heap-allocated per parse.
public sealed class NaiveParsedRequest
{
    public string Method { get; }
    public string Path { get; }
    public string Query { get; }
    public string Version { get; }

    public NaiveParsedRequest(string method, string path, string query, string version)
    {
        Method = method;
        Path = path;
        Query = query;
        Version = version;
    }
}

// Optimized variant: ref struct over the original buffer, zero allocation.
public readonly ref struct OptimizedParsedRequest
{
    public ReadOnlySpan<byte> Method { get; }
    public ReadOnlySpan<byte> Path { get; }
    public ReadOnlySpan<byte> Query { get; }
    public ReadOnlySpan<byte> Version { get; }

    public OptimizedParsedRequest(
        ReadOnlySpan<byte> method,
        ReadOnlySpan<byte> path,
        ReadOnlySpan<byte> query,
        ReadOnlySpan<byte> version)
    {
        Method = method;
        Path = path;
        Query = query;
        Version = version;
    }
}
