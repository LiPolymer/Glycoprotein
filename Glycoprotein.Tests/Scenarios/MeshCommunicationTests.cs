using System.Text.Json;
using Glycoprotein.Connexon;
using Xunit;
using Xunit.Abstractions;

namespace Glycoprotein.Tests.Scenarios;

public sealed class MeshCommunicationTests : ScenarioTestBase {
    public MeshCommunicationTests(ITestOutputHelper output) : base(output) { }

    [Fact(DisplayName = "Mesh Communication: UDS mesh - RPC, action dispatch, and event pub/sub")]
    public async Task MeshCommunication_Scenario() {
        using var ctx = new SceneContext();
        var dir = SceneContext.UniqueMeshDir();
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
                Output.WriteLine("  [Alpha] Action 'do_status' fired"))
            .AddEvent<HeartbeatMessage>("evt_heartbeat");

        beta
            .AddFunction<MultiplyRequest, MultiplyResponse>("math_mul",
                req => new MultiplyResponse(req.A * req.B))
            .AddAction("do_shutdown", () =>
                Output.WriteLine("  [Beta] Action 'do_shutdown' fired"))
            .AddEvent<AlarmMessage>("evt_alarm");

        await ctx.StartAllAsync();

        await StepAsync("Mutual discovery", async () => {
            await Task.WhenAll(
                ctx.WaitForDiscoveryAsync("alpha", "beta", TimeSpan.FromSeconds(30)),
                ctx.WaitForDiscoveryAsync("beta", "alpha", TimeSpan.FromSeconds(30)));
        });

        await StepAsync("Beta -> Alpha math_add (10+25=35)", async () => {
            var res = await ctx.CallAsync("beta", "alpha", "math_add",
                JsonSerializer.SerializeToElement(new AddRequest(10, 25)));
            Assert.Equal(35, res?.GetProperty("Result").GetInt32());
        });

        await StepAsync("Alpha -> Beta math_mul (7*8=56)", async () => {
            var res = await ctx.CallAsync("alpha", "beta", "math_mul",
                JsonSerializer.SerializeToElement(new MultiplyRequest(7, 8)));
            Assert.Equal(56, res?.GetProperty("Result").GetInt32());
        });

        await StepAsync("Beta -> Alpha greet", async () => {
            var res = await ctx.CallAsync("beta", "alpha", "greet",
                JsonSerializer.SerializeToElement(new GreetRequest("World")));
            Assert.True(res.HasValue, "Expected non-null greet response");
        });

        await StepAsync("Beta -> Alpha do_status (action)", async () => {
            await ctx.DispatchAsync("beta", "alpha", "do_status");
            await Task.Delay(300);
        });

        await StepAsync("Alpha -> Beta do_shutdown (action)", async () => {
            await ctx.DispatchAsync("alpha", "beta", "do_shutdown");
            await Task.Delay(300);
        });

        await StepAsync("Alpha emits heartbeat -> Beta receives", async () => {
            var capture = ctx.CaptureEventAsync<HeartbeatMessage>("beta", "alpha", "evt_heartbeat", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("alpha", "evt_heartbeat", new HeartbeatMessage("alpha", DateTime.Now));
            var msg = await capture;
            Assert.NotNull(msg);
            Output.WriteLine($"  [Beta] <- heartbeat from {msg.NodeId}");
        });

        await StepAsync("Beta emits alarm -> Alpha receives", async () => {
            var capture = ctx.CaptureEventAsync<AlarmMessage>("alpha", "beta", "evt_alarm", TimeSpan.FromSeconds(5));
            await ctx.EmitAsync("beta", "evt_alarm", new AlarmMessage("CRITICAL", "Storage full", DateTime.Now));
            var msg = await capture;
            Assert.NotNull(msg);
            Output.WriteLine($"  [Alpha] <- alarm [{msg.Level}]: {msg.Description}");
        });

        Output.WriteLine("  ✔ Scenario finished successfully");
    }
}
