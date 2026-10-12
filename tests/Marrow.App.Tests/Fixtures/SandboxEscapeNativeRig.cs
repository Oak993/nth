using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Models;
using Marrow.Services.Web;
using Marrow.UI.Web;

namespace Marrow.App.Tests.Fixtures;

// Owner-scheduled only (W26 sandbox-escape suite, the native part; DESIGN 13.4). A test-owned off-screen window, the real
// WebView2 engine in a throwaway profile folder, and hostile pages mounted side by side as artifacts (each in a chat of its
// own, so each on a site of its own) or as inline widgets, with no pane, model or tools bound (each bridge call fails
// closed). Each page reports what it reached through its title, which nth reads like any title.
public sealed class SandboxEscapeNativeRig : IDisposable
{
    public const string Prefix = "nth-escape:";

    private readonly List<IDisposable> mounts = [];

    public SandboxEscapeNativeRig(WindowManagerNativeSession native, string surface, params string[] pages)
    {
        Folder = Path.Combine(Path.GetTempPath(), "nth-w26-native-" + Guid.NewGuid().ToString("N"));
        var threads = Path.Combine(Folder, "threads");
        Directory.CreateDirectory(threads);
        Surface = new WebSurface(new WebView2Engine(), new WebProfiles(Path.Combine(Folder, "WebView2"), "en-US"));
        var libraries = new ArtifactLibraryFiles(ArtifactLibraryFiles.DefaultFolder);
        var store = new ArtifactStore(() => threads);
        var runtime = new ArtifactRuntime(store, libraries);
        var row = new UniformGrid { Rows = 1 };
        for (var i = 0; i < pages.Length; i++)
        {
            WebSurfaceView view;
            string address;
            if (surface == "widget")
            {
                view = Surface.Open(WebSurfaceKind.InlineWidget, "w26-native");
                var widget = new InlineWidgetMount(view, "escape-" + i, pages[i], libraries);
                mounts.Add(widget);
                address = widget.Address;
            }
            else
            {
                var chat = Guid.NewGuid();
                var snapshot = store.Create(chat, new ArtifactDraft { Kind = ArtifactKind.Html, Title = "Escape " + i, Content = pages[i] }, new ArtifactAuthor("claude", "sonnet", null), null);
                view = Surface.Open(WebSurfaceKind.Artifact, "w26-native");
                var artifact = runtime.Mount(view, chat, snapshot.Artifact.Id);
                mounts.Add(artifact);
                address = artifact.Address;
            }

            var blocked = new List<string>();
            view.SandboxNavigationBlocked += (_, e) => blocked.Add(e.Url);
            Views.Add(view);
            Addresses.Add(address);
            Blocked.Add(blocked);
            row.Children.Add(new WebViewHost(view, null));
        }

        Window = new Avalonia.Controls.Window
        {
            Title = "W26 native fixture", Width = 800 * Math.Max(1, pages.Length), Height = 600, ShowInTaskbar = false, ShowActivated = false,
            Position = new PixelPoint(-32000, -32000), Content = row,
        };
        Window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(Window);
        Window.Show();
        foreach (var view in Views) view.SetShown(true);
    }

    public string Folder { get; }

    public WebSurface Surface { get; }

    public List<WebSurfaceView> Views { get; } = [];

    public List<string> Addresses { get; } = [];

    /// <summary>Per page: every navigation, frame or window nth cancelled, in order.</summary>
    public List<List<string>> Blocked { get; } = [];

    public Avalonia.Controls.Window Window { get; }

    /// <summary>Why the throwaway folder stayed behind after Dispose, if it did.</summary>
    public string? LeftBehind { get; private set; }

    /// <summary>A page's report (JSON) once it has set its title, else null.</summary>
    public string? Report(int page) =>
        Views[page].Engine?.Title is { } title && title.StartsWith(Prefix, StringComparison.Ordinal) ? title[Prefix.Length..] : null;

    public void Dispose()
    {
        foreach (var mount in mounts) mount.Dispose();
        foreach (var view in Views) Surface.Close(view);
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
