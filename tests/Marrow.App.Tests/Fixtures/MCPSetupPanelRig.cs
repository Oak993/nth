using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Builders and probes for the MCP setup panel's tests (App/Views/Settings/MCPSetupPanel.swift).</summary>
public static class MCPSetupPanelRig
{
    /// <summary>A remote server on a loopback port nothing listens on, so even a probe that did run reaches no real service.</summary>
    public static MCPCatalogEntry Entry(string id = "gitlab", string name = "GitLab") =>
        new(id, name, "GitLab", "Source control", MCPCategory.Developer, "", 0,
            new MCPTransport.Http("http://127.0.0.1:9/mcp", ValueMap<string, string>.Empty), "https://gitlab.com");

    /// <summary>The one guide the app ships (a GitLab server that is off on its own side), with its note replaceable.</summary>
    public static MCPSetupGuide Guide(string? note = "GitLab can take a minute to apply the change.") =>
        MCPProbe.GitLabServerDisabled("https://gitlab.com/api/v4/mcp") with { Note = note };

    /// <summary>Writes one server's connection state straight into the store (no probe, no I/O).</summary>
    public static void SetState(AppModel model, string id, MCPConnectionState state)
    {
        var field = typeof(MCPStore).GetField("statesField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var next = new Dictionary<string, MCPConnectionState>((Dictionary<string, MCPConnectionState>)field.GetValue(model.Mcp)!) { [id] = state };
        typeof(MCPStore).GetProperty("States")!.SetValue(model.Mcp, next);
    }

    /// <summary>Cancels every probe the store spawned, so nothing the panel started can run later.</summary>
    public static void CancelProbes(AppModel model)
    {
        var probes = (System.Collections.IDictionary)typeof(MCPStore).GetField("probes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model.Mcp)!;
        foreach (var handle in probes.Values) ((TaskHandle)handle!).Cancel();
    }

    /// <summary>Shows the view in a window of its own panel width and lays it out.</summary>
    public static Avalonia.Controls.Window Show(Control content, double width = 400, double height = 600)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    public static IReadOnlyList<T> All<T>(Visual root) where T : class =>
        [.. root.GetSelfAndVisualDescendants().OfType<T>()];

    public static TextBlock Text(Visual root, string text) =>
        All<TextBlock>(root).Single(block => block.Text == text);

    public static Button ButtonLabelled(Visual root, string label) =>
        All<Button>(root).Single(button => button.Content as string == label
            || button.GetSelfAndVisualDescendants().OfType<TextBlock>().Any(block => block.Text == label));

    public static void Click(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
