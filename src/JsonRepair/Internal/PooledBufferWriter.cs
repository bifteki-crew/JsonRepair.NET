using System;
using System.Buffers;

namespace JsonRepair.Internal;

/// <summary>
/// Minimal <see cref="IBufferWriter{T}"/> over an <see cref="ArrayPool{T}"/> buffer.
/// Used to stage repaired UTF-8 output for validation before it is handed to the caller's writer.
/// </summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;

    public PooledBufferWriter(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));
    }

    public int WrittenCount { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (WrittenCount + count > _buffer.Length) {
            throw new InvalidOperationException("Advanced past the end of the buffer.");
        }
        WrittenCount += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
    }

    private void Ensure(int sizeHint)
    {
        if (sizeHint <= 0) {
            sizeHint = 1;
        }
        if (WrittenCount + sizeHint <= _buffer.Length) {
            return;
        }

        int newCapacity = Math.Max(_buffer.Length * 2, WrittenCount + sizeHint);
        byte[] larger = ArrayPool<byte>.Shared.Rent(newCapacity);
        _buffer.AsSpan(0, WrittenCount).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
