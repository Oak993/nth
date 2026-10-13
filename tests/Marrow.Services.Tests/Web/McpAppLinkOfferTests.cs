using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review residual R2 (ED-NEW-web-sec-res-2; DESIGN.md 8.1 "A user click on a link inside an
// artifact is cancelled and offered to the link setting ... (the same list applies to MCP Apps and inline widgets)", 5.10 "the
// full URL with the query string expanded"). An app's link the user clicked in its view went straight to the link setting, so
// an app could carry what it holds in the link's query and send it with one click the user never saw the address of. The click
// now only offers the link (McpAppMount.PendingLink, shown on the app's card with its whole address), and only the user's Open
// opens exactly that address. A script-started navigation, a script-started window and a link that is not a web address offer
// nothing; a newer click replaces the offer; a dismissed offer opens nothing.
[Trait("Decision", "ED-NEW-web-sec-res-2")]
public sealed class McpAppLinkOfferTests
{
    private const string Address = "https://evil.example/collect?board=v2.4.1&note=salary%2090k#k=secret";

    [Fact]
    public void A_click_in_an_app_only_offers_its_link_and_Open_opens_exactly_that_address_once()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        var (mount, view) = rig.Open();
        rig.Handshake();
        var changes = 0;
        mount.LinkChanged += () => changes++;

        view.RaiseNavigationStarting(Address, userInitiated: true);

        Assert.Empty(rig.Ui.Links);
        Assert.Empty(rig.Ui.LinkCards); // the offer is on the app's own card, not the ui/open-link card
        Assert.Equal(Address, mount.PendingLink);
        Assert.Equal(1, changes);

        mount.OpenPendingLink();
        Assert.Equal([Address], rig.Ui.Links);
        Assert.Null(mount.PendingLink);
        Assert.Equal(2, changes);

        mount.OpenPendingLink(); // nothing is left to open
        Assert.Equal([Address], rig.Ui.Links);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_click_that_opens_a_window_is_offered_too_and_a_newer_click_replaces_the_offer()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        var (mount, view) = rig.Open();
        rig.Handshake();

        view.RaiseNewWindow("https://shipyard.dev/a?x=1", userInitiated: true);
        Assert.Equal("https://shipyard.dev/a?x=1", mount.PendingLink);
        view.RaiseNavigationStarting("https://shipyard.dev/b?y=2", userInitiated: true, mainFrame: false);
        Assert.Equal("https://shipyard.dev/b?y=2", mount.PendingLink);
        Assert.Empty(rig.Ui.Links);

        mount.OpenPendingLink();
        Assert.Equal(["https://shipyard.dev/b?y=2"], rig.Ui.Links);
    }

    [Fact]
    public void A_script_started_navigation_a_plain_scheme_or_a_dismissed_offer_opens_nothing()
    {
        using var rig = new McpAppsRig(csp: McpUiCsp.None);
        var (mount, view) = rig.Open();
        rig.Handshake();
        var changes = 0;
        mount.LinkChanged += () => changes++;

        view.RaiseNavigationStarting("https://tracker.example/pixel?d=board-secrets", userInitiated: false);
        view.RaiseNewWindow("https://tracker.example/window?d=board-secrets", userInitiated: false);
        view.RaiseNavigationStarting("ms-settings:privacy", userInitiated: true);
        view.RaiseNavigationStarting("file:///C:/Windows/win.ini", userInitiated: true);
        Assert.Null(mount.PendingLink);
        Assert.Equal(0, changes);

        view.RaiseNavigationStarting(Address, userInitiated: true);
        Assert.Equal(Address, mount.PendingLink);
        mount.DismissPendingLink();
        Assert.Null(mount.PendingLink);
        Assert.Equal(2, changes);
        mount.OpenPendingLink();
        mount.DismissPendingLink();

        Assert.Empty(rig.Ui.Links);
        Assert.Equal(2, changes);
    }
}
