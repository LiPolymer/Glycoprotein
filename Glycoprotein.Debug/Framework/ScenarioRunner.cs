using System.Reflection;
using Glycoprotein.Debug.Model;

namespace Glycoprotein.Debug.Framework;

public sealed class ScenarioRunner {
    readonly RunConfig _config;

    public ScenarioRunner(RunConfig config) {
        _config = config;
    }

    static string UniqueSocketDir(string basename) =>
        Path.Combine(Path.GetTempPath(), $"{basename}_{Guid.NewGuid():N}");

    public static string UniqueMeshDir() => UniqueSocketDir("glycoprotein");
    public static string UniqueMasteredDir() => UniqueSocketDir("glycoprotein_mastered");

    public static IReadOnlyList<ScenarioBase> DiscoverScenarios() {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true }
                        && t.IsSubclassOf(typeof(ScenarioBase)))
            .Select(Activator.CreateInstance)
            .Cast<ScenarioBase>()
            .OrderBy(s => s.Name)
            .ToList();
    }

    public IReadOnlyList<ScenarioBase> FilterScenarios(IReadOnlyList<ScenarioBase> all) {
        if (_config.ScenarioFilter == null) return all;
        return all.Where(s =>
            s.Name.Contains(_config.ScenarioFilter, StringComparison.OrdinalIgnoreCase)
            || s.Description.Contains(_config.ScenarioFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<List<ScenarioResult>> RunAllAsync(IReadOnlyList<ScenarioBase> scenarios) {
        var results = new List<ScenarioResult>();
        foreach (var scenario in scenarios) {
            results.Add(await RunSingleAsync(scenario));
        }
        return results;
    }

    async Task<ScenarioResult> RunSingleAsync(ScenarioBase scenario) {
        using var ctx = new SceneContext(_config);
        var scenarioStart = DateTime.UtcNow;

        try {
            await scenario.RunAsync(ctx).WaitAsync(_config.Timeout);
        }
        catch (TimeoutException) {
            ctx.RecordStep("(timeout)", false, $"Scenario exceeded {_config.Timeout.TotalSeconds}s timeout");
        }
        catch (Exception ex) {
            ctx.RecordStep("(fatal)", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        return new ScenarioResult {
            Name = scenario.Name,
            Description = scenario.Description,
            Steps = [.. ctx.Steps],
            Duration = DateTime.UtcNow - scenarioStart
        };
    }
}
