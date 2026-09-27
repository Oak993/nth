using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
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
    public static Button Button(Control control, string title) => Nodes<Button>(control).Single(b => AutomationProperties.GetName(b) == title || b.Content as string == title || (b.Content is Control c && Texts(c).Contains(title)));
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
    public static T Read<T>(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        return (T)(target.GetType().GetProperty(name, flags)?.GetValue(target) ?? target.GetType().GetField(name, flags)!.GetValue(target))!;
    }
    public static T Invoke<T>(object target, string name) => (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, null)!;
    public static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);
}


public sealed class MCPSettingsWindowScope(Avalonia.Controls.Window window) : IDisposable
{
    public void Dispose() => window.Close();
}




public sealed class MCPSettingsRefusingCredentialBackend : Marrow.Services.Port.ICredentialBackend
{
    private readonly AppModelCredentialBackend memory = new();
    public bool Refuse { get; set; } = true;
    public bool RefuseDeletes { get; set; }
    public int Write(Marrow.Services.Port.GenericCredential credential) => Refuse ? 5 : memory.Write(credential);
    public int Read(string name, out Marrow.Services.Port.GenericCredential? credential) => memory.Read(name, out credential);
    public int Delete(string name) => RefuseDeletes ? 5 : memory.Delete(name);
}

// Test sound seam: records playback requests and never opens an audio device.
public sealed class MCPSettingsSilentSound : ISoundOutput
{
    public int Plays { get; private set; }
    public ISoundPlayback Start(SwiftSound sound, float volume, Action finished) { Plays++; return new Playback(); }
    private sealed class Playback : ISoundPlayback { public void Stop() { } }
}
