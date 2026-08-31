using System.Collections.Concurrent;
using System.Text.Json;
using Glycoprotein.Connexon;

namespace Glycoprotein.Glycosylation;

public sealed class BeaconTracker(IConnexon connexon,TimeSpan? expiry = null,TimeSpan? cleanupInterval = null) : IDisposable {
    public event Action<Glycosyl.Beacon>? OnDiscovered;

    public event Action<Glycosyl.Beacon>? OnExpired;

    /// <summary>
    /// 节点 beacon 内容变化时触发 (含本机 Presenter 变化)。
    /// </summary>
    public event Action<Glycosyl.Beacon>? OnChanged;

    /// <summary>
    /// 是否将本机 Presenter 的首次出现信号 loopback 到 OnDiscovered 事件 (内容变化始终通过 OnChanged 通知)。
    /// </summary>
    public bool LoopbackPresenter { get; set; } = true;

    /// <summary>
    /// 本机节点 Id, 用于在收到自环 beacon 时识别 Presenter 信号。
    /// </summary>
    public string? PresenterId { get; set; }

    readonly TimeSpan _expiry = expiry ?? TimeSpan.FromSeconds(3);
    readonly TimeSpan _cleanupInterval = cleanupInterval ?? TimeSpan.FromSeconds(1);

    readonly ConcurrentDictionary<string,(Glycosyl.Beacon Glyco,DateTime LastSeen,string Signature)> _presenters = [];

    Task? _cleanupTask;
    bool _disposed;

    public IReadOnlyList<Glycosyl.Beacon> ActivePresenters { get => _presenters.Values.Select(v => v.Glyco).ToArray(); }

    public void Start() {
        ObjectDisposedException.ThrowIf(_disposed,this);
        connexon.OnGlycosylReceived += OnReceived;
        _cleanupTask = CleanupLoopAsync(connexon.CancellationToken);
    }

    public void Stop() {
        connexon.OnGlycosylReceived -= OnReceived;
        _presenters.Clear();
    }

    /// <summary>
    /// 接收本机 Presenter 的 beacon 变化信号: 更新存活记录, 触发 OnChanged, 并在 LoopbackPresenter 启用时触发 OnDiscovered。
    /// </summary>
    public void NotifyPresenterBeacon(Glycosyl.Beacon beacon) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        _presenters[beacon.Id] = (beacon,DateTime.UtcNow,BeaconPresenter.BuildSignature(beacon));
        if (!LoopbackPresenter) return;
        SafeInvoke(OnChanged,beacon);
        //SafeInvoke(OnDiscovered,beacon);
    }

    void OnReceived(Glycosyl glycosyl) {
        try {
            switch (glycosyl) {
                case Glycosyl.Heartbeat heartbeat:
                    OnHeartbeatReceived(heartbeat);
                    return;
                case Glycosyl.Beacon beacon:
                    OnBeaconReceived(beacon);
                    return;
            }
        } catch (JsonException ex) {
            Console.WriteLine($"JSON 解析失败: {ex.Message}");
        } catch (Exception ex) {
            Console.WriteLine($"接收异常: {ex.Message}");
        }
    }

    void OnHeartbeatReceived(Glycosyl.Heartbeat heartbeat) {
        if (!_presenters.TryGetValue(heartbeat.Id,out (Glycosyl.Beacon Glyco,DateTime LastSeen,string Signature) entry)) return;
        _presenters[heartbeat.Id] = (entry.Glyco,DateTime.UtcNow,entry.Signature);
    }

    void OnBeaconReceived(Glycosyl.Beacon beacon) {
        DateTime now = DateTime.UtcNow;
        bool isNew = false;
        bool isChanged = false;

        if (beacon.Id == PresenterId && !LoopbackPresenter) {
            _presenters[beacon.Id] = (beacon,now,BeaconPresenter.BuildSignature(beacon));
            return;
        }

        _presenters.AddOrUpdate(
                                beacon.Id,
                                addValueFactory: _ => {
                                    isNew = true;
                                    return (beacon,now,BeaconPresenter.BuildSignature(beacon));
                                },
                                updateValueFactory: (_,old) => {
                                    string signature = BeaconPresenter.BuildSignature(beacon);
                                    isChanged = signature != old.Signature;
                                    return (beacon,now,signature);
                                });

        if (isNew) {
            SafeInvoke(OnDiscovered,beacon);
        } else if (isChanged) {
            SafeInvoke(OnChanged,beacon);
        }
    }

    async Task CleanupLoopAsync(CancellationToken ct) {
        using PeriodicTimer timer = new PeriodicTimer(_cleanupInterval);

        try {
            while (await timer.WaitForNextTickAsync(ct)) {
                DateTime cutoff = DateTime.UtcNow - _expiry;

                foreach (KeyValuePair<string,(Glycosyl.Beacon Glyco,DateTime LastSeen,string Signature)> kvp in _presenters) {
                    if (kvp.Value.LastSeen >= cutoff) continue;

                    if (_presenters.TryRemove(kvp.Key,out (Glycosyl.Beacon Glyco,DateTime LastSeen,string Signature) removed)) {
                        SafeInvoke(OnExpired,removed.Glyco);
                    }
                }
            }
        } catch (OperationCanceledException) { }
    }

    void SafeInvoke(Action<Glycosyl.Beacon>? action,Glycosyl.Beacon glycosyl) {
        try {
            action?.Invoke(glycosyl);
        } catch (Exception ex) {
            Console.WriteLine($"事件处理程序内部抛出异常: {ex.Message}");
        }
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}