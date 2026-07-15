using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Connexon;

/// <summary>
/// Meshed UnixDomainSockets based Connexon.
/// </summary>
public sealed class UnixDomainMeshConnexon : IConnexon {
    public static string DefaultSocketDirectory {
        get =>
            Path.Combine(Path.GetTempPath(),"glycoprotein");
    }

    public event Action<Glycosyl>? OnGlycosylReceived;

    readonly string _socketDir;
    readonly string _nodeId;
    readonly string _mySocketPath;
    readonly TimeSpan _discoveryInterval = TimeSpan.FromSeconds(2);

    Socket? _listener;
    CancellationTokenSource? _cts;
    bool _disposed;

    readonly ConcurrentDictionary<string, NetworkStream> _peers = new ConcurrentDictionary<string,NetworkStream>();

    public CancellationToken CancellationToken {
        get => (_cts ?? throw new InvalidOperationException("UnixDomainMeshConnexon is not started.")).Token;
    }

    public UnixDomainMeshConnexon(string nodeId, string? socketDirectory = null) {
        _nodeId = nodeId;
        _socketDir = socketDirectory ?? DefaultSocketDirectory;
        _mySocketPath = Path.Combine(_socketDir, $"{_nodeId}.sock");
    }

    public void Start() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        Directory.CreateDirectory(_socketDir);

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_mySocketPath));
        _listener.Listen(128);

        _ = AcceptLoopAsync(_cts.Token);
        _ = DiscoveryLoopAsync(_cts.Token);
    }

    public void Stop() {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _listener?.Dispose();
        _listener = null;

        foreach (KeyValuePair<string,NetworkStream> kvp in _peers)
            kvp.Value.Dispose();
        _peers.Clear();

        try {
            File.Delete(_mySocketPath);
        }
        catch {
            //ignore
        }
    }

    public async Task SendAsync(Glycosyl glycosyl, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await SendBytesAsync(glycosyl.ToBytes(), ct);
    }

    public async Task SendBytesAsync(byte[] data, CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] framed = FrameMessage(data);

        OnGlycosylReceived?.Invoke(Glycosyl.FromBytes(data));

        foreach (KeyValuePair<string,NetworkStream> kvp in _peers) {
            try {
                await kvp.Value.WriteAsync(framed, ct);
                await kvp.Value.FlushAsync(ct);
            }
            catch {
                _peers.TryRemove(kvp.Key, out _);
                try {
                    await kvp.Value.DisposeAsync();
                }
                catch {
                    // ignored
                }
            }
        }
    }

    public void SendBytes(byte[] data) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] framed = FrameMessage(data);

        OnGlycosylReceived?.Invoke(Glycosyl.FromBytes(data));

        foreach (KeyValuePair<string,NetworkStream> kvp in _peers) {
            try {
                kvp.Value.Write(framed);
                kvp.Value.Flush();
            }
            catch {
                _peers.TryRemove(kvp.Key, out _);
                try { kvp.Value.Dispose(); }
                catch {
                    // ignored
                }
            }
        }
    }

    public void Send(Glycosyl glycosyl) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SendBytes(glycosyl.ToBytes());
    }

    async Task DiscoveryLoopAsync(CancellationToken ct) {
        using PeriodicTimer timer = new PeriodicTimer(_discoveryInterval);
        try {
            do {
                await ScanAndConnectAsync(ct);
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    async Task ScanAndConnectAsync(CancellationToken ct) {
        try {
            string[] files = Directory.GetFiles(_socketDir, "*.sock");
            foreach (string file in files) {
                if (ct.IsCancellationRequested) return;
                string otherId = Path.GetFileNameWithoutExtension(file);
                if (otherId == _nodeId) continue;
                if (_peers.ContainsKey(otherId)) continue;

                if (string.Compare(_nodeId, otherId, StringComparison.Ordinal) > 0)
                    continue;

                await ConnectToPeerAsync(otherId, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (DirectoryNotFoundException) { }
    }

    async Task ConnectToPeerAsync(string peerId, CancellationToken ct) {
        string peerPath = Path.Combine(_socketDir, $"{peerId}.sock");
        Socket sock = new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        try {
            await sock.ConnectAsync(new UnixDomainSocketEndPoint(peerPath), ct);
        }
        catch {
            sock.Dispose();
            return;
        }

        NetworkStream stream = new NetworkStream(sock,true);

        byte[] handshake = FrameMessage(Encoding.UTF8.GetBytes(_nodeId));
        await stream.WriteAsync(handshake, ct);
        await stream.FlushAsync(ct);

        _peers[peerId] = stream;
        _ = PeerReadLoopAsync(peerId, stream, ct);
    }

    async Task AcceptLoopAsync(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                Socket client = await _listener!.AcceptAsync(ct);
                _ = HandleIncomingAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    async Task HandleIncomingAsync(Socket socket, CancellationToken ct) {
        NetworkStream stream = new NetworkStream(socket,true);
        try {
            byte[] handshake = await ReadMessageAsync(stream, ct);
            string peerId = Encoding.UTF8.GetString(handshake);

            if (!_peers.TryAdd(peerId,stream)) {
                await stream.DisposeAsync();
                return;
            }

            await PeerReadLoopAsync(peerId, stream, ct);
        }
        catch {
            await stream.DisposeAsync();
        }
    }

    async Task PeerReadLoopAsync(string peerId, NetworkStream stream, CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                byte[] message = await ReadMessageAsync(stream, ct);
                OnGlycosylReceived?.Invoke(Glycosyl.FromBytes(message));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch {
            // ignored
        }
        finally {
            _peers.TryRemove(peerId, out _);
            await stream.DisposeAsync();
        }
    }

    static byte[] FrameMessage(byte[] payload) {
        byte[] framed = new byte[4 + payload.Length];
        BitConverter.TryWriteBytes(framed.AsSpan(0, 4), payload.Length);
        Array.Copy(payload, 0, framed, 4, payload.Length);
        return framed;
    }

    static async Task<byte[]> ReadMessageAsync(Stream stream, CancellationToken ct) {
        byte[] lenBuf = new byte[4];
        await stream.ReadExactlyAsync(lenBuf, 0, 4, ct);
        int length = BitConverter.ToInt32(lenBuf, 0);
        if (length is < 0 or > 1_0000_0000)
            throw new InvalidDataException($"Invalid message length: {length}");
        byte[] buf = new byte[length];
        await stream.ReadExactlyAsync(buf, 0, length, ct);
        return buf;
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
