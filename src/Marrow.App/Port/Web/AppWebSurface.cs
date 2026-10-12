using System.Globalization;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Services.Web;

// PORT-ONLY: web surfaces W14 (E:/Droppy/work/design/web-surfaces/DESIGN.md 2.1 "one WebView2 environment per process",
// 2.3 the lifecycle ticks, 3.6 reserved chords inside pages; ED-NEW-web-w14-1). No Swift counterpart. The process's one web
// surface, made the first time a pane needs a page: WebView2, nth's one user data folder, the UI language, the main actor
// for its events, the workspace's reserved chords bound, and its lifecycle tick (suspend, discard, pre-warm close, hang
// probes) four times a second while it has views, and not at all without them. Tabula is the first pane to need it; Visus
// (W8a) opens its tabs here too and adds the window-level parts (focus, window transitions). Off until Program.Main enables
// it, so a test never starts WebView2.

namespace Marrow.App.Port.Web;

public static class AppWebSurface
{
    /// <summary>How often the lifecycle runs while the surface has views (DESIGN 12: the 250 ms ticks of 2.3).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private static readonly object Gate = new();
    private static bool enabled;
    private static WebSurface? surface;
    private static IDisposable? shortcuts;
    private static DispatcherTimer? ticker;

    /// <summary>Program.Main turns the surface on; until then <see cref="Current"/> is null and previews show their code.</summary>
    public static void Enable() => enabled = true;

    /// <summary>The surface, made on first use; null while off.</summary>
    public static WebSurface? Current
    {
        get
        {
            if (!enabled) return null;
            lock (Gate)
            {
                if (surface is not null)
                {
                    // Asked for again: a view may open now, so the tick runs (it stops itself when no view is left).
                    if (ticker is { IsEnabled: false } idle) idle.Start();
                    return surface;
                }
                surface = new WebSurface(new WebView2Engine(), new WebProfiles(WebProfiles.DefaultUserDataFolder, CultureInfo.CurrentUICulture.Name), post: MainActor.Post);
                shortcuts = Marrow.App.Views.Panes.PaneShortcuts.Bind(surface);
                ticker = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => Tick());
                ticker.Start();
                return surface;
            }
        }
    }

    private static void Tick()
    {
        if (surface is not { } current) return;
        if (current.Views.Count > 0) current.Tick();
        else ticker?.Stop();
    }
}
