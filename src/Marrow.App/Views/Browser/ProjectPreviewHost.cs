using Marrow.App.Runtime;
using Marrow.App.Views.Terminal;
using Marrow.Core.Localisation;
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
    /// <summary>
    /// W10 (DESIGN 5.11 "HTML files get 'Open in Visus'", 5.2 "which serves it through the preview origin, never through file:"): the address of an HTML
    /// file a chat linked to, on the project's preview site, or null when it may not be offered. The file must be a full local path strictly inside the
    /// project's folder (case folded, dot segments resolved, no UNC or device form), an existing .html or .htm file, with no hidden folder or file on
    /// the way and no link or junction anywhere under the folder; nothing outside the project is ever served.
    /// </summary>
    public static string? HtmlFileAddress(StaticPreviewSite site, string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return null;
        if (!(path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(site.Root));
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            var segments = Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar);
            if (segments.Any(segment => segment.Length == 0 || segment.StartsWith('.') || segment.Contains(':', StringComparison.Ordinal))) return null;
            // ED-NEW-secfinal-21: the rule above reads the text of the path; Windows also opens a folder or file through its 8.3 alias (GIT~1 is .git).
            // The path is resolved to its real long names, and the same hidden-name rule runs on those; an alias is not offered.
            if (site.RealSegments(full) is not { } real || real.Count != segments.Length
                || real.Where((segment, index) => segment.StartsWith('.') || !string.Equals(segment, segments[index], StringComparison.OrdinalIgnoreCase)).Any()) return null;
            var current = root;
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return null;
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current)) return null;
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return null;
            }
            return !File.Exists(full) ? null : site.Origin + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>W10: the project folder a chat's previews are served from (its worktree, else the project's folder), or null.</summary>
    public static StaticPreviewSite? SiteFor(AppModel app, ThreadRuntime runtime)
    {
        if (app.Thread(runtime.ThreadID) is not { } thread || app.Project(thread.ProjectID) is not { } project) return null;
        return new StaticPreviewSite(project.Id.ToString("N"), thread.WorktreePath ?? project.Path);
    }

    /// <summary>W10: opens an HTML file of the project in a preview tab (the address <see cref="HtmlFileAddress"/> gave). Null when it may not be.</summary>
    public static VisusTab? OpenFile(AppModel app, ThreadRuntime runtime, string path)
    {
        if (SiteFor(app, runtime) is not { } site || HtmlFileAddress(site, path) is not { } address) return null;
        var tab = Open(runtime, address);
        tab.Preview?.Mount(site);
        return tab;
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
            yield return new VisusMenuEntry(UiText.Get("visus.preview.pick_element"), () => _ = tab.Preview?.Picker?.SetEnabledAsync(true), "Ctrl+Shift+E");
            yield return new VisusMenuEntry(UiText.Get("visus.preview.open_devtools"), () => (tab.View?.Engine as IPreviewViewport)?.OpenPreviewDevTools());
        }
    }
}
