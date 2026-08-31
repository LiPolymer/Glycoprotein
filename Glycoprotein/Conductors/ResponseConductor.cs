using System.Collections.Concurrent;
using System.Text.Json;
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
    
    public bool RemoveField(string fid) {
        return _responders.TryRemove(fid,out _);
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
            QuerySchema = Glycosyl.GenerateSchema<T1>(),
            ReceiptSchema = Glycosyl.GenerateSchema<T2>()
        },je => {
            if (je == null) return null;
            T1? param = je.Value.Deserialize<T1>();
            if (param == null) return null;
            return Glycosyl.SerializeToJsonElement(fun(param));
        });
    }

    public void AddFunction<T>(Field.Method meta,Func<T> query) {
        AddRawFunction(meta with {
            QuerySchema = null,
            ReceiptSchema = Glycosyl.GenerateSchema<T>()
        },_ => Glycosyl.SerializeToJsonElement(query()));
    }

    public void AddAction<T>(Field.Method meta,Action<T> reactor) {
        AddRawFunction(meta with {
            QuerySchema = Glycosyl.GenerateSchema<T>(),
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
        if (!_responders.TryGetValue(query.Fid,out (Field.Method Meta,Func<JsonElement?,JsonElement?> Func) f)) return;

        JsonElement? payload = null;
        string? error = null;
        try {
            payload = f.Func(query.Payload);
        } catch (Exception ex) {
            error = ex.Message;
            Console.WriteLine($"字段 '{query.Fid}' 处理异常: {ex.Message}");
        }

        try {
            _connexon.Send(new Glycosyl.Reply {
                Payload = payload,
                Qid = query.Qid,
                TargetGid = query.SourceGid,
                Error = error
            });
        } catch (Exception ex) {
            Console.WriteLine($"Reply 发送失败: {ex.Message}");
        }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _connexon.OnGlycosylReceived -= OnReceived;
    }
}