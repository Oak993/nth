using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Web;

namespace Marrow.App.Tests.Fixtures;

// Owner-scheduled only (web surfaces security review, finding L7; ED-NEW-web-sec-mcpapps-4). A test-owned off-screen window,
// the real WebView2 engine in a throwaway profile folder, and one MCP App page mounted by nth's real MCP Apps host with a
// declaration the user allowed: this rig is the server (the page, no tools) and the user (Allow on the domain card, no to
// every other card). The page reports through its title which addresses its CSP refused; nth's request filter logs what it
// refused. No pane, model or provider is bound.
internal sealed class McpAppReachNativeRig : IMcpAppServers, IMcpAppHostUi, IDisposable
{
    public const string Prefix = "nth-reach:";
    public const string Resource = "ui://shipyard/reach.html";

    private readonly McpUiCsp declared;
    private readonly string html;

    public McpAppReachNativeRig(WindowManagerNativeSession native, McpUiCsp declared, string html)
    {
        this.declared = declared;
        this.html = html;
        Folder = Path.Combine(Path.GetTempPath(), "nth-sec-native-" + Guid.NewGuid().ToString("N"));
        Surface = new WebSurface(new WebView2Engine(), new WebProfiles(Path.Combine(Folder, "WebView2"), "en-US"));
        View = Surface.Open(WebSurfaceKind.McpApp, "sec-native");
        Grants = new McpAppGrantStore(null);
        Host = new McpAppsHost(this, Grants, this, static action => Avalonia.Threading.Dispatcher.UIThread.Post(action), log: Log);
        Mount = Host.Mount(View, new McpAppLaunch(Guid.NewGuid(), "shipyard", Resource) { ToolName = "reach", Title = "Reach" });
        Window = new Avalonia.Controls.Window
        {
            Title = "MCP App reach fixture", Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false,
            Position = new PixelPoint(-32000, -32000), Content = new WebViewHost(View, null),
        };
        Window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(Window);
        Window.Show();
        View.SetShown(true);
    }

    public string Folder { get; }

    public WebSurface Surface { get; }

    public WebSurfaceView View { get; }

    public McpAppGrantStore Grants { get; }

    public McpAppsHost Host { get; }

    public McpAppMount Mount { get; }

    public Avalonia.Controls.Window Window { get; }

    /// <summary>Every line nth's host logged (its request filter's refusals among them).</summary>
    public List<string> Logs { get; } = [];

    /// <summary>Why the throwaway folder stayed behind after Dispose, if it did.</summary>
    public string? LeftBehind { get; private set; }

    /// <summary>True once the user's Allow is the page's grant.</summary>
    public bool Granted => Grants.Get("shipyard").Domains.ContainsKey(Resource);

    /// <summary>The page's report (JSON) once it has set its title, else null.</summary>
    public string? Report() =>
        View.Engine?.Title is { } title && title.StartsWith(Prefix, StringComparison.Ordinal) ? title[Prefix.Length..] : null;

    private void Log(string line)
    {
        lock (Logs) Logs.Add(line);
    }

    // ---- the server ----

    public Task<McpAppPage> ReadPageAsync(string server, string uri, CancellationToken cancellationToken) =>
        Task.FromResult(new McpAppPage(server, uri, html, declared));

    public Task<IReadOnlyList<McpAppTool>> ToolsAsync(string server, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<McpAppTool>>([]);

    public Task<JsonObject> CallToolAsync(string server, string tool, JsonObject arguments, CancellationToken cancellationToken) =>
        Task.FromException<JsonObject>(new InvalidOperationException("This fixture has no tools."));

    public Task<JsonObject> ReadResourceAsync(string server, string uri, CancellationToken cancellationToken) =>
        Task.FromException<JsonObject>(new InvalidOperationException("This fixture has no resources."));

    // ---- the user ----

    public bool Dark => false;

    public event Action? ThemeChanged
    {
        add { }
        remove { }
    }

    public Task<McpAppDomainDecision> AskDomainsAsync(McpAppMount app, McpAppDomainRequest request) => Task.FromResult(McpAppDomainDecision.Allow);

    public Task<bool> AskMessagesAsync(McpAppMount app) => Task.FromResult(false);

    public Task<McpAppToolDecision> AskToolAsync(McpAppMount app, string tool, string? title, JsonObject arguments) => Task.FromResult(McpAppToolDecision.Deny);

    public Task<bool> AskPermissionAsync(McpAppMount app, WebPermissionKind kind) => Task.FromResult(false);

    public bool ToolsNeedApproval(Guid thread) => true;

    public string? PostMessage(McpAppMount app, string text) => "This fixture takes no messages.";

    public void SetModelContext(McpAppMount app, string text)
    {
    }

    public void OpenLink(McpAppMount app, string url)
    {
    }

    public Task<bool> AskOpenLinkAsync(McpAppMount app, string url) => Task.FromResult(false);

    public Task<bool> AskDisplayModeAsync(McpAppMount app, McpAppDisplayMode mode) => Task.FromResult(false);

    public void Dispose()
    {
        Mount.Dispose();
        Surface.Close(View);
        Window.Close();
        Surface.Dispose();
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException error)
        {
            LeftBehind = error.Message; // the test-owned browser process may still be releasing its profile
        }
    }
}
