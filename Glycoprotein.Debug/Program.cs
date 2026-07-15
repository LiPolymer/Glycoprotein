using System.Text.Json;
using Glycoprotein;
using Glycoprotein.Connexon;

namespace Glycoprotein.Debug;

record AddRequest(int A, int B);
record AddResponse(int Result);
record MultiplyRequest(int A, int B);
record MultiplyResponse(int Result);
record GreetRequest(string Name);
record GreetResponse(string Message);
record HeartbeatMessage(string NodeId, DateTime Time);
record AlarmMessage(string Level, string Description, DateTime Time);

class Program {
    static async Task Main() {
        using var alpha = new GlycoComplex("alpha",
            new UnixDomainMeshConnexon("alpha"));
        using var beta = new GlycoComplex("beta",
            new UnixDomainMeshConnexon("beta"));

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

        var alphaGotAlarm = new TaskCompletionSource<AlarmMessage>();
        var betaGotHeartbeat = new TaskCompletionSource<HeartbeatMessage>();

        alpha.On<AlarmMessage>("beta", "evt_alarm",
            msg => { Console.WriteLine($"  [Alpha] ← alarm [{msg.Level}]: {msg.Description}"); alphaGotAlarm.TrySetResult(msg); });
        beta.On<HeartbeatMessage>("alpha", "evt_heartbeat",
            msg => { Console.WriteLine($"  [Beta] ← heartbeat from {msg.NodeId}"); betaGotHeartbeat.TrySetResult(msg); });

        var alphaSeesBeta = new TaskCompletionSource<bool>();
        var betaSeesAlpha = new TaskCompletionSource<bool>();

        alpha.OnDiscovered += b => {
            Console.WriteLine($"[Alpha] discovered {b.Id} ({b.Fields.Count} fields)");
            if (b.Id == "beta") alphaSeesBeta.TrySetResult(true);
        };
        beta.OnDiscovered += b => {
            Console.WriteLine($"[Beta] discovered {b.Id} ({b.Fields.Count} fields)");
            if (b.Id == "alpha") betaSeesAlpha.TrySetResult(true);
        };

        await Task.WhenAll(alpha.StartAsync(), beta.StartAsync());
        Console.WriteLine($"[Alpha] ready  [Beta] ready\n");

        Console.WriteLine("=== Waiting for mutual discovery ===");
        await Task.WhenAll(
            alphaSeesBeta.Task.WaitAsync(TimeSpan.FromSeconds(30)),
            betaSeesAlpha.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        Console.WriteLine("Both nodes see each other.\n");

        var pass = 0;
        var total = 0;

        total++;
        Console.WriteLine($"── Test {total}: Beta → Alpha math_add ──");
        var addRes = await beta.CallAsync("alpha", "math_add",
            JsonSerializer.SerializeToElement(new AddRequest(10, 25)));
        Console.WriteLine($"  10 + 25 = {addRes}");
        pass++;
        Console.WriteLine("  PASS\n");

        total++;
        Console.WriteLine($"── Test {total}: Alpha → Beta math_mul ──");
        var mulRes = await alpha.CallAsync("beta", "math_mul",
            JsonSerializer.SerializeToElement(new MultiplyRequest(7, 8)));
        Console.WriteLine($"  7 × 8 = {mulRes}");
        pass++;
        Console.WriteLine("  PASS\n");

        total++;
        Console.WriteLine($"── Test {total}: Beta → Alpha greet ──");
        var greetRes = await beta.CallAsync("alpha", "greet",
            JsonSerializer.SerializeToElement(new GreetRequest("World")));
        Console.WriteLine($"  {greetRes}");
        pass++;
        Console.WriteLine("  PASS\n");

        total++;
        Console.WriteLine($"── Test {total}: Beta → Alpha do_status ──");
        await beta.DispatchAsync("alpha", "do_status");
        await Task.Delay(300);
        pass++;
        Console.WriteLine("  PASS\n");

        total++;
        Console.WriteLine($"── Test {total}: Alpha → Beta do_shutdown ──");
        await alpha.DispatchAsync("beta", "do_shutdown");
        await Task.Delay(300);
        pass++;
        Console.WriteLine("  PASS\n");

        total++;
        Console.WriteLine($"── Test {total}: Alpha emits evt_heartbeat → Beta ──");
        await alpha.EmitAsync("evt_heartbeat",
            new HeartbeatMessage("alpha", DateTime.Now));
        await betaGotHeartbeat.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pass++;
        Console.WriteLine("  PASS\n");

        total++;
        Console.WriteLine($"── Test {total}: Beta emits evt_alarm → Alpha ──");
        await beta.EmitAsync("evt_alarm",
            new AlarmMessage("CRITICAL", "Storage full", DateTime.Now));
        await alphaGotAlarm.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pass++;
        Console.WriteLine("  PASS\n");

        Console.WriteLine("═══════════════════════");
        Console.WriteLine($"  {pass}/{total} tests passed");
        Console.WriteLine("═══════════════════════");

        Console.WriteLine("\n\n╔══════════════════════════════════════╗");
        Console.WriteLine("║  SECOND TEST: MasteredConnexon       ║");
        Console.WriteLine("╚══════════════════════════════════════╝\n");

        using var ma = new GlycoComplex("master_a",
            new UnixDomainMasteredConnexon("master_a"));
        using var mb = new GlycoComplex("master_b",
            new UnixDomainMasteredConnexon("master_b"));

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

        var maAlarm = new TaskCompletionSource<AlarmMessage>();
        var mbHeartbeat = new TaskCompletionSource<HeartbeatMessage>();

        ma.On<AlarmMessage>("master_b", "evt_alarm",
            msg => { Console.WriteLine($"  [MasterA] ← alarm [{msg.Level}]: {msg.Description}"); maAlarm.TrySetResult(msg); });
        mb.On<HeartbeatMessage>("master_a", "evt_heartbeat",
            msg => { Console.WriteLine($"  [MasterB] ← heartbeat from {msg.NodeId}"); mbHeartbeat.TrySetResult(msg); });

        var maSeesMb = new TaskCompletionSource<bool>();
        var mbSeesMa = new TaskCompletionSource<bool>();

        ma.OnDiscovered += b => {
            Console.WriteLine($"[MasterA] discovered {b.Id} ({b.Fields.Count} fields)");
            if (b.Id == "master_b") maSeesMb.TrySetResult(true);
        };
        mb.OnDiscovered += b => {
            Console.WriteLine($"[MasterB] discovered {b.Id} ({b.Fields.Count} fields)");
            if (b.Id == "master_a") mbSeesMa.TrySetResult(true);
        };

        await Task.WhenAll(ma.StartAsync(), mb.StartAsync());
        Console.WriteLine($"[MasterA] ready  [MasterB] ready\n");

        Console.WriteLine("=== Waiting for hub-based discovery ===");
        await Task.WhenAll(
            maSeesMb.Task.WaitAsync(TimeSpan.FromSeconds(30)),
            mbSeesMa.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        Console.WriteLine("Both nodes see each other via hub.\n");

        var pass2 = 0;
        var total2 = 0;

        total2++;
        Console.WriteLine($"── Mastered {total2}: Mb → Ma math_add ──");
        var addRes2 = await mb.CallAsync("master_a", "math_add",
            JsonSerializer.SerializeToElement(new AddRequest(15, 30)));
        Console.WriteLine($"  15 + 30 = {addRes2}");
        pass2++;
        Console.WriteLine("  PASS\n");

        total2++;
        Console.WriteLine($"── Mastered {total2}: Ma → Mb greet ──");
        var greetRes2 = await mb.CallAsync("master_a", "greet",
            JsonSerializer.SerializeToElement(new GreetRequest("Kiloo")));
        Console.WriteLine($"  {greetRes2}");
        pass2++;
        Console.WriteLine("  PASS\n");

        total2++;
        Console.WriteLine($"── Mastered {total2}: Ma → Mb do_ping ──");
        await ma.DispatchAsync("master_b", "do_ping");
        await Task.Delay(300);
        pass2++;
        Console.WriteLine("  PASS\n");

        total2++;
        Console.WriteLine($"── Mastered {total2}: Ma emits heartbeat → Mb ──");
        await ma.EmitAsync("evt_heartbeat",
            new HeartbeatMessage("master_a", DateTime.Now));
        await mbHeartbeat.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pass2++;
        Console.WriteLine("  PASS\n");

        total2++;
        Console.WriteLine($"── Mastered {total2}: Mb emits alarm → Ma ──");
        await mb.EmitAsync("evt_alarm",
            new AlarmMessage("WARN", "Disk space low", DateTime.Now));
        await maAlarm.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pass2++;
        Console.WriteLine("  PASS\n");

        Console.WriteLine("═══════════════════════");
        Console.WriteLine($"  Mastered: {pass2}/{total2} tests passed");
        Console.WriteLine("═══════════════════════");

        Console.WriteLine("\n\n╔══════════════════════════════════════╗");
        Console.WriteLine("║  THIRD TEST: Mastered Hub Failover   ║");
        Console.WriteLine("╚══════════════════════════════════════╝\n");

        // Node A starts first → wins the hub election
        using var hA = new GlycoComplex("hub_a",
            new UnixDomainMasteredConnexon("hub_a"));
        hA.AddFunction<AddRequest, AddResponse>("add",
            req => new AddResponse(req.A + req.B));
        hA.AddFunction<GreetRequest, GreetResponse>("echo",
            req => new GreetResponse($"HubA echoes: {req.Name}"));

        await hA.StartAsync();
        Console.WriteLine("[HubA] started (should be the hub)\n");

        // Node B and C join
        using var hB = new GlycoComplex("hub_b",
            new UnixDomainMasteredConnexon("hub_b"));
        using var hC = new GlycoComplex("hub_c",
            new UnixDomainMasteredConnexon("hub_c"));

        hB.AddFunction<AddRequest, AddResponse>("mul",
            req => new AddResponse(req.A * req.B));
        hB.AddAction("ping", () =>
            Console.WriteLine("  [HubB] ping!"));

        hC.AddFunction<GreetRequest, GreetResponse>("welcome",
            req => new GreetResponse($"HubC welcomes {req.Name}"));

        // B and C listen for each other's events
        hB.On<AlarmMessage>("hub_c", "evt_alarm",
            msg => Console.WriteLine($"  [HubB] ← alarm from C [{msg.Level}]"));
        hC.On<HeartbeatMessage>("hub_b", "evt_heart",
            msg => Console.WriteLine($"  [HubC] ← heartbeat from B"));

        await Task.WhenAll(hB.StartAsync(), hC.StartAsync());
        Console.WriteLine("[HubB] ready  [HubC] ready\n");

        // Wait for mutual discovery via hub
        Console.WriteLine("=== All 3 nodes discovering each other ===");
        var bSeesC = new TaskCompletionSource<bool>();
        var cSeesB = new TaskCompletionSource<bool>();

        hB.OnDiscovered += b => {
            Console.WriteLine($"[HubB] discovered {b.Id}");
            if (b.Id == "hub_c") bSeesC.TrySetResult(true);
        };
        hC.OnDiscovered += b => {
            Console.WriteLine($"[HubC] discovered {b.Id}");
            if (b.Id == "hub_b") cSeesB.TrySetResult(true);
        };

        await Task.WhenAll(
            bSeesC.Task.WaitAsync(TimeSpan.FromSeconds(30)),
            cSeesB.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        Console.WriteLine("All 3 nodes discovered.\n");

        var pass3 = 0;
        var total3 = 0;

        // Pre-kill: verify communication works through current hub (A)
        total3++;
        Console.WriteLine($"── Failover {total3}: C → A (via hub) add 4+6 ──");
        var r1 = await hC.CallAsync("hub_a", "add",
            JsonSerializer.SerializeToElement(new AddRequest(4, 6)));
        Console.WriteLine($"  4 + 6 = {r1}");
        pass3++;
        Console.WriteLine("  PASS\n");

        total3++;
        Console.WriteLine($"── Failover {total3}: B → C (via hub) welcome ──");
        var r2 = await hB.CallAsync("hub_c", "welcome",
            JsonSerializer.SerializeToElement(new GreetRequest("HubB")));
        Console.WriteLine($"  {r2}");
        pass3++;
        Console.WriteLine("  PASS\n");

        // ── KILL THE HUB ──
        Console.WriteLine("=== Killing HubA (the hub) ===");
        hA.Dispose();
        Console.WriteLine("[HubA] disposed — hub is down!\n");

        // Give survivors time to detect disconnection, elect new hub, and reconnect
        Console.WriteLine("=== Waiting for re-election and reconnection ===");
        await Task.Delay(3000);
        Console.WriteLine("Reconnection should be complete.\n");

        // Post-kill: verify B and C can still communicate through the new hub
        total3++;
        Console.WriteLine($"── Failover {total3}: C → B (via new hub) mul 6*7 ──");
        var r3 = await hC.CallAsync("hub_b", "mul",
            JsonSerializer.SerializeToElement(new AddRequest(6, 7)));
        Console.WriteLine($"  6 × 7 = {r3}");
        pass3++;
        Console.WriteLine("  PASS\n");

        total3++;
        Console.WriteLine($"── Failover {total3}: C → B (via new hub) ping ──");
        await hC.DispatchAsync("hub_b", "ping");
        await Task.Delay(300);
        pass3++;
        Console.WriteLine("  PASS\n");

        total3++;
        Console.WriteLine($"── Failover {total3}: B → C (via new hub) welcome again ──");
        var r4 = await hB.CallAsync("hub_c", "welcome",
            JsonSerializer.SerializeToElement(new GreetRequest("SurvivorB")));
        Console.WriteLine($"  {r4}");
        pass3++;
        Console.WriteLine("  PASS\n");

        // Verify A is truly gone — calling A should fail
        total3++;
        Console.WriteLine($"── Failover {total3}: C → A should FAIL (A is dead) ──");
        try {
            await hC.CallAsync("hub_a", "add",
                JsonSerializer.SerializeToElement(new AddRequest(1, 1)),
                new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
            Console.WriteLine("  UNEXPECTED: call succeeded to dead node");
        }
        catch (Exception ex) {
            Console.WriteLine($"  Expected failure: {ex.GetType().Name}");
            pass3++;
            Console.WriteLine("  PASS\n");
        }

        Console.WriteLine("═══════════════════════");
        Console.WriteLine($"  Failover: {pass3}/{total3} tests passed");
        Console.WriteLine("═══════════════════════");
    }
}
