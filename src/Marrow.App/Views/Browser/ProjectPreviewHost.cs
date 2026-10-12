using Marrow.App.Runtime;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.6 and 5.8; ED-NEW-web-w9-3. W10 can bind the approved settings through these hooks.
public static class ProjectPreviewHost
{
    public static Func<bool> OpenAutomatically { get; set; } = () => true;
    public static Func<DevicePreset> DefaultDevice { get; set; } = () => DevicePreset.Phone;
    public static Func<bool> ReloadOnSave { get; set; } = () => true;
    public static bool CanOpen(AppModel app, Project project, ThreadRuntime runtime) => app.Terminals.Sessions(runtime.ThreadID).Any(s => s.PreviewUrl is not null)
        || File.Exists(Path.Combine(app.Thread(runtime.ThreadID)?.WorktreePath ?? project.Path, "index.html"));
    public static void Found(AppModel app, TerminalSession terminal)
    {
        if (terminal.PreviewUrl is null) return;
        terminal.OpenPreview = () => Open(app.Runtime(terminal.ThreadID), terminal.PreviewUrl);
        if (OpenAutomatically()) terminal.OpenPreview();
    }
    public static void Open(AppModel app, Project project, ThreadRuntime runtime)
    {
        var address = app.Terminals.Sessions(runtime.ThreadID).Select(s => s.PreviewUrl).FirstOrDefault(s => s is not null);
        if (address is not null) { Open(runtime, address); return; }
        var root = app.Thread(runtime.ThreadID)?.WorktreePath ?? project.Path;
        if (!File.Exists(Path.Combine(root, "index.html"))) return;
        var site = new StaticPreviewSite(project.Id.ToString("N"), root);
        var tab = Open(runtime, site.Origin + "/"); tab.Preview?.Mount(site);
    }
    public static VisusTab Open(ThreadRuntime runtime, string address)
    {
        var session = VisusHost.SessionFor(runtime);
        var tab = session.Tabs.FirstOrDefault(t => t.Kind == VisusTabKind.Preview && t.Url == address);
        if (tab is null) tab = session.NewTab(VisusTabKind.Preview, address); else session.Select(tab);
        runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser);
        return tab;
    }
    internal static void RestoreSite(VisusTab tab, ThreadRuntime? runtime)
    {
        if (runtime?.App is not { } app || app.Thread(runtime.ThreadID) is not { } thread || app.Project(thread.ProjectID) is not { } project) return;
        var site = new StaticPreviewSite(project.Id.ToString("N"), thread.WorktreePath ?? project.Path);
        if (tab.Url.StartsWith(site.Origin + "/", StringComparison.OrdinalIgnoreCase)) tab.Preview?.Mount(site);
    }
    internal static IEnumerable<VisusMenuEntry> Menu(VisusSession session, VisusTab? tab)
    {
        if (tab?.Kind == VisusTabKind.Preview)
        {
            yield return new VisusMenuEntry("Pick element", () => _ = tab.Preview?.Picker?.SetEnabledAsync(true), "Ctrl+Shift+E");
            yield return new VisusMenuEntry("Open DevTools", () => (tab.View?.Engine as IPreviewViewport)?.OpenPreviewDevTools());
        }
    }
}
