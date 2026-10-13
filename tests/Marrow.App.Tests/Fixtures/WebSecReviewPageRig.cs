using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Window.Native;

/// <summary>
/// WebSecReviewNativeTests' rig (ED-NEW-web-sec-rev-3, ED-NEW-web-sec-rev-4): a test-owned window with one Visus tab on the real
/// WebView2 engine and a throwaway profile folder, whose every request receives the given page from memory (no network, no
/// account). The page reports through its title: <see cref="Report"/> is the title after <see cref="Prefix"/>, or null.
/// </summary>
internal sealed class WebSecReviewPageRig : IDisposable
{
    public const string Prefix = "nth-sec-rev:";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "nth-sec-rev-native-" + Guid.NewGuid().ToString("N"));
    private readonly VisusSession session;
    private readonly VisusTab tab;
    private readonly Avalonia.Controls.Window window;

    public WebSecReviewPageRig(WindowManagerNativeSession native, byte[] page)
    {
        VisusHost.ResetForTests();
        VisusHost.DataFolder = null;
        VisusHost.SurfaceFactory = () => new WebSurface(new WebView2Engine(), new WebProfiles(folder, "en-US"));
        session = new VisusSession();
        tab = session.NewTab(VisusTabKind.Browse, "https://nth-sec-rev.invalid/page");
        tab.View!.Engine!.SetResourceHandler(new WebSecReviewPage(page));
        window = new Avalonia.Controls.Window
        {
            Title = "Security review native fixture", Width = 800, Height = 600,
            Content = new VisusPaneBody(session), ShowInTaskbar = false,
        };
        window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(window);
        window.Show();
    }

    public string? Report =>
        tab.View?.Engine?.Title is { } title && title.StartsWith(Prefix, StringComparison.Ordinal) ? title[Prefix.Length..] : null;

    public void Dispose()
    {
        window.Close();
        session.Dispose();
        VisusHost.ResetForTests();
        try { Directory.Delete(folder, true); }
        catch (IOException) { } // The test-owned browser process may still be releasing its profile.
    }
}

/// <summary>Answers every request of <see cref="WebSecReviewPageRig"/>'s tab with one HTML page, never cached.</summary>
internal sealed class WebSecReviewPage(byte[] body) : IWebResourceHandler
{
    public WebResourceResponse Answer(WebResourceRequest request) => new(200, "OK",
        [new("Content-Type", "text/html; charset=utf-8"), new("Cache-Control", "no-store")], body);
}
