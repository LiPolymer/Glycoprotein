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
        ValidateField(gid, fid, typeof(Field.Action));
        await SendQueryAndWaitAsync(gid, fid, null, ct);
    }

    public async Task<JsonElement?> DoFunctionAsync(string gid, string fid, JsonElement? param = null, CancellationToken ct = default) {
        ValidateField(gid, fid, typeof(Field.Function), param);
        return await SendQueryAndWaitAsync(gid, fid, param, ct);
    }

    void ValidateField(string gid, string fid, Type expectedType, JsonElement? param = null) {
        IReadOnlyList<Glycosyl.Beacon>? beacons = _beaconProvider();
        Field? field = beacons?.FirstOrDefault(b => b.Id == gid)?.Fields.FirstOrDefault(f => f.Id == fid);
        if (field == null) throw new InvalidOperationException($"Field '{fid}' on '{gid}' is not exist.");
        if (field?.GetType() != expectedType)
            throw new InvalidOperationException($"Field '{fid}' on '{gid}' is not a {expectedType.Name}.");
        if (param == null || field is not Field.Function fn || fn.QuerySchema == null) return;
        EvaluationResults er = JsonSchema.Build(fn.QuerySchema.Value).Evaluate(param.Value);
        if (!er.IsValid)
            throw new InvalidOperationException(
                string.Join('\n', er.Errors?.Select(kvp => $"{kvp.Key} : {kvp.Value}") ?? ["???"]));
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
