using System.Collections.Concurrent;
using System.Text.Json;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;
using Json.Schema;

namespace Glycoprotein.Conductors;

public sealed class QueryConductor : IDisposable {
    readonly IConnexon _connexon;
    readonly Func<IReadOnlyList<Glycosyl.Beacon>?> _beaconProvider;

    sealed class PendingRequest(TaskCompletionSource<JsonElement?> tcs, CancellationTokenRegistration ctr) {
        public TaskCompletionSource<JsonElement?> Tcs { get; } = tcs;
        public CancellationTokenRegistration Ctr { get; } = ctr;

        public void Dispose() {
            Ctr.Dispose();
        }
    }

    readonly ConcurrentDictionary<Guid, PendingRequest> _pending = [];
    bool _disposed;

    public QueryConductor(IConnexon connexon, Func<IReadOnlyList<Glycosyl.Beacon>?> beaconProvider) {
        _connexon = connexon;
        _beaconProvider = beaconProvider;
        _connexon.OnGlycosylReceived += OnReceived;
    }

    public async Task DoActionAsync(string gid, string fid, CancellationToken ct = default) {
        ValidateField(gid, fid);
        await SendQueryAndWaitAsync(gid, fid, null, ct);
    }
    
    public async Task DoActionAsync<T>(string gid, string fid, T arg, CancellationToken ct = default) {
        JsonElement? param = JsonSerializer.SerializeToElement(arg);
        await CallFunctionRawAsync(gid, fid, param, ct);
    }

    public async Task<JsonElement?> CallFunctionRawAsync(string gid, string fid, JsonElement? param = null, CancellationToken ct = default) {
        ValidateField(gid, fid, param);
        return await SendQueryAndWaitAsync(gid, fid, param, ct);
    }

    public async Task<TRes?> CallFunctionAsync<TReq,TRes>(string gid, string fid, TReq param, CancellationToken ct = default) {
        JsonElement? rp = await CallFunctionRawAsync(gid,fid,JsonSerializer.SerializeToElement(param),ct);
        return rp == null ? default : rp.Value.Deserialize<TRes>();
    }

    public async Task<T?> CallFunctionAsync<T>(string gid,string fid,CancellationToken ct = default) {
        JsonElement? rp = await CallFunctionRawAsync(gid,fid,null,ct);
        return rp == null ? default : rp.Value.Deserialize<T>();
    }

    void ValidateField(string gid, string fid, JsonElement? param = null) {
        IReadOnlyList<Glycosyl.Beacon>? beacons = _beaconProvider();
        Field? field = beacons?.FirstOrDefault(b => b.Id == gid)?.Fields.FirstOrDefault(f => f.Id == fid);
        if (field is null) throw new InvalidOperationException($"Field '{fid}' on '{gid}' is not exist");
        if (field is not Field.Method method) throw new InvalidOperationException($"Field '{fid}' on '{gid}' is not a Method.");
        if (param == null || method.QuerySchema == null) return;
        EvaluationResults er = JsonSchema.Build(method.QuerySchema.Value).Evaluate(param.Value);
        if (!er.IsValid) {
            throw new InvalidOperationException(string.Join('\n', er.Errors?
                                                                .Select(kvp => $"{kvp.Key} : {kvp.Value}") 
                                                                  ?? ["???"]));
        }
    }

    async Task<JsonElement?> SendQueryAndWaitAsync(string gid, string fid, JsonElement? param, CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Glycosyl.Query query = new Glycosyl.Query {
            Gid = gid,
            Fid = fid,
            Payload = param
        };

        TaskCompletionSource<JsonElement?> tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration ctr = ct.Register(() => tcs.TrySetCanceled(ct));
        PendingRequest pending = new PendingRequest(tcs,ctr);

        try {
            _pending.TryAdd(query.Qid, pending);
            await _connexon.SendAsync(query, ct);
            return await tcs.Task;
        }
        finally {
            _pending.TryRemove(query.Qid, out _);
            pending.Dispose();
        }
    }

    void OnReceived(Glycosyl gly) {
        if (_disposed) return;
        if (gly is not Glycosyl.Reply reply) return;
        if (!_pending.TryRemove(reply.Qid, out PendingRequest? pending)) return;
        pending.Tcs.TrySetResult(reply.Payload);
        pending.Dispose();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _connexon.OnGlycosylReceived -= OnReceived;
        foreach (KeyValuePair<Guid, PendingRequest> kvp in _pending) {
            kvp.Value.Tcs.TrySetCanceled();
            kvp.Value.Dispose();
        }
        _pending.Clear();
    }
}
