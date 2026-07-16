using System.Text.Json;
using Glycoprotein.Debug.Framework;
using Glycoprotein.Debug.Model;
using Glycoprotein.Connexon;

namespace Glycoprotein.Debug.Scenarios;

public sealed class HubFailover : ScenarioBase {
    public override string Name => "Hub Failover";
    public override string Description => "Hub election and failover: kill the hub, verify survivors re-elect and continue communicating";

    public override async Task RunAsync(SceneContext ctx) {
        var dir = ScenarioRunner.UniqueMasteredDir();
        var hA = ctx.CreateNode("hub_a",
            new UnixDomainMasteredConnexon("hub_a", dir));
        hA.AddFunction<AddRequest, AddResponse>("add",
            req => new AddResponse(req.A + req.B));
        hA.AddFunction<GreetRequest, GreetResponse>("echo",
            req => new GreetResponse($"HubA echoes: {req.Name}"));

        await hA.StartAsync();
        Console.WriteLine("[HubA] started (should be the hub)");

        var hB = ctx.CreateNode("hub_b",
            new UnixDomainMasteredConnexon("hub_b", dir));
        var hC = ctx.CreateNode("hub_c",
            new UnixDomainMasteredConnexon("hub_c", dir));

        hB.AddFunction<AddRequest, AddResponse>("mul",
            req => new AddResponse(req.A * req.B));
        hB.AddAction("ping", () =>
            Console.WriteLine("  [HubB] ping!"));

        hC.AddFunction<GreetRequest, GreetResponse>("welcome",
            req => new GreetResponse($"HubC welcomes {req.Name}"));

        await Task.WhenAll(hB.StartAsync(), hC.StartAsync());
        Console.WriteLine("[HubB] ready  [HubC] ready");

        await ctx.RunStepAsync("All 3 nodes discover each other via hub", async () => {
            await Task.WhenAll(
                ctx.WaitForDiscoveryAsync("hub_b", "hub_c", TimeSpan.FromSeconds(30)),
                ctx.WaitForDiscoveryAsync("hub_c", "hub_b", TimeSpan.FromSeconds(30)),
                ctx.WaitForDiscoveryAsync("hub_c", "hub_a", TimeSpan.FromSeconds(30)));
            Console.WriteLine("  All 3 nodes discovered.");
        });

        await ctx.WaitInteractiveAsync();

        await ctx.RunStepAsync("Pre-kill: C -> A add 4+6=10", async () => {
            var res = await ctx.CallAsync("hub_c", "hub_a", "add",
                JsonSerializer.SerializeToElement(new AddRequest(4, 6)));
            var val = res?.GetProperty("Result").GetInt32();
            ctx.Assert(val == 10, $"Expected 10, got {val}");
        });

        await ctx.RunStepAsync("Pre-kill: B -> C welcome", async () => {
            var res = await ctx.CallAsync("hub_b", "hub_c", "welcome",
                JsonSerializer.SerializeToElement(new GreetRequest("HubB")));
            ctx.Assert(res.HasValue, "Expected non-null welcome response");
        });

        await ctx.WaitInteractiveAsync();

        Console.WriteLine("=== Killing HubA (the hub) ===");
        hA.Dispose();
        Console.WriteLine("[HubA] disposed — hub is down!");

        Console.WriteLine("=== Waiting for re-election and reconnection ===");
        await Task.Delay(4000);
        Console.WriteLine("Reconnection should be complete.");

        await ctx.RunStepAsync("Survivors re-discover each other", async () => {
            await Task.WhenAll(
                ctx.WaitForDiscoveryAsync("hub_b", "hub_c", TimeSpan.FromSeconds(30)),
                ctx.WaitForDiscoveryAsync("hub_c", "hub_b", TimeSpan.FromSeconds(30)));
            Console.WriteLine("  Survivors see each other.");
        });

        await ctx.WaitInteractiveAsync();

        await ctx.RunStepAsync("Post-kill: C -> B mul 6*7=42", async () => {
            var res = await ctx.CallAsync("hub_c", "hub_b", "mul",
                JsonSerializer.SerializeToElement(new AddRequest(6, 7)));
            var val = res?.GetProperty("Result").GetInt32();
            ctx.Assert(val == 42, $"Expected 42, got {val}");
        });

        await ctx.RunStepAsync("Post-kill: C -> B ping (action)", async () => {
            await ctx.DispatchAsync("hub_c", "hub_b", "ping");
            await Task.Delay(300);
        });

        await ctx.RunStepAsync("Post-kill: B -> C welcome again", async () => {
            var res = await ctx.CallAsync("hub_b", "hub_c", "welcome",
                JsonSerializer.SerializeToElement(new GreetRequest("SurvivorB")));
            ctx.Assert(res.HasValue, "Expected non-null welcome response");
        });

        await ctx.RunStepAsync("Call to dead hub A must fail", async () => {
            var hCNode = hC;
            try {
                await hCNode.CallAsync("hub_a", "add",
                    JsonSerializer.SerializeToElement(new AddRequest(1, 1)),
                    new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
                ctx.Fail("Call to dead node should have thrown");
            }
            catch (Exception ex) {
                Console.WriteLine($"  Expected failure: {ex.GetType().Name}");
            }
        });
    }
}
