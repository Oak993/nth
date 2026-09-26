using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Isolated model and logical-tree helpers for MCPSettingsPage.swift; no real credentials or probes.
public static class MCPSettingsPageFixture
{
    public static MCPCatalogEntry Entry(string id = "offline", MCPTransport? transport = null) => new(
        id, "Example", "Vendor", "Summary", MCPCategory.Developer, "", 0,
        transport ?? new MCPTransport.Stdio("offline-fixture", ValueList<string>.Empty), "");
    public static MCPCustomServer Custom(int rows = 0, MCPCustomServer.Kind kind = MCPCustomServer.Kind.Stdio) => new("custom-example", "Example", kind)
    {
        Command = "offline-fixture", Url = "https://example.invalid/mcp",
        Env = new ValueList<MCPCustomServer.Variable>(Enumerable.Range(0, rows).Select(i => new MCPCustomServer.Variable("KEY" + i))),
        Headers = new ValueList<MCPCustomServer.Variable>(Enumerable.Range(0, rows).Select(i => new MCPCustomServer.Variable("KEY" + i))),
    };
    public static T[] Nodes<T>(Control control) where T : Control => control.GetSelfAndLogicalDescendants().OfType<T>().ToArray();
    public static string[] Texts(Control control) => Nodes<TextBlock>(control).Select(x => x.Text ?? "").ToArray();
    public static Button Button(Control control, string title) => Nodes<Button>(control).Single(b => b.Content as string == title || (b.Content is Control c && Texts(c).Contains(title)));
    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    public static TextBox Field(Control control, string prompt) => Nodes<TextBox>(control).First(x => x.PlaceholderText == prompt);
    public static void State(AppModel model, string id, MCPConnectionState value) =>
        ((Dictionary<string, MCPConnectionState>)typeof(MCPStore).GetField("statesField", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model.Mcp)!)[id] = value;
    public static void Connection(AppModel model, MCPConnection connection) =>
        ((Dictionary<string, MCPConnection>)typeof(MCPStore).GetField("connectionsField", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model.Mcp)!)[connection.CatalogID] = connection;
    public static IDisposable Attach(Control control, AppModel model)
    {
        AppEnvironment.SetAppModel(control, model);
        var window = new Avalonia.Controls.Window { Content = control, Width = 800, Height = 800 };
        window.Show(); Dispatcher.UIThread.RunJobs(); return new MCPSettingsWindowScope(window);
    }
    public static T Read<T>(object target, string name) => (T)target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target)!;
    public static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);
}


public sealed class MCPSettingsWindowScope(Avalonia.Controls.Window window) : IDisposable
{
    public void Dispose() => window.Close();
}

