using Glycoprotein.Debug.Framework;
using Glycoprotein.Debug.Model;

namespace Glycoprotein.Debug;

static class Program {
    static async Task<int> Main(string[] args) {
        var config = RunConfig.Parse(args);

        if (args.Length == 0) {
            Console.WriteLine("Usage: Glycoprotein.Debug [options]");
            Console.WriteLine("  --list                List all scenarios");
            Console.WriteLine("  --scenario <name>     Run matching scenario(s)");
            Console.WriteLine("  --interactive         Step-by-step with keypress between steps");
            Console.WriteLine("  --output json|text    Output format (default: text)");
            Console.WriteLine("  --timeout <seconds>   Per-scenario timeout (default: 60)");
            Console.WriteLine();
        }

        var all = ScenarioRunner.DiscoverScenarios();

        if (config.ListOnly) {
            Console.WriteLine($"Discovered {all.Count} scenario(s):");
            foreach (var s in all) {
                Console.WriteLine($"  {s.Name}");
                Console.WriteLine($"    {s.Description}");
            }
            return 0;
        }

        var runner = new ScenarioRunner(config);
        var scenarios = runner.FilterScenarios(all);

        if (scenarios.Count == 0) {
            Console.WriteLine("No matching scenarios found.");
            return 1;
        }

        var results = await runner.RunAllAsync(scenarios);
        OutputRenderer.Render(results, config.JsonOutput);

        CleanTempDirs();

        return results.All(r => r.Passed) ? 0 : 1;
    }

    static void CleanTempDirs() {
        foreach (var pattern in new[] { "glycoprotein*" }) {
            var dirs = Directory.GetDirectories(Path.GetTempPath(), pattern);
            foreach (var dir in dirs) {
                try { Directory.Delete(dir, true); }
                catch { }
            }
        }
    }
}
