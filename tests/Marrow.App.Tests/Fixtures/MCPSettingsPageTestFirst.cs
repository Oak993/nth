using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Helpers for the test-first MCPSettingsPage tests (MCPSettingsPage.swift). No real server, browser or CLI is started:
// a click on Connect queues the store's probe on the manual main loop, and these helpers never let it run.
public static class MCPSettingsPageTestFirst
{
    /// <summary>Runs exactly ONE queued main-loop item (the body) and nothing queued behind it. Connect queues its
    /// probe as a main-loop task; draining the loop would run the probe (an OAuth probe opens a browser sign-in).</summary>
    public static void Step(TestRuntime runtime, Action body)
    {
        runtime.Loop.Enqueue(body);
        runtime.Loop.RunUntil(() => true);
    }

    /// <summary>Publishes a connection state through the store's observable setter, as a finished probe would.</summary>
    public static void Publish(AppModel model, string id, MCPConnectionState state) =>
        typeof(MCPStore).GetProperty(nameof(MCPStore.States))!.SetValue(model.Mcp, new Dictionary<string, MCPConnectionState>(model.Mcp.States, StringComparer.Ordinal) { [id] = state });

    /// <summary>The window a floating guide panel presents, or null before it was ever shown.</summary>
    public static Avalonia.Controls.Window? PanelWindow(FloatingGuidePanel owner) =>
        (Avalonia.Controls.Window?)typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);

    public static bool KeyPanelVisible() => PanelWindow(MCPKeyPanel.Shared)?.IsVisible ?? false;

    /// <summary>The ids with a probe queued in the store (MCPStore.swift:78 probes[entry.id]).</summary>
    public static bool ProbeQueued(MCPStore store, string id) => ((System.Collections.IDictionary)MCPSettingsPageFixture.Read<object>(store, "probes")).Contains(id);

    /// <summary>The drawn solid colour equals the Swift colour token in both themes.</summary>
    public static void Colour(IBrush? actual, SwiftColor expected, Control owner)
    {
        var window = TopLevel.GetTopLevel(owner) as Avalonia.Controls.Window;
        var previous = window?.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                if (window is not null) { window.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs(); }
                var colour = ((ISolidColorBrush)expected.Brush(owner)).Color;
                var drawn = Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color;
                Assert.Equal(colour, drawn);
            }
        }
        finally { if (window is not null) { window.RequestedThemeVariant = previous; Dispatcher.UIThread.RunJobs(); } }
    }

    /// <summary>Lays the window out and renders a frame so Bounds and hit testing are real.</summary>
    public static void Layout(Control control)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
    }

    /// <summary>The control's top-left in the window's coordinates.</summary>
    public static Point Origin(Control control) => control.TranslatePoint(new Point(0, 0), TopLevel.GetTopLevel(control)!)!.Value;
}
