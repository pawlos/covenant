using System;
using System.Buffers;
using System.IO;
using CallgraphClosure.Attributes;

namespace Showcase.Http.Optimized;

public sealed class RequestReader
{
    private const int BufferSize = 4096;

    [MustNotAllocate]
    public BufferLease ReadNext(Stream input)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var bytesRead = input.Read(buffer, 0, BufferSize);
        return new BufferLease(buffer, bytesRead);
    }
}

public readonly struct BufferLease : IDisposable
{
    private readonly byte[] _buffer;
    public int Length { get; }

    [MustNotAllocate]
    internal BufferLease(byte[] buffer, int length)
    {
        _buffer = buffer;
        Length = length;
    }

    public ReadOnlySpan<byte> AsSpan() => _buffer.AsSpan(0, Length);

    public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
}
