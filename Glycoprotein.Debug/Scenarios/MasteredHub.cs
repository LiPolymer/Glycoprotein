using System.Text.Json;
using Glycoprotein.Debug.Framework;
using Glycoprotein.Debug.Model;
using Glycoprotein.Connexon;

namespace Glycoprotein.Debug.Scenarios;

public sealed class MasteredHub : ScenarioBase {
    public override string Name => "Mastered Hub";
    public override string Description => "Unix Domain Socket hub-and-spoke with master election: RPC, action, and events";

    public override async Task RunAsync(SceneContext ctx) {
        var dir = ScenarioRunner.UniqueMasteredDir();
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
                Console.WriteLine("  [MasterB] Action 'do_ping' fired"))
            .AddEvent<AlarmMessage>("evt_alarm");

        await ctx.StartAllAsync();

        await ctx.RunStepAsync("Hub-based mutual discovery", async () => {
            await Task.WhenAll(
                ctx.WaitForDiscoveryAsync("master_a", "master_b", TimeSpan.FromSeconds(30)),
                ctx.WaitForDiscoveryAsync("master_b", "master_a", TimeSpan.FromSeconds(30)));
            Console.WriteLine("  Both nodes see each other via hub.");
        });

        await ctx.WaitInteractiveAsync();

        await ctx.RunStepAsync("MasterB -> MasterA math_add (15+30=45)", async () => {
            var res = await ctx.CallAsync("master_b", "master_a", "math_add",
                JsonSerializer.SerializeToElement(new AddRequest(15, 30)));
            var val = res?.GetProperty("Result").GetInt32();
            ctx.Assert(val == 45, $"Expected 45, got {val}");
        });

        await ctx.RunStepAsync("MasterB -> MasterA greet", async () => {
            var res = await ctx.CallAsync("master_b", "master_a", "greet",
                JsonSerializer.SerializeToElement(new GreetRequest("Kiloo")));
            ctx.Assert(res.HasValue, "Expected non-null greet response");
        });

        await ctx.RunStepAsync("MasterA -> MasterB do_ping (action)", async () => {
            await ctx.DispatchAsync("master_a", "master_b", "do_ping");
            await Task.Delay(300);
        });

        await ctx.RunStepAsync("MasterA emits heartbeat -> MasterB receives", async () => {
            var captureTask = ctx.CaptureEventAsync<HeartbeatMessage>("master_b", "master_a", "evt_heartbeat", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("master_a", "evt_heartbeat", new HeartbeatMessage("master_a", DateTime.Now));
            var msg = await captureTask;
            ctx.Assert(msg != null, "Expected heartbeat message");
            Console.WriteLine($"  [MasterB] <- heartbeat from {msg?.NodeId}");
        });

        await ctx.RunStepAsync("MasterB emits alarm -> MasterA receives", async () => {
            var captureTask = ctx.CaptureEventAsync<AlarmMessage>("master_a", "master_b", "evt_alarm", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("master_b", "evt_alarm", new AlarmMessage("WARN", "Disk space low", DateTime.Now));
            var msg = await captureTask;
            ctx.Assert(msg != null, "Expected alarm message");
            Console.WriteLine($"  [MasterA] <- alarm [{msg?.Level}]: {msg?.Description}");
        });
    }
}
