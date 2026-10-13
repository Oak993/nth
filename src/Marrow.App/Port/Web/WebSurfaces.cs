// PORT-ONLY: web surfaces W19 (DESIGN.md 2.1-2.3 "one WebSurface per process"; ED-NEW-web-w19-4). The process's one web engine
// layer, made on first use: WebView2 behind the engine seam, the profiles under nth's data folder in the app's language, its
// replies on the main actor, and its housekeeping tick once a second (discard, hang probes, prewarm). The first unit that
// needs a web view in the app makes it; every other surface (Visus, Tabula's pane) uses the same one. Tests swap the
// factory for the fake engine and the live content for a plain control (no WebView2 window in a headless run).
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Web;

namespace Marrow.App.Port.Web;

public static class WebSurfaces
{
    private static WebSurface? shared;
    private static DispatcherTimer? tick;

    /// <summary>Makes the process's surface; tests set a fake engine's.</summary>
    public static Func<WebSurface> Factory { get; set; } = static () =>
        new WebSurface(new WebView2Engine(), new WebProfiles(WebProfiles.DefaultUserDataFolder, CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-US"),
            post: MainActor.Post);

    /// <summary>Makes a view's live content; null is the real one (WebViewHost.CreateNativeView). Tests set a plain control.</summary>
    public static Func<IWebViewSurface, Control>? LiveFactory { get; set; }

    /// <summary>The process's surface, made on first use.</summary>
    public static WebSurface Shared
    {
        get
        {
            if (shared is not null) return shared;
            shared = Factory();
            if (Avalonia.Application.Current is not null)
            {
                tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => shared?.Tick());
                tick.Start();
            }

            return shared;
        }
    }

    /// <summary>A host for one of the surface's views, with the live content the process uses.</summary>
    public static WebViewHost Host(IWebViewSurface view) => new(view, LiveFactory);

    /// <summary>Tests: forgets the surface (and stops its tick) so the next use makes a new one.</summary>
    internal static void Reset()
    {
        tick?.Stop();
        tick = null;
        shared?.Dispose();
        shared = null;
    }
}
