using System.Text.Json;
using Glycoprotein.Conductors;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;

namespace Glycoprotein;

public sealed class GlycoComplex : IDisposable {
    readonly ResponseConductor _responseConductor;
    readonly EventEmitter _eventEmitter;
    readonly BeaconPresenter _beaconPresenter;
    readonly QueryConductor _queryConductor;
    readonly EventReceiver _eventReceiver;
    bool _started;
    bool _disposed;

    public event Action<Glycosyl.Beacon>? OnDiscovered {
        add => Tracker.OnDiscovered += value;
        remove => Tracker.OnDiscovered -= value;
    }

    public event Action<Glycosyl.Beacon>? OnExpired {
        add => Tracker.OnExpired += value;
        remove => Tracker.OnExpired -= value;
    }

    public string Id { get; }
    public IConnexon Connexon { get; }
    public BeaconTracker Tracker { get; }
    public IReadOnlyList<Glycosyl.Beacon> Presenters { get => Tracker.ActivePresenters; }

    public GlycoComplex(string id, IConnexon? connexon = null) {
        Id = id;
        Connexon = connexon ?? new UnixDomainMeshConnexon(Id);
        _responseConductor = new ResponseConductor(Connexon, id);
        _eventEmitter = new EventEmitter(Connexon, id);
        _beaconPresenter = new BeaconPresenter(Connexon);
        Tracker = new BeaconTracker(Connexon);
        _queryConductor = new QueryConductor(Connexon, () => Tracker.ActivePresenters);
        _eventReceiver = new EventReceiver(Connexon);
    }

    public GlycoComplex AddAction(string fid, Action action) {
        _responseConductor.AddAction(new Field.Action { Id = fid }, action);
        return this;
    }

    public GlycoComplex AddFunction<TReq, TRes>(string fid, Func<TReq, TRes> func) {
        _responseConductor.AddFunction(new Field.Function { Id = fid }, func);
        return this;
    }

    public GlycoComplex AddFunction(string fid, Func<JsonElement?, JsonElement?> func) {
        _responseConductor.AddBareFunction(new Field.Function { Id = fid }, func);
        return this;
    }

    public GlycoComplex AddEvent(string fid) {
        _eventEmitter.AddEvent(new Field.Event { Id = fid });
        return this;
    }

    public GlycoComplex AddEvent<T>(string fid) {
        _eventEmitter.AddEvent<T>(new Field.Event { Id = fid });
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

    public Task<JsonElement?> CallFunctionAsync(string gid, string fid, JsonElement? param = null, CancellationToken ct = default)
        => _queryConductor.CallFunctionAsync(gid, fid, param, ct);

    public Task EmitEventAsync(string fid)
        => _eventEmitter.EmitEventAsync(fid);

    public Task EmitEventAsync<T>(string fid, T arg)
        => _eventEmitter.EmitEventAsync(fid, arg);

    public Task EmitEventRawAsync(string fid, JsonElement? arg)
        => _eventEmitter.EmitBareEventAsync(fid, arg);

    public void Start() {
        _ = StartAsync();
    }

    public async Task StartAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;

        Connexon.Start();
        Tracker.Start();

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
        Tracker.Dispose();
        _queryConductor.Dispose();
        _responseConductor.Dispose();
        _eventReceiver.Dispose();
        Connexon.Dispose();
    }
}
