using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ArIED61850Tester.Protocol.Osi;

public sealed class TpktClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public bool IsConnected => _tcpClient?.Connected == true;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        _tcpClient = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await _tcpClient.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();
    }

    public async Task SendTpktAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_stream == null) throw new InvalidOperationException("TPKT stream is not connected.");
        if (payload.Length > ushort.MaxValue - 4) throw new ArgumentOutOfRangeException(nameof(payload), "TPKT payload is too large.");

        var frame = new byte[payload.Length + 4];
        frame[0] = 0x03; // TPKT version
        frame[1] = 0x00;
        var length = frame.Length;
        frame[2] = (byte)(length >> 8);
        frame[3] = (byte)(length & 0xff);
        payload.CopyTo(frame.AsMemory(4));
        await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ReceiveTpktAsync(CancellationToken cancellationToken)
    {
        if (_stream == null) throw new InvalidOperationException("TPKT stream is not connected.");

        var header = await ReadExactAsync(4, cancellationToken).ConfigureAwait(false);
        if (header[0] != 0x03) throw new InvalidDataException($"Unsupported TPKT version {header[0]}.");
        var length = (header[2] << 8) | header[3];
        if (length < 4) throw new InvalidDataException($"Invalid TPKT length {length}.");
        return await ReadExactAsync(length - 4, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        if (_stream == null) throw new InvalidOperationException("TPKT stream is not connected.");
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new IOException("Remote IEC 61850 peer closed the TCP connection.");
            offset += read;
        }
        return buffer;
    }

    public ValueTask DisposeAsync()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        _stream = null;
        _tcpClient = null;
        return ValueTask.CompletedTask;
    }
}
