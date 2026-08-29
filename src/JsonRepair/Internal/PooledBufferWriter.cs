using System;
using System.Buffers;

namespace JsonRepair.Internal;

/// <summary>
/// Minimal <see cref="IBufferWriter{T}"/> over an <see cref="ArrayPool{T}"/> buffer.
/// Used to stage repaired UTF-8 output for validation before it is handed to the caller's writer.
/// </summary>
/// <remarks>
/// <para>
/// The instance itself is cached per thread, because allocating one per call put 32 B/op back on a
/// path whose whole point is not to allocate. <see cref="Rent"/> takes the cached instance and
/// clears the cache for the duration, so a re-entrant repair — the caller's
/// <see cref="IBufferWriter{T}"/> could call back into the engine while it is being written to —
/// gets its own instance rather than corrupting this one.
/// </para>
/// <para>
/// Only the object is cached. Its backing array is rented on <see cref="Rent"/> and returned on
/// <see cref="Dispose"/>, so a thread that repairs one large document does not then pin a large
/// array for the rest of its life.
/// </para>
/// </remarks>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private static readonly byte[] EmptyBuffer = [];

    [ThreadStatic]
    private static PooledBufferWriter? _cached;

    private byte[] _buffer = EmptyBuffer;

    private PooledBufferWriter()
    {
    }

    public int WrittenCount { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    /// <summary>Takes a writer for this thread. Dispose returns it, along with its backing array.</summary>
    public static PooledBufferWriter Rent(int capacity)
    {
        PooledBufferWriter writer = _cached ?? new PooledBufferWriter();
        _cached = null; // rented out: a nested call must not be handed the same instance

        writer._buffer = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, 256));
        writer.WrittenCount = 0;
        return writer;
    }

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
        if (_buffer.Length > 0) {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = EmptyBuffer;
        }
        WrittenCount = 0;
        _cached = this;
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
