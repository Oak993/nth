using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Markdown;
using Marrow.UI.NthUi;
using Marrow.UI.Web;

// PORT-ONLY: web surfaces W14, Tabula's preview (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.1 the kinds, 6.4 "Markdown
// renders with nth's own MarkdownView natively (no web view)", "HTML and React update the preview at most every 250 ms by
// morphing the DOM", 6.5 the sandbox, 2.3 the lifecycle, 2.4 airspace; ED-NEW-web-w14-1). No Swift counterpart. Markdown,
// nth-ui and code draw natively; HTML, React, SVG, Mermaid and projects run in a sandboxed page on the artifact's own site
// (ArtifactRuntime.Mount) inside a WebViewHost, which swaps the page for its snapshot during gestures. The page is opened
// when the preview is on screen, is marked hidden when another view or the gallery covers it, and is closed 3 s after the
// pane leaves the window (a pane moved between slots comes back to the same page). Without the web runtime a page kind
// shows its code.

namespace Marrow.App.Views.Artifacts;

/// <summary>What the preview draws.</summary>
public enum TabulaPreviewMode
{
    None,
    Markdown,
    NthUi,
    Code,
    Web,
}

public sealed class TabulaPreview : Panel
{
    /// <summary>A page whose pane left the window is closed after this long (a move between slots keeps it).</summary>
    public static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(3);

    /// <summary>The window id pages open under until Visus (W8a) gives each window its own.</summary>
    public const string WindowId = "main";

    private readonly TabulaContext context;
    private readonly ScrollViewer native;
    private readonly TabulaCodeView code = new() { IsVisible = false };
    private WebSurface? surface;
    private WebSurfaceView? view;
    private ArtifactMount? mount;
    private WebViewHost? host;
    private ArtifactAddress? wanted;
    private int? wantedVersion;
    private string? nativeKey;
    private NthUiView? nthUi;
    private string nthUiText = "";
    private bool active = true;
    private bool attached;
    private DispatcherTimer? closing;

