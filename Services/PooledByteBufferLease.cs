using System.Buffers;

namespace ArIED61850Tester.Services;

/// <summary>
/// ArrayPool-backed lease for transient network/codec buffers owned by ARSAS.
/// Do not use this for buffers whose lifetime is owned by ARIEC61850, Npcap, WPF bindings,
/// report snapshots, or evidence objects. Pooling is deliberately limited to byte buffers
/// with a clear rent/use/return lifetime so use-after-return cannot corrupt process data.
/// </summary>
public sealed class PooledByteBufferLease : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;
    private readonly bool _clearOnReturn;

    private PooledByteBufferLease(int minimumLength, bool clearOnReturn)
    {
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength));

        _buffer = ArrayPool<byte>.Shared.Rent(minimumLength);
        _length = minimumLength;
        _clearOnReturn = clearOnReturn;
    }

    public int Length => _length;

    public Memory<byte> Memory
    {
        get
        {
            var buffer = Volatile.Read(ref _buffer)
                ?? throw new ObjectDisposedException(nameof(PooledByteBufferLease));
            return buffer.AsMemory(0, _length);
        }
    }

    public Span<byte> Span
    {
        get
        {
            var buffer = Volatile.Read(ref _buffer)
                ?? throw new ObjectDisposedException(nameof(PooledByteBufferLease));
            return buffer.AsSpan(0, _length);
        }
    }

    public static PooledByteBufferLease Rent(int minimumLength, bool clearOnReturn = false)
        => new(minimumLength, clearOnReturn);

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is null)
            return;

        ArrayPool<byte>.Shared.Return(buffer, _clearOnReturn);
    }
}
