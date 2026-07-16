using Glycoprotein.Debug.Model;

namespace Glycoprotein.Debug.Framework;

public abstract class ScenarioBase {
    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract Task RunAsync(SceneContext ctx);
}
