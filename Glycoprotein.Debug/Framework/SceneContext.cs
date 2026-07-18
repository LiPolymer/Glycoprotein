using System.Text.Json;
using Glycoprotein.Connexon;
using Glycoprotein.Debug.Model;
using Glycoprotein.Glycosylation;

namespace Glycoprotein.Debug.Framework;

public sealed class SceneContext : IDisposable {
    readonly List<GlycoComplex> _nodes = [];
    readonly RunConfig _config;
    readonly List<StepResult> _steps = [];

    public IReadOnlyList<StepResult> Steps => _steps;
    public bool Interactive => _config.Interactive;

    public SceneContext(RunConfig config) {
        _config = config;
    }

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

        await tcs.Task.WaitAsync(timeout ?? _config.Timeout);
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

    public void Assert(bool condition, string message) {
        if (!condition)
            throw new AssertionException(message);
    }

    public void Fail(string message) {
        throw new AssertionException(message);
    }

    public void RecordStep(string label, bool passed, string? error = null, TimeSpan? duration = null) {
        _steps.Add(new StepResult {
            Label = label,
            Passed = passed,
            Error = error,
            Duration = duration ?? TimeSpan.Zero
        });
    }

    public async Task RunStepAsync(string label, Func<Task> step) {
        var start = DateTime.UtcNow;
        try {
            await step();
            RecordStep(label, true, duration: DateTime.UtcNow - start);
        }
        catch (AssertionException ex) {
            RecordStep(label, false, ex.Message, DateTime.UtcNow - start);
        }
        catch (Exception ex) {
            RecordStep(label, false, $"{ex.GetType().Name}: {ex.Message}", DateTime.UtcNow - start);
        }
    }

    public async Task WaitInteractiveAsync() {
        if (!_config.Interactive) return;
        Console.Write("  [Press any key to continue]");
        await Task.Run(() => Console.ReadKey(true));
        Console.WriteLine();
    }

    public void Dispose() {
        foreach (var n in _nodes) n.Dispose();
        _nodes.Clear();
    }
}

public sealed class AssertionException(string message) : Exception(message);
