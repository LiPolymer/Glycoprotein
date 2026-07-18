using Glycoprotein.Glycosylation;
using ShulkerRDK.Shared;

namespace Glycoprotein.Demo;

public record PingRespondModel(string Message);

static class Program {
    static readonly Dictionary<string,Func<string[],Task>> Actions = [];
    static GlycoComplex? _gly;

    static async Task Main(string[] _) {
        Terminal.Init(new AnsiTerminal());
        Terminal.Write("&6Gimme an identity:");
        string id = Console.ReadLine() ?? Guid.NewGuid().ToString();
        _gly = new GlycoComplex(id)
            .AddFunction(new Field.Method {
                Id = "ping",
                FriendlyName = "Ping!",
                Description = "Get请求测试"
            },() => {
                Terminal.WriteLine("Received Ping!");
                return new PingRespondModel("Pong!");
            });
        await _gly.StartAsync();
        Terminal.WriteLine("&aComplex Started");
        _gly.OnDiscovered += beacon => {
            Terminal.WriteLine("\nDiscovered:" + beacon.Id + "\n  " + string.Join("\n  ",beacon.Fields
                                                                                      .Select(field => field.Id + "|"
                                                                                                  + field.FriendlyName + "|"
                                                                                                  + field.Description)));
            Terminal.Write("&6>&e");
        };
        _gly.OnExpired += beacon => Terminal.Write($"\nExpired:{beacon.Id}\n&6>&e");
        Actions.Add("list",_ => {
            try {
                Terminal.WriteLine(string.Join('\n',_gly.Presenters
                                                   .Select(beacon => beacon.Id + "\n  "
                                                                               + string.Join("\n  ",beacon.Fields
                                                                                                 .Select(field => field.Id + "|"
                                                                                                          + field.FriendlyName + "|"
                                                                                                          + field.Description)))));
                return Task.CompletedTask;
            }
            catch (Exception exception) {
                return Task.FromException(exception);
            }
        });
        Actions.Add("act",async arg => {
            if (arg.Length < 3) {
                Terminal.WriteLine($"&cMissing params: &6required 3, received {arg.Length}");
                return;
            }
            try {
                await _gly.DoActionAsync(arg[1],arg[2]);
            }
            catch (Exception e) {
                Terminal.WriteLine(e.Message);
            }
        });
        Actions.Add("ping",async arg => {
            if (arg.Length < 2) {
                Terminal.WriteLine($"&cMissing params: &6required 2, received {arg.Length}");
                return;
            }
            try {
                Terminal.WriteLine((await _gly.CallFunctionAsync<PingRespondModel>(arg[1],"ping"))?.Message ?? "Invalid Receipt");
            }
            catch (Exception e) {
                Terminal.WriteLine(e.Message);
            }
        });
        InteractLoop();
    }

    static void InteractLoop() {
        while (true) {
            Terminal.Write("&6>&e");
            string cmd = Console.ReadLine()!;
            Terminal.Write("&r");
            string[] cmdArg = Tools.ResolveArgs(cmd);
            if (cmdArg.Length == 0) continue;
            if (Actions.TryGetValue(cmdArg[0],out Func<string[],Task>? act)) act(cmdArg).Wait();
            else Terminal.WriteLine($"&cCommand {cmdArg[0]} not found!");
        }
        // ReSharper disable once FunctionNeverReturns
    }
}