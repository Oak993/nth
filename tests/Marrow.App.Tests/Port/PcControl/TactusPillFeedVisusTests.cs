// PORT-ONLY: web surfaces W11 (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "The pill: 'Using Visus' with the step line,
// Pause and Stop. Stop is the Tactus chord ... and the pill button; it ends every web step at once"; ED-NEW-web-w11-1). With
// Tactus on, Visus is one more app of the pill: it joins through the pause control, its line is the web tools' latest step,
// and it waits on its own card while one shows in Visus.
using System.Text.Json.Nodes;
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Web;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class TactusPillFeedVisusTests
{
    private const string Thread = "chat-a";

    private double now = 10;

    private static async Task Search(WebAgentHub hub, string words) =>
        await hub.Engine(Thread)!.Call(WebToolSchemas.Open, new JsonObject { ["query"] = words }, new WebAgentCaller(Thread, "claude-opus-5-5", true), CancellationToken.None);

    [Fact]
    public async Task Visus_is_an_app_of_the_pill_with_the_web_steps_line_until_the_turn_ends()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        var hub = new WebAgentHub(() => assembly.Control, () => assembly.Pause);
        var asks = new VisusAgentAsks();
        hub.Bind(new NoWebTabs(), asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now, web: hub, webAsks: asks);

        await Search(hub, "team settings");
        var during = feed.Facts();
        hub.EndTurn(Thread);
        var after = feed.Facts();

        var visus = Assert.Single(during.Apps);
        Assert.Equal(WebAgentHub.App, visus.Key);
        Assert.Equal("Visus", visus.Name);
        Assert.Equal("Searching for team settings", visus.Step);
        Assert.Equal(PillAppPhase.Working, visus.Phase);
        Assert.Equal("The page stays as it is", visus.PausedLine);
        Assert.False(visus.OwnCard);
        Assert.Equal(WebAgentHub.App, during.LatestApp);
        Assert.Equal("Using Visus", PillStates.Compute(during, now).Title);
        Assert.Empty(after.Apps);
    }

    [Fact]
    public async Task Visus_waits_on_its_own_card_while_one_shows_and_the_pills_Pause_holds_it()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        var hub = new WebAgentHub(() => assembly.Control, () => assembly.Pause);
        var asks = new VisusAgentAsks();
        hub.Bind(new NoWebTabs(), asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now, web: hub, webAsks: asks);
        await Search(hub, "team settings");

        var card = asks.AskSite(new WebSiteAsk(Thread, "t1", "https://a.test", "Opus 5.5", Shared: false), CancellationToken.None);
        var asking = Assert.Single(feed.Facts().Apps);
        asks.Oldest!.Answer(VisusAgentChoice.Deny);
        await card;
        assembly.Pause.Pause(PcScope.App(WebAgentHub.App), PcPauseSource.Row);
        var paused = Assert.Single(feed.Facts().Apps);

        Assert.True(asking.OwnCard);
        Assert.True(asking.WaitsOnAsk);
        Assert.Equal("nth needs your OK", PillStates.Compute(new PillFacts { Apps = [asking] }, now).Title);
        Assert.True(hub.IsPaused);
        Assert.Equal(PillAppPhase.Paused, paused.Phase);
        Assert.False(paused.OwnCard);
    }
}
