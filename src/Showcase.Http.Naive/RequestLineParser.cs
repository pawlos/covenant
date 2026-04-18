using System;
using CallgraphClosure.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Naive;

public static class RequestLineParser
{
    [MustNotAllocate]
    public static NaiveParsedRequest Parse(string line)
    {
        // Allocations all over the place — every one is intentional.
        var parts = line.Split(' ');         // new string[] + N substring allocations
        if (parts.Length != 3)
            throw new FormatException("Malformed request line");

        var method = parts[0];
        var target = parts[1];
        var version = parts[2];

        string path;
        string query;
        var queryIdx = target.IndexOf('?');
        if (queryIdx >= 0)
        {
            path = target.Substring(0, queryIdx);   // new string
            query = target.Substring(queryIdx + 1); // new string
        }
        else
        {
            path = target;
            query = string.Empty;
        }

        return new NaiveParsedRequest(method, path, query, version);  // new class
    }
}
