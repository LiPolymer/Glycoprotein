using Glycoprotein.Connexon;
using Microsoft.Extensions.Hosting;

namespace Glycoprotein.HostedService;

public class GlycoService(string id,IConnexon? connexon = null) : GlycoComplex(id,connexon),IHostedService {
    public Task StopAsync(CancellationToken cancellationToken) {
        Dispose();
        return Task.CompletedTask;
    }
}