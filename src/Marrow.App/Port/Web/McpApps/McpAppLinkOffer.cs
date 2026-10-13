// PORT-ONLY: web surfaces security review residual R2 (DESIGN.md 8.1 "A user click on a link inside an artifact is cancelled and
// offered to the link setting ... (the same list applies to MCP Apps and inline widgets)", 5.10 "the full URL with the query
// string expanded"; ED-NEW-web-sec-res-2). No Swift counterpart. A link the user clicked in an MCP App is offered where the app
// is: its card in the thread does it itself (InlineWebWidget, as for an inline widget); the App pane and the pop-out window
// each keep one of these under their trust strip.
using Avalonia;
using Marrow.App.Views.Artifacts;
using Marrow.App.Views.Chat;
using Marrow.Core.Localisation;

namespace Marrow.App.Port.Web.McpApps;

/// <summary>
/// ED-NEW-web-sec-res-2: Tabula's notice offering the link the user clicked in the app shown here: "Open link from the app", the
/// whole address in the mono face with no line cap, and Open; its close button dismisses the offer. Drawn again only when the
/// offered link or the app changes.
/// </summary>
internal sealed class McpAppLinkOffer
{
    private McpAppSession? session;
    private string? offered;

    public McpAppLinkOffer(Thickness margin)
    {
        Notice = new TabulaNotice { IsVisible = false, Margin = margin };
        Notice.Dismissed += () => session?.DismissLink();
    }

    public TabulaNotice Notice { get; }

    /// <summary>Shows what the app here offers now; null (no app here) hides the notice.</summary>
    public void Show(McpAppSession? shown)
    {
        var link = shown?.PendingLink;
        if (shown == session && link == offered && Notice.IsVisible == link is not null) return;
        session = shown;
        offered = link;
        if (shown is null || link is null) Notice.Hide();
        else Notice.ShowAddress(InlineWebWidget.AppOpenLinkTitle, link, (UiText.Get("tabula.notice.open"), shown.OpenLink));
    }
}
