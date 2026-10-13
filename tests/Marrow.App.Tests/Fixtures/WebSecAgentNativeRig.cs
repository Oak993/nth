using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: web surfaces security review, the native checks (E:/Droppy/work/scratch/lead/web-sec-findings.md H1, H2, M2;
// ED-NEW-web-sec-agent-1, -2, -4). Owner-scheduled only. A test-owned window and profile, one agent tab on a made-up shop page that
// holds another made-up site's payment frame, and the real hub, engine and WebView2. Every request is answered in memory; the
// shop page shows what its note field, the frame's password field and the frame's Pay now button received.

/// <summary>Two made-up sites: a shop page (a new-tab link, a note field, a status line) and another site's payment frame.</summary>
public sealed class WebSecAgentNativeSites : IWebResourceHandler
{
    public const string Shop = "https://shop.invalid/";
    public const string Next = "https://other.invalid/next";

    // Layout in CSS px of the visible page: the note field's centre is (120, 75); the frame starts at (40, 120), so its Pay now
    // button's centre is (140, 160) and its password field's centre is (160, 235).
    private const string ShopPage = """
        <!doctype html><html><head><title>Shop</title></head><body style="margin:0;font:16px sans-serif">
        <a href="https://other.invalid/next" target="_blank" style="position:absolute;left:20px;top:20px">Read the next page</a>
        <input id="note" aria-label="Note" style="position:absolute;left:20px;top:60px;width:200px;height:30px;margin:0;border:0;padding:0">
        <p id="state" style="position:absolute;left:300px;top:20px;margin:0">note 0, typed 0, pay idle</p>
        <iframe src="https://pay.invalid/card" title="Payment" style="position:absolute;left:40px;top:120px;width:400px;height:220px;border:0"></iframe>
        <script>
        let note = 0, typed = 0, pay = 'idle';
        const show = () => { document.getElementById('state').textContent = 'note ' + note + ', typed ' + typed + ', pay ' + pay; };
        document.getElementById('note').addEventListener('input', e => { note = e.target.value.length; show(); });
        addEventListener('message', e => { const d = String(e.data); if (d === 'pay') pay = 'pressed'; else if (d.startsWith('typed ')) typed = Number(d.slice(6)); show(); });
        </script></body></html>
        """;

    private const string PayFrame = """
        <!doctype html><html><head><title>Card</title></head><body style="margin:0;font:16px sans-serif">
        <button style="position:absolute;left:20px;top:20px;width:160px;height:40px" onclick="parent.postMessage('pay', '*')">Pay now</button>
        <input type="password" aria-label="Password" style="position:absolute;left:20px;top:100px;width:200px;height:30px;margin:0;border:0;padding:0" oninput="parent.postMessage('typed ' + this.value.length, '*')">
        </body></html>
        """;

    private const string NextPage = "<!doctype html><html><head><title>Next</title></head><body><p>Next page</p></body></html>";

    /// <param name="shop">Another shop page (the second-pass checks, ED-NEW-web-sec-agent-7 to -9); null for the one above.</param>
    /// <param name="frames">Other frame pages by address start; a request no entry starts is answered as above.</param>
    public WebSecAgentNativeSites(string? shop = null, IReadOnlyDictionary<string, string>? frames = null)
    {
        this.shop = shop ?? ShopPage;
        this.frames = frames ?? new Dictionary<string, string>();
    }

    private readonly string shop;
    private readonly IReadOnlyDictionary<string, string> frames;

    public WebResourceResponse Answer(WebResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var framed = frames.FirstOrDefault(f => request.Uri.StartsWith(f.Key, StringComparison.OrdinalIgnoreCase)).Value;
        var html = framed is not null ? framed
            : request.Uri.StartsWith("https://pay.invalid/", StringComparison.OrdinalIgnoreCase) ? PayFrame
            : request.Uri.StartsWith("https://other.invalid/", StringComparison.OrdinalIgnoreCase) ? NextPage
            : shop;
        return new(200, "OK", [new("Content-Type", "text/html; charset=utf-8"), new("Cache-Control", "no-store")], Encoding.UTF8.GetBytes(html));
    }
}

/// <summary>The window, the agent tab on the shop page, and the web tools bound to the test chat (no site card: AskNewSites off).</summary>
internal sealed class WebSecAgentNativeRig : IDisposable
{
    public const string Thread = "5b0f3c2e-7d4a-4c1b-9e2f-1a2b3c4d5e6f";

    public WebSecAgentNativeRig(WindowManagerNativeSession native, WebSecAgentNativeSites? sites = null)
    {
        ArgumentNullException.ThrowIfNull(native);
        Folder = Path.Combine(Path.GetTempPath(), "nth-web-sec-native-" + Guid.NewGuid().ToString("N"));
        VisusHost.ResetForTests();
        VisusHost.DataFolder = null;
        VisusHost.SurfaceFactory = () => new WebSurface(new WebView2Engine(), new WebProfiles(Folder, "en-US"));
        Session = new VisusSession();
        Session.AgentThread = Thread;
        Tab = Session.NewTab(VisusTabKind.Agent, WebSecAgentNativeSites.Shop);
        Tab.View!.Engine!.SetResourceHandler(sites ?? new WebSecAgentNativeSites());
        Tab.View.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess && e.Url == WebSecAgentNativeSites.Shop) Loaded = true;
        };
        Chats = new VisusAgentChatsFake(Session, Thread) { PaneOpen = true };
        Hub = new WebAgentHub(() => null, () => null);
        Binding = new VisusAgentBinding(Chats, Hub, new VisusAgentAsks(), () => new PublishedJobSource("Published"), grants: new WebAgentGrants(),
            settings: () => WebAgentSettings.Default with { AskNewSites = false }, watchTurnEnd: (_, _) => { });
        Body = new VisusPaneBody(Session);
        Window = new Avalonia.Controls.Window
        {
            Title = "Web security native fixture", Width = 900, Height = 700,
            Content = Body, ShowInTaskbar = false,
        };
        Window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(Window);
        Window.Show();
    }

    public string Folder { get; }
    public VisusSession Session { get; }
    public VisusTab Tab { get; }
    public VisusAgentChatsFake Chats { get; }
    public WebAgentHub Hub { get; }
    public VisusAgentBinding Binding { get; }
    public VisusPaneBody Body { get; }
    public Avalonia.Controls.Window Window { get; }
    public bool Loaded { get; private set; }

    /// <summary>One web tool call as the chat's model makes it: started on the UI thread, awaited on the test's thread.</summary>
    public WebToolResult Call(WindowManagerNativeSession native, string tool, string json)
    {
        ArgumentNullException.ThrowIfNull(native);
        var call = native.OnUi(() => Hub.Engine(Thread)?.Call(tool, JsonNode.Parse(json)!.AsObject(), new WebAgentCaller(Thread, "claude-opus-5-5", true), CancellationToken.None))
            ?? throw new InvalidOperationException("The web tools are not bound to the test chat.");
        return call.WaitAsync(TimeSpan.FromSeconds(40)).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Binding.Dispose();
        Window.Close();
        Session.Dispose();
        VisusHost.ResetForTests();
        try { Directory.Delete(Folder, true); }
        catch (IOException) { } // The test-owned browser process may still be releasing its profile.
    }
}
