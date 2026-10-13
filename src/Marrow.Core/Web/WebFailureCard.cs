using Marrow.Core.Localisation;

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

    /// <summary>W23: the user said yes to the runtime install (or update) and nth is getting it from Microsoft.</summary>
    RuntimeInstalling,

    /// <summary>W23: the runtime install (or update) did not finish; the card says why and offers another go.</summary>
    RuntimeInstallFailed,
}

/// <summary>W23: which failure kinds belong to the runtime flow (the offer, the update offer, the work, its failure).</summary>
public static class WebFailureKinds
{
    /// <summary>
    /// True for the cards that stand in for a page because the WebView2 runtime is not usable: the pane keeps no live
    /// content under them, a restart of Visus leaves them alone, and a runtime that turns up replaces them.
    /// </summary>
    public static bool IsRuntime(this WebFailureKind kind) =>
        kind is WebFailureKind.RuntimeMissing or WebFailureKind.RuntimeTooOld
            or WebFailureKind.RuntimeInstalling or WebFailureKind.RuntimeInstallFailed;
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

    /// <summary>W23: opens Microsoft's WebView2 Runtime page in the default browser (the install card's second way out).</summary>
    OpenRuntimePage,
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
    // DESIGN 2.7 strings, read from the resource files (Localisation/Strings/failure-en.json; W22, ED-NEW-web-w22-2).
    public static string CrashedTitle => UiText.Get("failure.crashed_title");
    public static string CrashedBody => UiText.Get("failure.crashed_body");
    public static string HungTitle => UiText.Get("failure.hung_title");
    public static string HungBody => UiText.Get("failure.hung_body");
    public static string BrowserGoneTitle => UiText.Get("failure.browser_gone_title");
    public static string BrowserGoneBody => UiText.Get("failure.browser_gone_body");
    public static string RuntimeMissingTitle => UiText.Get("failure.runtime_missing_title");
    public static string RuntimeMissingBody => UiText.Get("failure.runtime_missing_body");
    public static string RuntimeTooOldTitle => UiText.Get("failure.runtime_too_old_title");
    public static string RuntimeTooOldBody => UiText.Get("failure.runtime_too_old_body");
    public static string RuntimeInstallingTitle => UiText.Get("failure.runtime_installing_title");
    public static string RuntimeUpdatingTitle => UiText.Get("failure.runtime_updating_title");
    public static string RuntimeInstallingBody => UiText.Get("failure.runtime_installing_body");
    public static string RuntimeInstallFailedTitle => UiText.Get("failure.runtime_install_failed_title");
    public static string RuntimeUpdateFailedTitle => UiText.Get("failure.runtime_update_failed_title");
    public static string ReloadsStoppedBody => UiText.Get("failure.reloads_stopped_body");

    public static string ReloadTitle => UiText.Get("failure.reload_title");
    public static string CloseTabTitle => UiText.Get("failure.close_tab_title");
    public static string WaitTitle => UiText.Get("failure.wait_title");
    public static string RestartVisusTitle => UiText.Get("failure.restart_visus_title");
    public static string InstallTitle => UiText.Get("failure.install_title");
    public static string UpdateTitle => UiText.Get("failure.update_title");
    public static string TryAgainTitle => UiText.Get("failure.try_again_title");
    public static string OpenRuntimePageTitle => UiText.Get("failure.open_runtime_page_title");

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
            WebFailureKind.RuntimeInstalling => RuntimeInstalling(updating: false, showsCard),
            WebFailureKind.RuntimeInstallFailed => throw new ArgumentException("The install-failed card says why: use RuntimeInstallFailed(updating, reason).", nameof(kind)),
        };
        return automaticReloadsStopped && kind is WebFailureKind.RendererCrashed or WebFailureKind.RendererHung
            ? card with { Body = card.Body + " " + ReloadsStoppedBody }
            : card;
    }

    /// <summary>
    /// W23: the card while the runtime flow is running (nth was asked to install or update, and is downloading and running
    /// Microsoft's bootstrapper). No buttons: the work cannot be cancelled once the installer has started, and it ends in
    /// the pane's page or in <see cref="RuntimeInstallFailed"/>. Not dimmed, like the offer it replaces.
    /// </summary>
    public static WebFailureCard RuntimeInstalling(bool updating, bool showsCard = true) =>
        new(WebFailureKind.RuntimeInstalling, updating ? RuntimeUpdatingTitle : RuntimeInstallingTitle, RuntimeInstallingBody,
            [], showsCard, Dimmed: false);

    /// <summary>
    /// W23: the card when the flow did not end with a usable runtime. <paramref name="reason"/> is one calm sentence
    /// (<see cref="WebRuntimeBootstrapper.ReasonFor"/>); the buttons are another go and Microsoft's own page.
    /// </summary>
    public static WebFailureCard RuntimeInstallFailed(bool updating, string reason, bool showsCard = true) =>
        new(WebFailureKind.RuntimeInstallFailed, updating ? RuntimeUpdateFailedTitle : RuntimeInstallFailedTitle, reason,
            [new(updating ? WebFailureAction.UpdateRuntime : WebFailureAction.InstallRuntime, TryAgainTitle),
             new(WebFailureAction.OpenRuntimePage, OpenRuntimePageTitle)], showsCard, Dimmed: false);
}
