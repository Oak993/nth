using System.Reflection;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Marrow.App.Tests.Fixtures;

// Pass every native call through to the real headless window; intercept only its optional browser feature.
public class MCPProofsMcpAWindowProxy : DispatchProxy
{
    public IWindowImpl Original { get; set; } = null!;
    public ILauncher Launcher { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod!.Name == "TryGetFeature" && args![0] as Type == typeof(ILauncher)
            ? Launcher : targetMethod.Invoke(Original, args);
}
