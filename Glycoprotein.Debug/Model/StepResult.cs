namespace Glycoprotein.Debug.Model;

public sealed class StepResult {
    public string Label { get; init; } = "";
    public bool Passed { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
}
