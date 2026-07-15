using System.Text.Json;
using Glycoprotein.Debug.Framework;
using Glycoprotein.Debug.Model;
using Glycoprotein.Connexon;

namespace Glycoprotein.Debug.Scenarios;

public sealed class MeshCommunication : ScenarioBase {
    public override string Name => "Mesh Communication";
    public override string Description => "Unix Domain Socket mesh: RPC, action dispatch, and event pub/sub between two nodes";

    public override async Task RunAsync(SceneContext ctx) {
        var dir = ScenarioRunner.UniqueMeshDir();
        var alpha = ctx.CreateNode("alpha",
            new UnixDomainMeshConnexon("alpha", dir));
        var beta = ctx.CreateNode("beta",
            new UnixDomainMeshConnexon("beta", dir));

        alpha
            .AddFunction<AddRequest, AddResponse>("math_add",
                req => new AddResponse(req.A + req.B))
            .AddFunction<GreetRequest, GreetResponse>("greet",
                req => new GreetResponse($"Hello, {req.Name}! from Alpha"))
            .AddAction("do_status", () =>
                Console.WriteLine("  [Alpha] Action 'do_status' fired"))
            .AddEvent<HeartbeatMessage>("evt_heartbeat");

        beta
            .AddFunction<MultiplyRequest, MultiplyResponse>("math_mul",
                req => new MultiplyResponse(req.A * req.B))
            .AddAction("do_shutdown", () =>
                Console.WriteLine("  [Beta] Action 'do_shutdown' fired"))
            .AddEvent<AlarmMessage>("evt_alarm");

        await ctx.StartAllAsync();

        await ctx.RunStepAsync("Mutual discovery", async () => {
            await Task.WhenAll(
                ctx.WaitForDiscoveryAsync("alpha", "beta", TimeSpan.FromSeconds(30)),
                ctx.WaitForDiscoveryAsync("beta", "alpha", TimeSpan.FromSeconds(30)));
            Console.WriteLine("  Both nodes see each other.");
        });

        await ctx.WaitInteractiveAsync();

        await ctx.RunStepAsync("Beta -> Alpha math_add (10+25=35)", async () => {
            var res = await ctx.CallAsync("beta", "alpha", "math_add",
                JsonSerializer.SerializeToElement(new AddRequest(10, 25)));
            var val = res?.GetProperty("Result").GetInt32();
            ctx.Assert(val == 35, $"Expected 35, got {val}");
        });

        await ctx.RunStepAsync("Alpha -> Beta math_mul (7*8=56)", async () => {
            var res = await ctx.CallAsync("alpha", "beta", "math_mul",
                JsonSerializer.SerializeToElement(new MultiplyRequest(7, 8)));
            var val = res?.GetProperty("Result").GetInt32();
            ctx.Assert(val == 56, $"Expected 56, got {val}");
        });

        await ctx.RunStepAsync("Beta -> Alpha greet", async () => {
            var res = await ctx.CallAsync("beta", "alpha", "greet",
                JsonSerializer.SerializeToElement(new GreetRequest("World")));
            ctx.Assert(res.HasValue, "Expected non-null greet response");
        });

        await ctx.RunStepAsync("Beta -> Alpha do_status (action)", async () => {
            await ctx.DispatchAsync("beta", "alpha", "do_status");
            await Task.Delay(300);
        });

        await ctx.RunStepAsync("Alpha -> Beta do_shutdown (action)", async () => {
            await ctx.DispatchAsync("alpha", "beta", "do_shutdown");
            await Task.Delay(300);
        });

        await ctx.RunStepAsync("Alpha emits heartbeat -> Beta receives", async () => {
            var captureTask = ctx.CaptureEventAsync<HeartbeatMessage>("beta", "alpha", "evt_heartbeat", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("alpha", "evt_heartbeat", new HeartbeatMessage("alpha", DateTime.Now));
            var msg = await captureTask;
            ctx.Assert(msg != null, "Expected heartbeat message");
            Console.WriteLine($"  [Beta] <- heartbeat from {msg?.NodeId}");
        });

        await ctx.RunStepAsync("Beta emits alarm -> Alpha receives", async () => {
            var captureTask = ctx.CaptureEventAsync<AlarmMessage>("alpha", "beta", "evt_alarm", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("beta", "evt_alarm", new AlarmMessage("CRITICAL", "Storage full", DateTime.Now));
            var msg = await captureTask;
            ctx.Assert(msg != null, "Expected alarm message");
            Console.WriteLine($"  [Alpha] <- alarm [{msg?.Level}]: {msg?.Description}");
        });
    }
}
