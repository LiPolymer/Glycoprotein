using System.Text.Json;
using Glycoprotein.Debug.Model;

namespace Glycoprotein.Debug.Framework;

public static class OutputRenderer {
    public static void Render(List<ScenarioResult> results, bool jsonOutput) {
        if (jsonOutput) {
            RenderJson(results);
        }
        else {
            RenderText(results);
        }
    }

    static void RenderJson(List<ScenarioResult> results) {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(results, opts));
    }

    static void RenderText(List<ScenarioResult> results) {
        foreach (var scenario in results) {
            Console.WriteLine();
            Console.WriteLine($"═══ {scenario.Name} ═══");
            Console.WriteLine($"  {scenario.Description}");
            Console.WriteLine();

            foreach (var step in scenario.Steps) {
                var icon = step.Passed ? "[PASS]" : "[FAIL]";
                Console.Write(step.Passed
                    ? $"  {icon} {step.Label}"
                    : $"  {icon} {step.Label}");
                if (step.Duration.TotalMilliseconds > 0)
                    Console.Write($" ({step.Duration.TotalMilliseconds:F0}ms)");
                Console.WriteLine();
                if (!step.Passed && step.Error != null)
                    Console.WriteLine($"        Error: {step.Error}");
            }

            Console.WriteLine();
            Console.WriteLine($"  {scenario.PassCount}/{scenario.TotalCount} passed  ({scenario.Duration.TotalSeconds:F1}s)");
        }

        var totalPass = results.Sum(r => r.PassCount);
        var totalCount = results.Sum(r => r.TotalCount);
        Console.WriteLine();
        Console.WriteLine("═══════════════════════");
        Console.WriteLine($"  {totalPass}/{totalCount} tests passed");
        Console.WriteLine("═══════════════════════");

        var allPassed = results.All(r => r.Passed);
        if (!allPassed) {
            var failedScenarios = results.Where(r => !r.Passed).Select(r => r.Name);
            Console.WriteLine();
            Console.WriteLine("Failed scenarios:");
            foreach (var name in failedScenarios)
                Console.WriteLine($"  - {name}");
        }
    }
}
