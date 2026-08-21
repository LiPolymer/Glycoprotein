using System.Text.Json;
using Glycoprotein.Connexon;
using Xunit;
using Xunit.Abstractions;

namespace Glycoprotein.Tests.Scenarios;

public sealed class HubFailoverTests : ScenarioTestBase {
    public HubFailoverTests(ITestOutputHelper output) : base(output) { }

    // [Fact(DisplayName = "Hub Failover: kill the hub, survivors re-elect and keep communicating")]
    // public async Task HubFailover_Scenario() {
    //     using var ctx = new SceneContext();
    //     var dir = SceneContext.UniqueMasteredDir();
    //     var hA = ctx.CreateNode("hub_a",
    //         new UnixDomainMasteredConnexon("hub_a", dir));
    //     hA.AddFunction<AddRequest, AddResponse>("add",
    //         req => new AddResponse(req.A + req.B));
    //     hA.AddFunction<GreetRequest, GreetResponse>("echo",
    //         req => new GreetResponse($"HubA echoes: {req.Name}"));
    //
    //     await hA.StartAsync();
    //     Output.WriteLine("[HubA] started (should be the hub)");
    //
    //     var hB = ctx.CreateNode("hub_b",
    //         new UnixDomainMasteredConnexon("hub_b", dir));
    //     var hC = ctx.CreateNode("hub_c",
    //         new UnixDomainMasteredConnexon("hub_c", dir));
    //
    //     hB.AddFunction<AddRequest, AddResponse>("mul",
    //         req => new AddResponse(req.A * req.B));
    //     hB.AddAction("ping", () =>
    //         Output.WriteLine("  [HubB] ping!"));
    //
    //     hC.AddFunction<GreetRequest, GreetResponse>("welcome",
    //         req => new GreetResponse($"HubC welcomes {req.Name}"));
    //
    //     await Task.WhenAll(hB.StartAsync(), hC.StartAsync());
    //     Output.WriteLine("[HubB] ready  [HubC] ready");
    //
    //     await StepAsync("All 3 nodes discover each other via hub", async () => {
    //         await Task.WhenAll(
    //             ctx.WaitForDiscoveryAsync("hub_b", "hub_c", TimeSpan.FromSeconds(30)),
    //             ctx.WaitForDiscoveryAsync("hub_c", "hub_b", TimeSpan.FromSeconds(30)),
    //             ctx.WaitForDiscoveryAsync("hub_c", "hub_a", TimeSpan.FromSeconds(30)));
    //     });
    //
    //     await StepAsync("Pre-kill: C -> A add 4+6=10", async () => {
    //         var res = await ctx.CallAsync("hub_c", "hub_a", "add",
    //             JsonSerializer.SerializeToElement(new AddRequest(4, 6)));
    //         Assert.Equal(10, res?.GetProperty("Result").GetInt32());
    //     });
    //
    //     await StepAsync("Pre-kill: B -> C welcome", async () => {
    //         var res = await ctx.CallAsync("hub_b", "hub_c", "welcome",
    //             JsonSerializer.SerializeToElement(new GreetRequest("HubB")));
    //         Assert.True(res.HasValue, "Expected non-null welcome response");
    //     });
    //
    //     Output.WriteLine("=== Killing HubA (the hub) ===");
    //     hA.Dispose();
    //     Output.WriteLine("[HubA] disposed - hub is down!");
    //
    //     Output.WriteLine("=== Waiting for re-election and reconnection ===");
    //     await Task.Delay(4000);
    //     Output.WriteLine("Reconnection should be complete.");
    //
    //     await StepAsync("Survivors re-discover each other", async () => {
    //         await Task.WhenAll(
    //             ctx.WaitForDiscoveryAsync("hub_b", "hub_c", TimeSpan.FromSeconds(30)),
    //             ctx.WaitForDiscoveryAsync("hub_c", "hub_b", TimeSpan.FromSeconds(30)));
    //     });
    //
    //     await StepAsync("Post-kill: C -> B mul 6*7=42", async () => {
    //         var res = await ctx.CallAsync("hub_c", "hub_b", "mul",
    //             JsonSerializer.SerializeToElement(new AddRequest(6, 7)));
    //         Assert.Equal(42, res?.GetProperty("Result").GetInt32());
    //     });
    //
    //     await StepAsync("Post-kill: C -> B ping (action)", async () => {
    //         await ctx.DispatchAsync("hub_c", "hub_b", "ping");
    //         await Task.Delay(300);
    //     });
    //
    //     await StepAsync("Post-kill: B -> C welcome again", async () => {
    //         var res = await ctx.CallAsync("hub_b", "hub_c", "welcome",
    //             JsonSerializer.SerializeToElement(new GreetRequest("SurvivorB")));
    //         Assert.True(res.HasValue, "Expected non-null welcome response");
    //     });
    //
    //     await StepAsync("Call to dead hub A must fail", async () => {
    //         await Assert.ThrowsAnyAsync<Exception>(async () => {
    //             await hC.CallFunctionRawAsync("hub_a", "add",
    //                 JsonSerializer.SerializeToElement(new AddRequest(1, 1)),
    //                 new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
    //         });
    //     });
    //
    //     Output.WriteLine("  ✔ Scenario finished successfully");
    // }
}
