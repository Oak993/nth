namespace Marrow.Core.Web;

/// <summary>DESIGN 2.7: the failures a web view can show a card for.</summary>
public enum WebFailureKind
{
    /// <summary><c>ProcessFailed</c>, kind <c>RenderProcessExited</c> or <c>FrameRenderProcessExited</c>.</summary>
    RendererCrashed,

    /// <summary><c>ProcessFailed</c>, kind <c>RenderProcessUnresponsive</c>, or no <c>requestAnimationFrame</c> reply in 5 s.</summary>
    RendererHung,

    /// <summary><c>ProcessFailed</c>, kind <c>BrowserProcessExited</c>.</summary>
    BrowserGone,

    /// <summary><c>GetAvailableCoreWebView2BrowserVersionString</c> found no runtime.</summary>
    RuntimeMissing,

    /// <summary>The runtime is older than the minimum the SDK needs.</summary>
    RuntimeTooOld,
}

/// <summary>DESIGN 2.7: the buttons a failure card offers.</summary>
public enum WebFailureAction
{
    Reload,
    CloseTab,
    Wait,
    RestartVisus,
    InstallRuntime,
    UpdateRuntime,
}

/// <summary>One button on a failure card: its action and its label.</summary>
public sealed record WebFailureButton(WebFailureAction Action, string Title);

/// <summary>
/// DESIGN 2.7: a calm native card over the pane's last snapshot (dimmed). Crash cards are native, never web content,
/// and keep the pane header and trust frame (8.2). <see cref="ShowsCard"/> is false for the panes after the first when
/// the browser process is gone ("every web pane shows its snapshot and one card in the first"); those panes show their
/// dimmed snapshot only.
/// </summary>
public sealed record WebFailureCard(
    WebFailureKind Kind,
    string Title,
    string Body,
    IReadOnlyList<WebFailureButton> Buttons,
    bool ShowsCard,
    bool Dimmed)
{
    // DESIGN 2.7 strings. W22 moves every new string into the resource files.
    public const string CrashedTitle = "This page stopped working.";
    public const string CrashedBody = "Reload opens the page again. Anything typed into a form on it is gone.";
    public const string HungTitle = "This page is not responding.";
    public const string HungBody = "Wait for it, or reload it. Reloading loses anything typed into a form on it.";
    public const string BrowserGoneTitle = "Visus stopped.";
    public const string BrowserGoneBody = "Restart Visus to open your tabs again.";
    public const string RuntimeMissingTitle = "Visus needs the WebView2 runtime.";
    public const string RuntimeMissingBody = "It is a 2 MB download from Microsoft. nth installs it when you say so.";
    public const string RuntimeTooOldTitle = "Visus needs a newer WebView2 runtime.";
    public const string RuntimeTooOldBody = "Update it from Microsoft, then open Visus again.";
    public const string ReloadsStoppedBody = "It stopped working 3 times in 5 minutes, so nth no longer reloads it by itself.";

    public const string ReloadTitle = "Reload";
    public const string CloseTabTitle = "Close tab";
    public const string WaitTitle = "Wait";
    public const string RestartVisusTitle = "Restart Visus";
    public const string InstallTitle = "Install";
    public const string UpdateTitle = "Update";

    /// <summary>The card for <paramref name="kind"/> (DESIGN 2.7 table).</summary>
    /// <param name="kind">What failed.</param>
    /// <param name="showsCard">False for the panes after the first when the browser process is gone.</param>
    /// <param name="automaticReloadsStopped">The page crashed 3 times in 5 minutes: say that reloads stopped.</param>
    public static WebFailureCard For(WebFailureKind kind, bool showsCard = true, bool automaticReloadsStopped = false)
    {
        var card = kind switch
        {
            WebFailureKind.RendererCrashed => new WebFailureCard(kind, CrashedTitle, CrashedBody,
                [new(WebFailureAction.Reload, ReloadTitle), new(WebFailureAction.CloseTab, CloseTabTitle)], showsCard, Dimmed: true),
            WebFailureKind.RendererHung => new WebFailureCard(kind, HungTitle, HungBody,
                [new(WebFailureAction.Wait, WaitTitle), new(WebFailureAction.Reload, ReloadTitle)], showsCard, Dimmed: true),
            WebFailureKind.BrowserGone => new WebFailureCard(kind, BrowserGoneTitle, BrowserGoneBody,
                [new(WebFailureAction.RestartVisus, RestartVisusTitle)], showsCard, Dimmed: true),
            WebFailureKind.RuntimeMissing => new WebFailureCard(kind, RuntimeMissingTitle, RuntimeMissingBody,
                [new(WebFailureAction.InstallRuntime, InstallTitle)], showsCard, Dimmed: false),
            WebFailureKind.RuntimeTooOld => new WebFailureCard(kind, RuntimeTooOldTitle, RuntimeTooOldBody,
                [new(WebFailureAction.UpdateRuntime, UpdateTitle)], showsCard, Dimmed: false),
        };
        return automaticReloadsStopped && kind is WebFailureKind.RendererCrashed or WebFailureKind.RendererHung
            ? card with { Body = card.Body + " " + ReloadsStoppedBody }
            : card;
    }
}
