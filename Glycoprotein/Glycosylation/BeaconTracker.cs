using System.Collections.Concurrent;
using System.Text.Json;
using Glycoprotein.Connexon;

namespace Glycoprotein.Glycosylation;

public sealed class BeaconTracker(IConnexon connexon,TimeSpan? expiry = null,TimeSpan? cleanupInterval = null) : IDisposable {
    public event Action<Glycosyl.Beacon>? OnDiscovered;

    public event Action<Glycosyl.Beacon>? OnExpired;

    /// <summary>
    /// 是否将本机 Presenter 的 beacon 信号 loopback 到 OnDiscovered 事件 (含本机首次出现与内容变化)。
    /// </summary>
    public bool LoopbackPresenter { get; set; } = true;

    /// <summary>
    /// 本机节点 Id, 用于在收到自环 beacon 时识别 Presenter 信号。
    /// </summary>
    public string? PresenterId { get; set; }

    readonly TimeSpan _expiry = expiry ?? TimeSpan.FromSeconds(3);
    readonly TimeSpan _cleanupInterval = cleanupInterval ?? TimeSpan.FromSeconds(1);

    readonly ConcurrentDictionary<string,(Glycosyl.Beacon Glyco,DateTime LastSeen)> _presenters = [];

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
    /// 接收本机 Presenter 的 beacon 变化信号: 更新存活记录, 并在 LoopbackPresenter 启用时触发 OnDiscovered。
    /// </summary>
    public void NotifyPresenterBeacon(Glycosyl.Beacon beacon) {
        ObjectDisposedException.ThrowIf(_disposed,this);
        _presenters[beacon.Id] = (beacon,DateTime.UtcNow);
        if (!LoopbackPresenter) return;
        SafeInvoke(OnDiscovered,beacon);
    }

    void OnReceived(Glycosyl glycosyl) {
        try {
            if (glycosyl is not Glycosyl.Beacon beacon) return;

            DateTime now = DateTime.UtcNow;
            bool isNew = false;

            if (beacon.Id == PresenterId && !LoopbackPresenter) {
                _presenters[beacon.Id] = (beacon,now);
                return;
            }

            _presenters.AddOrUpdate(
                                    beacon.Id,
                                    addValueFactory: _ => {
                                        isNew = true;
                                        return (beacon,now);
                                    },
                                    updateValueFactory: (_,_) => (beacon,now)
                                   );

            if (isNew) {
                SafeInvoke(OnDiscovered,beacon);
            }
        } catch (JsonException ex) {
            Console.WriteLine($"JSON 解析失败: {ex.Message}");
        } catch (Exception ex) {
            Console.WriteLine($"接收异常: {ex.Message}");
        }
    }

    async Task CleanupLoopAsync(CancellationToken ct) {
        using PeriodicTimer timer = new PeriodicTimer(_cleanupInterval);

        try {
            while (await timer.WaitForNextTickAsync(ct)) {
                DateTime cutoff = DateTime.UtcNow - _expiry;

                foreach (KeyValuePair<string,(Glycosyl.Beacon Glyco,DateTime LastSeen)> kvp in _presenters) {
                    if (kvp.Value.LastSeen >= cutoff) continue;

                    if (_presenters.TryRemove(kvp.Key,out (Glycosyl.Beacon Glyco,DateTime LastSeen) removed)) {
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