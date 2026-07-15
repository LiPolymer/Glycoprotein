namespace Glycoprotein.Debug.Model;

public sealed class RunConfig {
    public bool ListOnly { get; set; }
    public string? ScenarioFilter { get; set; }
    public bool Interactive { get; set; }
    public bool JsonOutput { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    public static RunConfig Parse(string[] args) {
        var cfg = new RunConfig();
        for (int i = 0; i < args.Length; i++) {
            switch (args[i].ToLowerInvariant()) {
                case "--list":
                    cfg.ListOnly = true;
                    break;
                case "--scenario" when i + 1 < args.Length:
                    cfg.ScenarioFilter = args[++i];
                    break;
                case "--interactive":
                    cfg.Interactive = true;
                    break;
                case "--output" when i + 1 < args.Length:
                    cfg.JsonOutput = args[++i].Equals("json", StringComparison.OrdinalIgnoreCase);
                    break;
                case "--timeout" when i + 1 < args.Length:
                    if (double.TryParse(args[++i], out var sec))
                        cfg.Timeout = TimeSpan.FromSeconds(sec);
                    break;
            }
        }
        return cfg;
    }
}
