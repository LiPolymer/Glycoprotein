using System.Collections.Concurrent;
using System.Text.Json;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Conductors;

public sealed class EventReceiver : IDisposable {
    readonly ConcurrentDictionary<(string Gid,string Fid),Action<JsonElement?>> _handlers = [];
    readonly IConnexon _connexon;
    bool _disposed;

    public EventReceiver(IConnexon connexon) {
        _connexon = connexon;
        connexon.OnGlycosylReceived += OnReceived;
    }

    public void AddEvent(string gid,string fid,Action handler) {
        _handlers[(gid,fid)] = _ => handler();
    }

    public void AddEvent(string gid,string fid,Action<JsonElement?> handler) {
        _handlers[(gid,fid)] = handler;
    }

    public void AddEvent<T>(string gid,string fid,Action<T> handler) {
        _handlers[(gid,fid)] = je => {
            if (je == null) return;
            T? arg = je.Value.Deserialize<T>();
            if (arg == null) return;
            handler(arg);
        };
    }

    void OnReceived(Glycosyl gly) {
        if (_disposed) return;
        if (gly is not Glycosyl.Event evt) return;
        if (!_handlers.TryGetValue((evt.Gid,evt.Fid),out Action<JsonElement?>? h)) return;
        h(evt.Arg);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _connexon.OnGlycosylReceived -= OnReceived;
        _handlers.Clear();
    }
}