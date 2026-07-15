namespace Glycoprotein.Debug.Model;

public sealed class ScenarioResult {
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<StepResult> Steps { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public bool Passed => Steps.Count > 0 && Steps.All(s => s.Passed);
    public int PassCount => Steps.Count(s => s.Passed);
    public int TotalCount => Steps.Count;
}
