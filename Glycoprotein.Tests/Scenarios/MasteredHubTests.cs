using System.Text.Json;
using Glycoprotein.Connexon;
using Xunit;
using Xunit.Abstractions;

namespace Glycoprotein.Tests.Scenarios;

public sealed class MasteredHubTests : ScenarioTestBase {
    public MasteredHubTests(ITestOutputHelper output) : base(output) { }

    [Fact(DisplayName = "Mastered Hub: hub-and-spoke with master election - RPC, action, and events")]
    public async Task MasteredHub_Scenario() {
        using var ctx = new SceneContext();
        var dir = SceneContext.UniqueMasteredDir();
        var ma = ctx.CreateNode("master_a",
            new UnixDomainMasteredConnexon("master_a", dir));
        var mb = ctx.CreateNode("master_b",
            new UnixDomainMasteredConnexon("master_b", dir));

        ma
            .AddFunction<AddRequest, AddResponse>("math_add",
                req => new AddResponse(req.A + req.B))
            .AddFunction<GreetRequest, GreetResponse>("greet",
                req => new GreetResponse($"Hi, {req.Name}! from MasterA"))
            .AddEvent<HeartbeatMessage>("evt_heartbeat");

        mb
            .AddAction("do_ping", () =>
                Output.WriteLine("  [MasterB] Action 'do_ping' fired"))
            .AddEvent<AlarmMessage>("evt_alarm");

        await ctx.StartAllAsync();

        await StepAsync("Hub-based mutual discovery", async () => {
            await Task.WhenAll(
                ctx.WaitForDiscoveryAsync("master_a", "master_b", TimeSpan.FromSeconds(30)),
                ctx.WaitForDiscoveryAsync("master_b", "master_a", TimeSpan.FromSeconds(30)));
        });

        await StepAsync("MasterB -> MasterA math_add (15+30=45)", async () => {
            var res = await ctx.CallAsync("master_b", "master_a", "math_add",
                JsonSerializer.SerializeToElement(new AddRequest(15, 30)));
            Assert.Equal(45, res?.GetProperty("Result").GetInt32());
        });

        await StepAsync("MasterB -> MasterA greet", async () => {
            var res = await ctx.CallAsync("master_b", "master_a", "greet",
                JsonSerializer.SerializeToElement(new GreetRequest("Kiloo")));
            Assert.True(res.HasValue, "Expected non-null greet response");
        });

        await StepAsync("MasterA -> MasterB do_ping (action)", async () => {
            await ctx.DispatchAsync("master_a", "master_b", "do_ping");
            await Task.Delay(300);
        });

        await StepAsync("MasterA emits heartbeat -> MasterB receives", async () => {
            var capture = ctx.CaptureEventAsync<HeartbeatMessage>("master_b", "master_a", "evt_heartbeat", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("master_a", "evt_heartbeat", new HeartbeatMessage("master_a", DateTime.Now));
            var msg = await capture;
            Assert.NotNull(msg);
            Output.WriteLine($"  [MasterB] <- heartbeat from {msg.NodeId}");
        });

        await StepAsync("MasterB emits alarm -> MasterA receives", async () => {
            var capture = ctx.CaptureEventAsync<AlarmMessage>("master_a", "master_b", "evt_alarm", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("master_b", "evt_alarm", new AlarmMessage("WARN", "Disk space low", DateTime.Now));
            var msg = await capture;
            Assert.NotNull(msg);
            Output.WriteLine($"  [MasterA] <- alarm [{msg.Level}]: {msg.Description}");
        });

        Output.WriteLine("  ✔ Scenario finished successfully");
    }
}
