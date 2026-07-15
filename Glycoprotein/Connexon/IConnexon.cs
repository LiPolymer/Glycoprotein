using Glycoprotein.Glycosylation;

namespace Glycoprotein.Connexon;

public interface IConnexon : IDisposable {
    event Action<Glycosyl>? OnGlycosylReceived;
    CancellationToken CancellationToken { get; }

    void Start();
    void Stop();
    Task SendAsync(Glycosyl glycosyl, CancellationToken ct = default);
    Task SendBytesAsync(byte[] data, CancellationToken ct = default);
    void SendBytes(byte[] data);
    void Send(Glycosyl glycosyl);
}
