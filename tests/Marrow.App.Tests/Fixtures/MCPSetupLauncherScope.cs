using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The browser boundary for the MCP setup panel: while it lives, the page the panel opens in "the default browser"
/// (a window's launcher, found through the application's desktop lifetime) is recorded here and never opened.
/// </summary>
public sealed class MCPSetupLauncherScope : IDisposable
{
    private static readonly FieldInfo PlatformField = typeof(TopLevel).GetField("<PlatformImpl>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo LifetimeField = typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly Avalonia.Controls.Window mainWindow;
    private readonly IWindowImpl original;
    private readonly object? previousLifetime;

    public MCPSetupLauncherScope()
    {
        mainWindow = new Avalonia.Controls.Window { Content = new Border() };
        mainWindow.Show();
        original = (IWindowImpl)PlatformField.GetValue(mainWindow)!;
        var proxy = DispatchProxy.Create<IWindowImpl, MCPProofsMcpAWindowProxy>();
        var typed = (MCPProofsMcpAWindowProxy)(object)proxy;
        typed.Original = original;
        typed.Launcher = Launcher;
        PlatformField.SetValue(mainWindow, proxy);
        previousLifetime = LifetimeField.GetValue(Application.Current);
        LifetimeField.SetValue(Application.Current, new ClassicDesktopStyleApplicationLifetime { MainWindow = mainWindow });
    }

    /// <summary>Every page the panel asked the browser to open, in order.</summary>
    public MCPProofsMcpALauncher Launcher { get; } = new();

    public void Dispose()
    {
        LifetimeField.SetValue(Application.Current, previousLifetime);
        PlatformField.SetValue(mainWindow, original);
        mainWindow.Close();
    }
}
