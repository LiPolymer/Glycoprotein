using Glycoprotein.Connexon;

namespace Glycoprotein.Glycosylation;

public sealed class BeaconPresenter(IConnexon connexon, TimeSpan? interval = null) {
    readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(1);

    public volatile Glycosyl.Beacon? BeaconPayload;
    
    public void Publish(Glycosyl.Beacon glycosyl) {
        BeaconPayload = glycosyl;
    }

    public Task StartAsync(Glycosyl.Beacon glycosyl, CancellationToken ct = default) {
        Publish(glycosyl);
        return PublishLoopAsync(ct);
    }
    public Task StartAsync(CancellationToken ct = default) {
        return PublishLoopAsync(ct);
    }
    
    async Task PublishLoopAsync(CancellationToken ct) {
        using PeriodicTimer timer = new PeriodicTimer(_interval);
        try {
            do {
                Glycosyl.Beacon? payload = BeaconPayload;
                if (payload == null) continue;
                await connexon.SendAsync(payload, ct);
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            Console.WriteLine($"发送失败: {ex.Message}");
        }
    }
}
