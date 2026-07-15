using System.Net;
using System.Net.Sockets;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Connexon;

/// <summary>
/// Connexon based on UDP Multicast.
/// </summary>
[Obsolete("This implementation could lead to data loss due to the limitation of UDP protocol.")]
public sealed class MulticastConnexon(IPEndPoint endPoint) : IConnexon {
    public const int MaxPayloadSize = 65507;
    public const int SafePayloadSize = 1400;

    public event Action<Glycosyl>? OnGlycosylReceived;

    UdpClient? _client;
    CancellationTokenSource? _cts;
    bool _disposed;

    public CancellationToken CancellationToken {
        get => (_cts ?? throw new InvalidOperationException("MulticastConnexon is not started.")).Token;
    }

    public void Start() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cts != null) return;

        _cts = new CancellationTokenSource();

        _client = new UdpClient(endPoint.Address.AddressFamily) {
            MulticastLoopback = true
        };
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        IPAddress bindAddress = endPoint.Address.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;

        _client.Client.Bind(new IPEndPoint(bindAddress, endPoint.Port));
        _client.JoinMulticastGroup(endPoint.Address);

        _ = ReceiveLoopAsync(_cts.Token);
    }

    public void Stop() {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _client?.Dispose();
        _client = null;
    }

    public async Task SendAsync(Glycosyl glycosyl,CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await SendBytesAsync(glycosyl.ToBytes(), ct);
    }
    
    public async Task SendBytesAsync(byte[] data, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (data.Length) {
            case > MaxPayloadSize:
                throw new InvalidOperationException($"Message size {data.Length} exceeds maximum UDP payload size {MaxPayloadSize:N0} bytes. Reduce the number of fields or split into multiple smaller services.");
            case > SafePayloadSize:
                Console.WriteLine($"Warning: Message is {data.Length} bytes, exceeding safe UDP size of {SafePayloadSize:N0} bytes. Consider reducing the number of fields to avoid IP fragmentation.");
                break;
        }
        await _client!.SendAsync(data, endPoint, ct);
    }

    public void SendBytes(byte[] data) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (data.Length) {
            case > MaxPayloadSize:
                throw new InvalidOperationException($"Message size {data.Length} exceeds maximum UDP payload size {MaxPayloadSize:N0} bytes. Reduce the number of fields or split into multiple smaller services.");
            case > SafePayloadSize:
                Console.WriteLine($"Warning: Message is {data.Length} bytes, exceeding safe UDP size of {SafePayloadSize:N0} bytes. Consider reducing the number of fields to avoid IP fragmentation.");
                break;
        }
        _client!.Send(data, endPoint);
    }

    public void Send(Glycosyl glycosyl) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SendBytes(glycosyl.ToBytes());
    }
    
    async Task ReceiveLoopAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested && _client != null) {
            try {
                UdpReceiveResult result = await _client.ReceiveAsync(ct);
                OnGlycosylReceived?.Invoke(Glycosyl.FromBytes(result.Buffer));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) {
                Console.WriteLine($"接收异常: {ex.Message}");
            }
        }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
