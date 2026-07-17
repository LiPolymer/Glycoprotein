using System.Text.Json;
using Glycoprotein.Conductors;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;

namespace Glycoprotein;

public sealed class GlycoComplex : IDisposable {
    readonly ResponseConductor _responseConductor;
    readonly EventEmitter _eventEmitter;
    readonly BeaconTracker _tracker;
    readonly BeaconPresenter _beaconPresenter;
    readonly QueryConductor _queryConductor;
    readonly EventReceiver _eventReceiver;
    bool _started;
    bool _disposed;

    public event Action<Glycosyl.Beacon>? OnDiscovered {
        add => _tracker.OnDiscovered += value;
        remove => _tracker.OnDiscovered -= value;
    }

    public event Action<Glycosyl.Beacon>? OnExpired {
        add => _tracker.OnExpired += value;
        remove => _tracker.OnExpired -= value;
    }

    public string Id { get; }
    public IConnexon Connexon { get; }
    public IReadOnlyList<Glycosyl.Beacon> Presenters { get => _tracker.ActivePresenters; }

    public GlycoComplex(string id, IConnexon? connexon = null) {
        Id = id;
        Connexon = connexon ?? new UnixDomainMeshConnexon(Id);
        _responseConductor = new ResponseConductor(Connexon, id);
        _eventEmitter = new EventEmitter(Connexon, id);
        _beaconPresenter = new BeaconPresenter(Connexon);
        _tracker = new BeaconTracker(Connexon);
        _queryConductor = new QueryConductor(Connexon, () => _tracker.ActivePresenters);
        _eventReceiver = new EventReceiver(Connexon);
    }

    public GlycoComplex AddAction(string fid, Action action) {
        _responseConductor.AddAction(new Field.Method { Id = fid }, action);
        return this;
    }

    public GlycoComplex AddAction(Field.Method field, Action action) {
        _responseConductor.AddAction(field, action);
        return this;
    }

    public GlycoComplex AddFunction<TReq, TRes>(string fid, Func<TReq, TRes> func) {
        _responseConductor.AddFunction(new Field.Method { Id = fid }, func);
        return this;
    }

    public GlycoComplex AddFunction<TReq, TRes>(Field.Method field, Func<TReq, TRes> func) {
        _responseConductor.AddFunction(field, func);
        return this;
    }

    public GlycoComplex AddFunction(string fid, Func<JsonElement?, JsonElement?> func) {
        _responseConductor.AddRawFunction(new Field.Method { Id = fid }, func);
        return this;
    }

    public GlycoComplex AddFunction(Field.Method field, Func<JsonElement?, JsonElement?> func) {
        _responseConductor.AddRawFunction(field, func);
        return this;
    }

    public GlycoComplex AddGet<T>(string fid, Func<T> query) {
        _responseConductor.AddGet(new Field.Method { Id = fid }, query);
        return this;
    }

    public GlycoComplex AddGet<T>(Field.Method field, Func<T> query) {
        _responseConductor.AddGet(field, query);
        return this;
    }

    public GlycoComplex AddSet<T>(string fid, Action<T> reactor) {
        _responseConductor.AddSet(new Field.Method { Id = fid }, reactor);
        return this;
    }

    public GlycoComplex AddSet<T>(Field.Method field, Action<T> reactor) {
        _responseConductor.AddSet(field, reactor);
        return this;
    }

    public GlycoComplex AddEvent(string fid) {
        _eventEmitter.AddEvent(new Field.Event { Id = fid });
        return this;
    }

    public GlycoComplex AddEvent(Field.Event field) {
        _eventEmitter.AddEvent(field);
        return this;
    }

    public GlycoComplex AddEvent<T>(string fid) {
        _eventEmitter.AddEvent<T>(new Field.Event { Id = fid });
        return this;
    }

    public GlycoComplex AddEvent<T>(Field.Event field) {
        _eventEmitter.AddEvent<T>(field);
        return this;
    }

    public GlycoComplex OnEvent(string gid, string fid, Action handler) {
        _eventReceiver.AddEvent(gid, fid, handler);
        return this;
    }

    public GlycoComplex OnEvent<T>(string gid, string fid, Action<T> handler) {
        _eventReceiver.AddEvent(gid, fid, handler);
        return this;
    }

    public GlycoComplex OnEventRaw(string gid, string fid, Action<JsonElement?> handler) {
        _eventReceiver.AddEvent(gid, fid, handler);
        return this;
    }

    public Task DoActionAsync(string gid, string fid, CancellationToken ct = default)
        => _queryConductor.DoActionAsync(gid, fid, ct);

    public Task<JsonElement?> CallFunctionRawAsync(string gid, string fid, JsonElement? param = null, CancellationToken ct = default)
        => _queryConductor.CallFunctionRawAsync(gid, fid, param, ct);

    
    
    public Task EmitEventAsync(string fid)
        => _eventEmitter.EmitEventAsync(fid);

    public Task EmitEventAsync<T>(string fid, T arg)
        => _eventEmitter.EmitEventAsync(fid, arg);

    public Task EmitEventRawAsync(string fid, JsonElement? arg)
        => _eventEmitter.EmitEventRawAsync(fid, arg);

    public void Start() {
        _ = StartAsync();
    }

    public async Task StartAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;

        Connexon.Start();
        _tracker.Start();

        if (!BuildAndPublishBeacon()) return;
        _ = _beaconPresenter.StartAsync(Connexon.CancellationToken);
    }

    public GlycoComplex RefreshBeacon() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BuildAndPublishBeacon();
        return this;
    }

    bool BuildAndPublishBeacon() {
        List<Field> fields = [];
        fields.AddRange(_responseConductor.Fields);
        fields.AddRange(_eventEmitter.Fields);

        if (fields.Count == 0) return false;
        Glycosyl.Beacon beacon = new Glycosyl.Beacon {
            Id = Id,
            Fields = fields
        };
        _beaconPresenter.Publish(beacon);
        _ = Connexon.SendAsync(beacon, Connexon.CancellationToken);
        return true;
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _tracker.Dispose();
        _queryConductor.Dispose();
        _responseConductor.Dispose();
        _eventReceiver.Dispose();
        Connexon.Dispose();
    }
}
