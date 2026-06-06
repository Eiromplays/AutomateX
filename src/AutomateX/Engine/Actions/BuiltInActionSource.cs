namespace AutomateX.Engine.Actions;

public sealed class BuiltInActionSource(IServiceProvider serviceProvider) : IActionSource
{
    public IEnumerable<RegisteredAction> GetActions() =>
        ActionDiscovery.FromAssembly(typeof(BuiltInActionSource).Assembly, "builtin", serviceProvider);
}
