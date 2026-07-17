using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Schema;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Conductors;

public sealed class EventEmitter(IConnexon connexon, string gid) {
    readonly ConcurrentDictionary<string,(Field.Event Field,Type? ArgType)> _events = [];

    public IReadOnlyList<Field> Fields {
        get => _events.Select(kvp => kvp.Value.Field).ToArray();
    }

    public void AddEvent(Field.Event field) {
        if (field.CallArgSchema != null) return;
        _events[field.Id] = (field,null);
    }

    public void AddEvent<T>(Field.Event field) {
        _events[field.Id] = (field with { 
            CallArgSchema = JsonSerializer
                .SerializeToElement(Glycosyl.Jso.GetJsonSchemaAsNode(typeof(T))) 
        },typeof(T));
    }
    
    public async Task EmitEventRawAsync(string fid, JsonElement? args = null) {
        await connexon.SendAsync(new Glycosyl.Event {
            Gid = gid,
            Fid = fid,
            Arg = args
        });
    }

    public async Task EmitEventAsync(string fid) {
        if (!_events.TryGetValue(fid,out (Field.Event Field,Type? ArgType) em) 
            || em.ArgType != null) return;
        await connexon.SendAsync(new Glycosyl.Event {
            Gid = gid,
            Fid = fid,
            Arg = null
        });
    }

    public async Task EmitEventAsync<T>(string fid,T arg) {
        if (!_events.TryGetValue(fid,out (Field.Event Field,Type? ArgType) em) 
            || em.ArgType != typeof(T)) return;
        await connexon.SendAsync(new Glycosyl.Event {
            Gid = gid,
            Fid = fid,
            Arg = JsonSerializer.SerializeToElement(arg)
        });
    }
}