// PORT-ONLY: web surfaces W19, the host of an html_widget (DESIGN.md 7.3 "html_widget {html, height?}: an inline HTML widget.
// Owned by W24 (tool) and W19 (host)"; 8.2 "Widget · from this answer"; ED-NEW-web-w19-3). No Swift counterpart. One widget,
// one sandboxed view on its own w-<id>.invalid site, for as long as the process runs: rows of a thread are drawn again, and the
// card asks for the widget by its id rather than owning it. W24 makes the card with InlineWebWidget.Html(...).
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Web;

namespace Marrow.App.Port.Web.McpApps;

public sealed class InlineWidgetSession
{
    /// <summary>The height before the widget says how tall it is (or the tool's height? when given).</summary>
    public const double DefaultHeight = 240;

    private static readonly Dictionary<string, InlineWidgetSession> Sessions = new(StringComparer.Ordinal);

    private InlineWidgetSession(string id, string html, double? height, WebSurface surface, ArtifactLibraryFiles libraries)
    {
        Id = id;
        DeclaredHeight = height is { } h && double.IsFinite(h) && h > 0 ? h : null;
        View = surface.Open(WebSurfaceKind.InlineWidget, "main");
        Host = WebSurfaces.Host(View);
        Mount = new InlineWidgetMount(View, id, html, libraries, Theme, MainActor.Post);
        Mount.SizeRequested += _ => Changed?.Invoke();
        Mount.LinkRequested += url =>
        {
            if (SwiftURL.Create(url) is { } link) InlineText.OpenUrl?.Invoke(link);
        };
        if (Avalonia.Application.Current is { } application)
            application.ActualThemeVariantChanged += (_, _) => Mount.PushTheme();
    }

    public string Id { get; }

    /// <summary>The height the tool asked for, if any.</summary>
    public double? DeclaredHeight { get; }

    public WebSurfaceView View { get; }

    public WebViewHost Host { get; }

    public InlineWidgetMount Mount { get; }

    /// <summary>The widget asked for another height.</summary>
    public event Action? Changed;

    /// <summary>The height the widget asked for, else the tool's, before the thread clamps it.</summary>
    public double? RequestedHeight => Mount.RequestedHeight ?? DeclaredHeight;

    /// <summary>The inline height for a thread this tall.</summary>
    public double InlineHeight(double threadHeight) =>
        InlineWebLive.Height(RequestedHeight, threadHeight > 0 ? threadHeight : 900, DefaultHeight, McpAppSession.MinimumInlineHeight);

    /// <summary>The widget of an id, made on first use (the same widget for every row that draws it).</summary>
    public static InlineWidgetSession For(string id, string html, double? height = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (Sessions.TryGetValue(id, out var session)) return session;
        var libraries = ArtifactRuntime.Shared?.Libraries ?? new ArtifactLibraryFiles(ArtifactLibraryFiles.DefaultFolder);
        session = new InlineWidgetSession(id, html, height, WebSurfaces.Shared, libraries);
        Sessions[id] = session;
        return session;
    }

    public static InlineWidgetSession? Find(string id) => Sessions.GetValueOrDefault(id);

    private static ArtifactTheme Theme()
    {
        var dark = Avalonia.Application.Current is { } application && LookDark(application);
        return new ArtifactTheme(dark, McpAppStyles.Variables(dark));
    }

    private static bool LookDark(Avalonia.Application application) => PlainControls.IsDark(application.ActualThemeVariant);

    /// <summary>Tests: forgets every widget.</summary>
    internal static void Reset() => Sessions.Clear();
}
