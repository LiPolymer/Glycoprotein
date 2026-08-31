using System.Text.Json;
using Glycoprotein.Connexon;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Tests;

public sealed class SceneContext : IDisposable {
    readonly List<GlycoComplex> _nodes = [];

    public GlycoComplex CreateNode(string id, IConnexon connexon) {
        var node = new GlycoComplex(id, connexon);
        _nodes.Add(node);
        return node;
    }

    public async Task StartAllAsync() {
        var tasks = _nodes.Select(n => n.StartAsync()).ToArray();
        await Task.WhenAll(tasks);
    }

    public async Task WaitForDiscoveryAsync(
        string observerId, string targetId,
        TimeSpan? timeout = null) {
        var observer = _nodes.First(n => n.Id == observerId);

        if (observer.Presenters.Any(p => p.Id == targetId))
            return;

        var tcs = new TaskCompletionSource<bool>();
        Action<Glycosyl.Beacon>? handler = null;
        handler = b => {
            if (b.Id == targetId) {
                tcs.TrySetResult(true);
                observer.OnDiscovered -= handler!;
            }
        };
        observer.OnDiscovered += handler;

        if (observer.Presenters.Any(p => p.Id == targetId)) {
            observer.OnDiscovered -= handler!;
            return;
        }

        await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(60));
    }

    public async Task<T> CaptureEventAsync<T>(
        string listenerId, string sourceId, string eventId,
        TimeSpan? timeout = null) {
        var listener = _nodes.First(n => n.Id == listenerId);
        var tcs = new TaskCompletionSource<T>();
        listener.OnEvent<T>(sourceId, eventId, msg => tcs.TrySetResult(msg));
        return await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(10));
    }

    public async Task<JsonElement?> CallAsync(
        string callerId, string targetId, string fid,
        JsonElement? param = null,
        CancellationToken ct = default) {
        var caller = _nodes.First(n => n.Id == callerId);
        return await caller.CallFunctionRawAsync(targetId, fid, param, ct);
    }

    public async Task DispatchAsync(
        string callerId, string targetId, string fid,
        CancellationToken ct = default) {
        var caller = _nodes.First(n => n.Id == callerId);
        await caller.DoActionAsync(targetId, fid, ct);
    }

    public async Task EmitAsync<T>(
        string emitterId, string fid, T arg,
        CancellationToken ct = default) {
        var emitter = _nodes.First(n => n.Id == emitterId);
        await emitter.EmitEventAsync(fid, arg, ct);
    }

    public async Task EmitAsync(
        string emitterId, string fid,
        CancellationToken ct = default) {
        var emitter = _nodes.First(n => n.Id == emitterId);
        await emitter.EmitEventAsync(fid, ct);
    }

    public void Dispose() {
        foreach (var n in _nodes) n.Dispose();
        _nodes.Clear();
    }

    public static string UniqueMeshDir() => UniqueSocketDir("glycoprotein");
    public static string UniqueMasteredDir() => UniqueSocketDir("glycoprotein_mastered");

    static string UniqueSocketDir(string basename) =>
        Path.Combine(Path.GetTempPath(), $"{basename}_{Guid.NewGuid():N}");
}
