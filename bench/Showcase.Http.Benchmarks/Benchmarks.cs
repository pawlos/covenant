using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Benchmarks;

[MemoryDiagnoser]
public class ParseBenchmarks
{
    private const string RequestLine = "GET /users?id=42&sort=asc HTTP/1.1";
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes(RequestLine);

    [Benchmark(Baseline = true)]
    public int Naive()
    {
        var req = Showcase.Http.Naive.RequestLineParser.Parse(RequestLine);
        return req.Method.Length + req.Path.Length + req.Query.Length + req.Version.Length;
    }

    [Benchmark]
    public int Optimized()
    {
        if (!Showcase.Http.Optimized.RequestLineParser.TryParse(_bytes, out var req))
            return -1;
        return req.Method.Length + req.Path.Length + req.Query.Length + req.Version.Length;
    }
}

[MemoryDiagnoser]
public class ReadBenchmarks
{
    private const string RequestLine = "GET /users?id=42&sort=asc HTTP/1.1\r\n";
    private readonly byte[] _requestBytes = Encoding.UTF8.GetBytes(RequestLine);
    private readonly Showcase.Http.Naive.RequestReader _naiveReader = new();
    private readonly Showcase.Http.Optimized.RequestReader _optimizedReader = new();

    [Benchmark(Baseline = true)]
    public int Naive()
    {
        using var stream = new MemoryStream(_requestBytes);
        var req = _naiveReader.ReadNext(stream);
        return req.Path.Length;
    }

    [Benchmark]
    public int Optimized()
    {
        using var stream = new MemoryStream(_requestBytes);
        using var lease = _optimizedReader.ReadNext(stream);
        if (!Showcase.Http.Optimized.RequestLineParser.TryParse(lease.AsSpan(), out var req))
            return -1;
        return req.Path.Length;
    }
}
