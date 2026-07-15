using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Schema;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Conductors;

public class ResponseConductor : IDisposable {
    readonly IConnexon _connexon;
    readonly string _gid;
    readonly ConcurrentDictionary<string,(Field.Action Meta,Action Act)> _actions = [];
    readonly ConcurrentDictionary<string,(Field.Function Meta,Func<JsonElement?,JsonElement?> Func)> _functions = [];
    
    public IReadOnlyList<Field> Fields {
        get => _actions.Select(Field (kvp) => kvp.Value.Meta)
            .Concat(_functions
                        .Select(Field (kvp) => kvp.Value.Meta))
            .ToArray();
    }

    public ResponseConductor(IConnexon connexon, string gid) {
        _connexon = connexon;
        _gid = gid;
        _connexon.OnGlycosylReceived += OnReceived;
    }

    public void AddAction(Field.Action meta, Action action) {
        _actions[meta.Id] = (meta,action);
    }

    public void AddBareFunction(Field.Function meta, Func<JsonElement?,JsonElement?> fun) {
        _functions[meta.Id] = (meta,fun);
    }
    
    public void AddFunction<T1,T2>(Field.Function meta, Func<T1,T2> fun) {
        AddBareFunction(new Field.Function {
            Id = meta.Id,
            FriendlyName = meta.FriendlyName,
            Description = meta.Description,
            QuerySchema = JsonSerializer.SerializeToElement(Glycosyl.Jso.GetJsonSchemaAsNode(typeof(T1))),
            ReceiptSchema = JsonSerializer.SerializeToElement(Glycosyl.Jso.GetJsonSchemaAsNode(typeof(T2)))
        },je => {
            if (je == null) return null;
            T1? param = je.Value.Deserialize<T1>();
            if (param == null) return null;
            return JsonSerializer.SerializeToElement(fun(param));
        });
    }
    
    void OnReceived(Glycosyl gly) {
        if (gly is not Glycosyl.Query query) return;
        if (query.Gid != _gid) return;
        if (_functions.TryGetValue(query.Fid, out (Field.Function Meta,Func<JsonElement?,JsonElement?> Func) f)) {
            _connexon.Send(new Glycosyl.Reply {
                Payload = f.Func(query.Payload),
                Qid = query.Qid 
            });
        } else if (_actions.TryGetValue(query.Fid,out (Field.Action Meta,Action Act) a)) {
            a.Act();
            _connexon.Send(new Glycosyl.Reply {
                Qid = query.Qid 
            });
        }
    }

    public void Dispose() {
        _connexon.OnGlycosylReceived -= OnReceived;
    }
}