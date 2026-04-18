using System.IO;
using System.Text;
using CallgraphClosure.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Naive;

public sealed class RequestReader
{
    private const int BufferSize = 4096;

    [MustNotAllocate]
    public NaiveParsedRequest ReadNext(Stream input)
    {
        var buffer = new byte[BufferSize];   // per-call heap array — CGC003
        var bytesRead = input.Read(buffer, 0, BufferSize);
        var line = Encoding.UTF8.GetString(buffer, 0, bytesRead); // allocates a string

        // Strip trailing CRLF for parsing.
        var eol = line.IndexOf('\r');
        if (eol < 0) eol = line.Length;

        return RequestLineParser.Parse(line.Substring(0, eol));  // another substring
    }
}
