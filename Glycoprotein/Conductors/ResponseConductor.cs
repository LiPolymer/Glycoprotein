using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Schema;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Conductors;

public sealed class ResponseConductor : IDisposable {
    readonly IConnexon _connexon;
    readonly string _gid;
    readonly ConcurrentDictionary<string,(Field.Method Meta,Func<JsonElement?,JsonElement?> Func)> _responders = [];
    bool _disposed;

    public IReadOnlyList<Field> Fields {
        get => _responders
            .Select(kvp => kvp.Value.Meta)
            .ToArray();
    }

    public ResponseConductor(IConnexon connexon,string gid) {
        _connexon = connexon;
        _gid = gid;
        _connexon.OnGlycosylReceived += OnReceived;
    }

    public void AddRawFunction(Field.Method meta,Func<JsonElement?,JsonElement?> fun) {
        _responders[meta.Id] = (meta,fun);
    }

    public void AddAction(Field.Method meta,Action action) {
        AddRawFunction(meta with {
            QuerySchema = null,
            ReceiptSchema = null
        },_ => {
            action();
            return null;
        });
    }

    public void AddFunction<T1,T2>(Field.Method meta,Func<T1,T2> fun) {
        AddRawFunction(meta with {
            QuerySchema = JsonSerializer.SerializeToElement(Glycosyl.Jso.GetJsonSchemaAsNode(typeof(T1))),
            ReceiptSchema = JsonSerializer.SerializeToElement(Glycosyl.Jso.GetJsonSchemaAsNode(typeof(T2)))
        },je => {
            if (je == null) return null;
            T1? param = je.Value.Deserialize<T1>();
            if (param == null) return null;
            return JsonSerializer.SerializeToElement(fun(param));
        });
    }

    public void AddFunction<T>(Field.Method meta,Func<T> query) {
        AddRawFunction(meta with {
            QuerySchema = null,
            ReceiptSchema = JsonSerializer.SerializeToElement(Glycosyl.Jso.GetJsonSchemaAsNode(typeof(T)))
        },_ => JsonSerializer.SerializeToElement(query()));
    }

    public void AddAction<T>(Field.Method meta,Action<T> reactor) {
        AddRawFunction(meta with {
            QuerySchema = JsonSerializer.SerializeToElement(Glycosyl.Jso.GetJsonSchemaAsNode(typeof(T))),
            ReceiptSchema = null
        },je => {
            if (je == null) return null;
            T? param = je.Value.Deserialize<T>();
            if (param == null) return null;
            reactor(param);
            return null;
        });
    }

    void OnReceived(Glycosyl gly) {
        if (_disposed) return;
        if (gly is not Glycosyl.Query query) return;
        if (query.Gid != _gid) return;
        if (_responders.TryGetValue(query.Fid,out (Field.Method Meta,Func<JsonElement?,JsonElement?> Func) f)) {
            _connexon.Send(new Glycosyl.Reply {
                Payload = f.Func(query.Payload),
                Qid = query.Qid
            });
        }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _connexon.OnGlycosylReceived -= OnReceived;
    }
}