    public TabulaPreview(TabulaContext context)
    {
        this.context = context;
        native = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsVisible = false,
        };
        Children.Add(native);
        Children.Add(code);
        AutomationProperties.SetName(this, "Preview");
    }

    public TabulaPreviewMode Mode { get; private set; }

    /// <summary>A page kind is shown as code because there is no web runtime here.</summary>
    public bool IsFallback { get; private set; }

    public WebSurfaceView? View => view;

    public ArtifactMount? Mount => mount;

    public WebViewHost? Host => host;

    /// <summary>The native rendering (Markdown, nth-ui), or null.</summary>
    public Control? Native => native.IsVisible ? native.Content as Control : null;

    internal TabulaCodeView Code => code;

    /// <summary>The page reported an error.</summary>
    public event Action<ArtifactRuntimeError>? ErrorReported;

    /// <summary>A link the user clicked left the artifact (the address).</summary>
    public event Action<string>? LinkRequested;

    /// <summary>The page asked for a library nth has no offline copy of.</summary>
    public event Action<string>? LibraryMissing;

    public event Action<ArtifactModelStatus>? ModelStatusChanged;

    public event Action<ArtifactCompletionResult>? ModelUsed;

    /// <summary>The page loaded.</summary>
    public event Action? Ready;

    /// <summary>Shows a version (null follows the latest).</summary>
    public void Show(ArtifactSnapshot snapshot, int? version)
    {
        var artifact = snapshot.Artifact;
        switch (artifact.Kind)
        {
            case ArtifactKind.Markdown:
                ShowMarkdown(snapshot.Text ?? "", streaming: false, key: Key(snapshot));
                return;
            case ArtifactKind.NthUi:
                ShowNthUi(snapshot.Text ?? "", streaming: false, key: Key(snapshot));
                return;
            case ArtifactKind.Code:
                ShowCode(snapshot.Text ?? "", artifact.Language, fallback: false);
                return;
        }

        if (context.Runtime is null || (surface ??= context.Surface()) is null)
        {
            var (text, language) = Source(snapshot);
            ShowCode(text, language, fallback: true);
            return;
        }

        CloseNative();
        code.IsVisible = false;
        IsFallback = false;
        Mode = TabulaPreviewMode.Web;
        var address = new ArtifactAddress(artifact.Thread, artifact.Id);
        if (mount is not null && mount.Artifact != address) CloseWeb();
        wanted = address;
        wantedVersion = version;
        if (mount is null) OpenWeb();
        else if (mount.Version != version) mount.Show(version);
        Apply();
    }

    /// <summary>The page loads its version again (the latest moved on while it is followed).</summary>
    public void Reload() => mount?.Reload();

    /// <summary>
    /// Text being written (6.4): Markdown and nth-ui render it as it arrives, code shows it, and a page kind morphs the
    /// shown page into it once the text has a body. False when this kind cannot show it (the view keeps its skeleton).
    /// </summary>
    public bool ShowStream(ArtifactKind kind, string text, string? language)
    {
        switch (kind)
        {
            case ArtifactKind.Markdown:
                ShowMarkdown(text, streaming: true, key: null);
                return true;
            case ArtifactKind.NthUi:
                ShowNthUi(text, streaming: true, key: null);
                return true;
            case ArtifactKind.Code:
                ShowCode(text, language, fallback: false);
                return true;
            case ArtifactKind.Html when mount is not null && HasBody(text):
                mount.Morph(text);
                return true;
            default:
                return false;
        }
    }

    internal static bool HasBody(string html) => html.Contains("<body", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nothing shown (the empty pane or a skeleton).</summary>
    public void Clear()
    {
        CloseNative();
        code.IsVisible = false;
        CloseWeb();
        wanted = null;
        Mode = TabulaPreviewMode.None;
        IsFallback = false;
    }

    /// <summary>The preview is on screen (false while Code, Diff, the gallery or a skeleton covers it).</summary>
    public void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        Apply();
    }

    public bool IsActive => active;

    /// <summary>The text the user chose in a Markdown or code preview, or null.</summary>
    public string? SelectedText()
    {
        if (Mode == TabulaPreviewMode.Code) return code.SelectedText;
        if (Mode != TabulaPreviewMode.Markdown || native.Content is not Control root) return null;
        return root.GetLogicalDescendants().OfType<SelectableTextBlock>().Select(b => b.SelectedText).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
    }

    /// <summary>The preview as a PNG: the page's own capture, or the native view drawn at the screen's scale.</summary>
    public async Task<byte[]?> CaptureAsync(CancellationToken cancellationToken)
    {
        if (Mode == TabulaPreviewMode.Web)
            return view?.Engine is { } engine && await engine.CaptureSnapshotAsync(cancellationToken).ConfigureAwait(true) is { } frame ? frame.Png : null;
        var target = Mode is TabulaPreviewMode.Code ? code : native.Content as Control;
        return target is null ? null : Render(target, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }

    /// <summary>Draws a native view into a PNG (its whole height, not just what scrolls into view).</summary>
    public static byte[]? Render(Control target, double scale)
    {
        var size = target.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return null;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(target);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private static string Key(ArtifactSnapshot snapshot) =>
        snapshot.Artifact.Thread.ToString("N") + "/" + snapshot.Artifact.Id + "/" + snapshot.Version.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A page kind's code: its text, or a project's entry file.</summary>
    internal static (string Text, string? Language) Source(ArtifactSnapshot snapshot)
    {
        if (snapshot.Files is { } files)
        {
            var entry = snapshot.Version.Entry ?? ArtifactStore.DefaultEntry;
            return files.TryGetValue(entry, out var page) ? (page, CodeHighlight.LanguageOfPath(entry)) : (files.Values.FirstOrDefault() ?? "", null);
        }
        return (snapshot.Text ?? "", LanguageOf(snapshot.Artifact.Kind, snapshot.Artifact.Language));
    }

    /// <summary>The highlighter's language of a kind (a code artifact names its own).</summary>
    internal static string? LanguageOf(ArtifactKind kind, string? language) => kind switch
    {
        ArtifactKind.Html => "html",
        ArtifactKind.React => language is "tsx" ? "tsx" : "jsx",
        ArtifactKind.Svg => "svg",
        ArtifactKind.Mermaid => "mermaid",
        ArtifactKind.Markdown => "markdown",
        ArtifactKind.Code => language,
        _ => null,
    };

    private void ShowMarkdown(string text, bool streaming, string? key)
    {
        PrepareNative(TabulaPreviewMode.Markdown);
        if (key is not null && key == nativeKey) return;
        nativeKey = key;
        native.Content = new MarkdownView(text, isStreaming: streaming) { Margin = new Thickness(22, 18, 22, 22), MaxWidth = 720 };
    }

    private void ShowNthUi(string text, bool streaming, string? key)
    {
        PrepareNative(TabulaPreviewMode.NthUi);
        if (key is not null && key == nativeKey) return;
        if (streaming && nthUi is not null && nativeKey is null && text.StartsWith(nthUiText, StringComparison.Ordinal))
        {
            // The text so far grew: only the new part is parsed, as the chat's own live answers do.
            if (text.Length > nthUiText.Length) nthUi.Append(text[nthUiText.Length..]);
            nthUiText = text;
            return;
        }
        nativeKey = key;
        if (streaming)
        {
            nthUi = new NthUiView();
            nthUi.Append(text);
        }
        else nthUi = NthUiView.Show(text);
        nthUiText = text;
        nthUi.Margin = new Thickness(16);
        native.Content = nthUi;
    }

    private void ShowCode(string text, string? language, bool fallback)
    {
        CloseNative();
        CloseWeb();
        Mode = TabulaPreviewMode.Code;
        IsFallback = fallback;
        code.IsVisible = true;
        code.Show(text, language);
    }

    private void PrepareNative(TabulaPreviewMode mode)
    {
        CloseWeb();
        code.IsVisible = false;
        IsFallback = false;
        if (Mode != mode)
        {
            nativeKey = null;
            nthUi = null;
            nthUiText = "";
            native.Content = null;
        }
        Mode = mode;
        native.IsVisible = true;
    }

    private void CloseNative()
    {
        native.IsVisible = false;
        native.Content = null;
        nativeKey = null;
        nthUi = null;
        nthUiText = "";
    }

    // ---- the page ----

    private void OpenWeb()
    {
        if (!attached || wanted is not { } address || context.Runtime is not { } runtime || surface is null || mount is not null) return;
        view = surface.Open(WebSurfaceKind.Artifact, WindowId);
        mount = runtime.Mount(view, address.Thread, address.Id, wantedVersion);
        mount.ErrorReported += OnError;
        mount.LinkRequested += OnLink;
        mount.LibraryMissing += OnMissing;
        mount.ModelStatusChanged += OnModelStatus;
        mount.ModelUsed += OnModelUsed;
        mount.Ready += OnReady;
        host = new WebViewHost(view, context.LiveFactory);
        Children.Insert(0, host);
    }

    private void CloseWeb()
    {
        closing?.Stop();
        closing = null;
        if (mount is { } current)
        {
            current.ErrorReported -= OnError;
            current.LinkRequested -= OnLink;
            current.LibraryMissing -= OnMissing;
            current.ModelStatusChanged -= OnModelStatus;
            current.ModelUsed -= OnModelUsed;
            current.Ready -= OnReady;
            current.Dispose();
        }
        if (host is not null) Children.Remove(host);
        if (view is not null) surface?.Close(view);
        mount = null;
        host = null;
        view = null;
    }

    private void Apply()
    {
        var shown = attached && active && Mode == TabulaPreviewMode.Web;
        if (host is not null) host.IsVisible = shown;
        view?.SetShown(shown);
    }

    private void OnError(ArtifactRuntimeError error) => ErrorReported?.Invoke(error);

    private void OnLink(string url) => LinkRequested?.Invoke(url);

    private void OnMissing(string url) => LibraryMissing?.Invoke(url);

    private void OnModelStatus(ArtifactModelStatus status) => ModelStatusChanged?.Invoke(status);

    private void OnModelUsed(ArtifactCompletionResult result) => ModelUsed?.Invoke(result);

    private void OnReady(string title) => Ready?.Invoke();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        closing?.Stop();
        closing = null;
        if (Mode == TabulaPreviewMode.Web && mount is null) OpenWeb();
        Apply();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        attached = false;
        Apply();
        if (mount is null) return;
        closing = new DispatcherTimer(CloseDelay, DispatcherPriority.Background, (_, _) =>
        {
            closing?.Stop();
            closing = null;
            if (attached) return;
            var keep = wanted;
            var version = wantedVersion;
            CloseWeb();
            wanted = keep;
            wantedVersion = version;
        });
        closing.Start();
    }

    /// <summary>Closes the page now (the pane's content went away for good).</summary>
    internal void CloseNow() => CloseWeb();
}
