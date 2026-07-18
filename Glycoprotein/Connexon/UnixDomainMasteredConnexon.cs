using System.Net.Sockets;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Connexon;

/// <summary>
/// Single UnixDomainSocket based Connexon with hub election.
/// </summary>
/// <remarks>
/// <b>Tip:</b> This Connexon was coded by Agents,
/// <br/> stability could be a problem.<br/>
/// <i>(btw I have already audited it lol)</i>
/// </remarks>
public sealed class UnixDomainMasteredConnexon : IConnexon {
    public static string DefaultSocketDirectory { get => Path.Combine(Path.GetTempPath(),"glycoprotein_mastered"); }

    public event Action<Glycosyl>? OnGlycosylReceived;

    readonly string _socketDir;
    readonly string _nodeId;
    readonly string _hubSocketPath;

    // Client state (always present)
    NetworkStream? _hubStream;
    CancellationTokenSource? _cts;
    bool _disposed;

    // Hub-only state
    Socket? _listener;
    readonly List<NetworkStream> _clients = [];
    readonly Lock _clientsLock = new Lock();
    bool _isHub;

    // Reconnection
    volatile bool _disconnected;
    readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1,1);

    public CancellationToken CancellationToken { get => (_cts ?? throw new InvalidOperationException("UnixDomainMasteredConnexon is not started.")).Token; }

    public UnixDomainMasteredConnexon(string nodeId,string? socketDirectory = null) {
        _nodeId = nodeId;
        _socketDir = socketDirectory ?? DefaultSocketDirectory;
        _hubSocketPath = Path.Combine(_socketDir,"hub.sock");
    }

    public void Start() {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        Directory.CreateDirectory(_socketDir);

        // Phase 1: Election — try to become the hub
        try {
            _listener = new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_hubSocketPath));
            _listener.Listen(128);
            _isHub = true;
        } catch (SocketException) when (File.Exists(_hubSocketPath)) {
            _listener?.Dispose();
            _listener = null;
        }

        // Phase 2: Hub starts accept loop before connecting to self
        if (_isHub) {
            _ = HubAcceptLoopAsync(_cts.Token);
        }

        // Phase 3: Connect to hub
        ConnectToHub();
    }

    void ConnectToHub() {
        Socket sock = new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        sock.Connect(new UnixDomainSocketEndPoint(_hubSocketPath));
        _hubStream = new NetworkStream(sock,true);
        _disconnected = false;
        _ = ClientReadLoopAsync(_hubStream,_cts!.Token);
    }

    public void Stop() {
        _reconnectLock.Wait(0); // Drain any in-flight reconnect
        try {
        } finally {
            _reconnectLock.Release();
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _hubStream?.Dispose();
        _hubStream = null;

        NetworkStream[] clients;
        lock (_clientsLock) {
            clients = _clients.ToArray();
            _clients.Clear();
        }

        foreach (NetworkStream c in clients) c.Dispose();

        _listener?.Dispose();
        _listener = null;

        if (!_isHub) return;
        try {
            File.Delete(_hubSocketPath);
        } catch {
            // ignored
        }
    }

    public async Task SendAsync(Glycosyl glycosyl,CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        await SendBytesAsync(glycosyl.ToBytes(),ct);
    }

    public async Task SendBytesAsync(byte[] data,CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (_disconnected)
            throw new InvalidOperationException("Disconnected from hub. Reconnection is in progress; retry shortly.");
        byte[] framed = FrameMessage(data);
        await _hubStream!.WriteAsync(framed,ct);
        await _hubStream!.FlushAsync(ct);
    }

    public void SendBytes(byte[] data) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (_disconnected)
            throw new InvalidOperationException("Disconnected from hub. Reconnection is in progress; retry shortly.");
        byte[] framed = FrameMessage(data);
        _hubStream!.Write(framed);
        _hubStream!.Flush();
    }

    public void Send(Glycosyl glycosyl) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        SendBytes(glycosyl.ToBytes());
    }

    // ═══ Hub: accept loop ═══

    async Task HubAcceptLoopAsync(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                Socket client = await _listener!.AcceptAsync(ct);
                NetworkStream stream = new NetworkStream(client,true);
                lock (_clientsLock) {
                    _clients.Add(stream);
                }

                _ = HubReadFromClientAsync(stream,ct);
            }
        } catch (OperationCanceledException) {
        } catch (ObjectDisposedException) {
        }
    }

    // ═══ Hub: read from one client ═══

    async Task HubReadFromClientAsync(NetworkStream stream,CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                byte[] message = await ReadMessageAsync(stream,ct);

                // Dispatch to THIS node's GlycoComplex handlers
                OnGlycosylReceived?.Invoke(Glycosyl.FromBytes(message));

                // Relay to ALL connected clients (UDP multicast parity)
                await HubBroadcastAsync(message,ct);
            }
        } catch (OperationCanceledException) {
        } catch (ObjectDisposedException) {
        } catch {
            /* client disconnected */
        } finally {
            lock (_clientsLock) {
                _clients.Remove(stream);
            }

            stream.Dispose();
        }
    }

    // ═══ Hub: relay to all clients ═══

    async Task HubBroadcastAsync(byte[] data,CancellationToken ct) {
        byte[] framed = FrameMessage(data);
        NetworkStream[] clients;
        lock (_clientsLock) {
            clients = _clients.ToArray();
        }

        foreach (NetworkStream client in clients) {
            try {
                await client.WriteAsync(framed,ct);
                await client.FlushAsync(ct);
            } catch {
                /* cleanup in HubReadFromClientAsync */
            }
        }
    }

    // ═══ Client: read loop ═══

    async Task ClientReadLoopAsync(NetworkStream stream,CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                byte[] message = await ReadMessageAsync(stream,ct);
                OnGlycosylReceived?.Invoke(Glycosyl.FromBytes(message));
            }
        } catch (OperationCanceledException) {
        } catch (ObjectDisposedException) {
        } catch (Exception) when (!ct.IsCancellationRequested && !_disposed) {
            // Hub went down — start reconnection + election
            _disconnected = true;
            _ = ReconnectAsync(ct);
        }
    }

    // ═══ Reconnection with hub election ═══

    async Task ReconnectAsync(CancellationToken ct) {
        if (!await _reconnectLock.WaitAsync(0,ct)) return;
        try {
            while (!ct.IsCancellationRequested && !_disposed) {
                try {
                    _hubStream?.Dispose();
                    _hubStream = null;

                    // Clean old hub state
                    _listener?.Dispose();
                    _listener = null;
                    lock (_clientsLock) {
                        foreach (NetworkStream c in _clients) c.Dispose();
                        _clients.Clear();
                    }

                    _isHub = false;

                    // Phase 1: Try connecting to existing hub
                    Socket sock = new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
                    try {
                        await sock.ConnectAsync(new UnixDomainSocketEndPoint(_hubSocketPath),ct);
                        _hubStream = new NetworkStream(sock,true);
                        _disconnected = false;
                        _ = ClientReadLoopAsync(_hubStream,ct);
                        return;
                    } catch {
                        sock.Dispose();
                    }

                    // Phase 2: No hub — become the hub
                    try {
                        File.Delete(_hubSocketPath);
                    } catch {
                        // ignored
                    }

                    try {
                        _listener = new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
                        _listener.Bind(new UnixDomainSocketEndPoint(_hubSocketPath));
                        _listener.Listen(128);
                        _isHub = true;
                        _ = HubAcceptLoopAsync(ct);
                    } catch (SocketException) {
                        await Task.Delay(100,ct);
                        continue; // Lost election, retry connect
                    }

                    // Phase 3: Connect to self
                    Socket selfSock = new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
                    await selfSock.ConnectAsync(new UnixDomainSocketEndPoint(_hubSocketPath),ct);
                    _hubStream = new NetworkStream(selfSock,true);
                    _disconnected = false;
                    _ = ClientReadLoopAsync(_hubStream,ct);
                    return;
                } catch (OperationCanceledException) {
                    return;
                } catch {
                    try {
                        await Task.Delay(500,ct);
                    } catch (OperationCanceledException) {
                        return;
                    }
                }
            }
        } finally {
            try {
                _reconnectLock.Release();
            } catch {
                // ignored
            }
        }
    }

    // ═══ Framing ═══

    static byte[] FrameMessage(byte[] payload) {
        byte[] framed = new byte[4 + payload.Length];
        BitConverter.TryWriteBytes(framed.AsSpan(0,4),payload.Length);
        Array.Copy(payload,0,framed,4,payload.Length);
        return framed;
    }

    static async Task<byte[]> ReadMessageAsync(Stream stream,CancellationToken ct) {
        byte[] lenBuf = new byte[4];
        await stream.ReadExactlyAsync(lenBuf,0,4,ct);
        int length = BitConverter.ToInt32(lenBuf,0);
        if (length is < 0 or > 1_0000_0000)
            throw new InvalidDataException($"Invalid message length: {length}");
        byte[] buf = new byte[length];
        await stream.ReadExactlyAsync(buf,0,length,ct);
        return buf;
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}