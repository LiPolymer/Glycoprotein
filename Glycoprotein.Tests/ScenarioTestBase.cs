using Xunit.Abstractions;

namespace Glycoprotein.Tests;

/// <summary>
/// Base for scenario tests: step-level logging via ITestOutputHelper.
/// A failed step logs a ✗ marker, then rethrows — xUnit fail-fast semantics are preserved.
/// </summary>
public abstract class ScenarioTestBase {
    protected readonly ITestOutputHelper Output;

    protected ScenarioTestBase(ITestOutputHelper output) {
        Output = output;
    }

    protected async Task StepAsync(string label, Func<Task> step) {
        var start = DateTime.UtcNow;
        Output.WriteLine($"  ▶ {label}");
        try {
            await step();
        }
        catch {
            Output.WriteLine($"  ✗ {label} - FAILED after {(DateTime.UtcNow - start).TotalMilliseconds:F0}ms");
            throw;
        }
        Output.WriteLine($"  ✓ {label} ({(DateTime.UtcNow - start).TotalMilliseconds:F0}ms)");
    }
}
