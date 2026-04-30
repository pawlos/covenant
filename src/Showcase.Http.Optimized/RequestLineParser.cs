using System;
using CallgraphClosure.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Optimized;

public static class RequestLineParser
{
    /// <summary>
    /// Parses an HTTP request line from a raw byte span without any heap allocation.
    /// Returns false if the line is malformed.
    /// </summary>
    [MustNotAllocate]
    [MustNotThrow]
    public static bool TryParse(ReadOnlySpan<byte> line, out OptimizedParsedRequest result)
    {
        result = default;

        // Strip trailing CRLF.
        var eol = line.IndexOf((byte)'\r');
        if (eol >= 0) line = line.Slice(0, eol);

        var firstSpace = line.IndexOf((byte)' ');
        if (firstSpace < 0) return false;
        var method = line.Slice(0, firstSpace);

        var rest = line.Slice(firstSpace + 1);
        var secondSpace = rest.IndexOf((byte)' ');
        if (secondSpace < 0) return false;
        var target = rest.Slice(0, secondSpace);
        var version = rest.Slice(secondSpace + 1);

        ReadOnlySpan<byte> path;
        ReadOnlySpan<byte> query;
        var queryIdx = target.IndexOf((byte)'?');
        if (queryIdx >= 0)
        {
            path = target.Slice(0, queryIdx);
            query = target.Slice(queryIdx + 1);
        }
        else
        {
            path = target;
            query = default;
        }

        result = new OptimizedParsedRequest(method, path, query, version);
        return true;
    }
}
