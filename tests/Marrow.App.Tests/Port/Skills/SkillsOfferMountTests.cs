using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.App.Views.Composer;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-14 (DL-18, DL-19). A chat that has an offer flow draws its card in the composer area, above the group and below the approval cards, and AppModel makes one flow per chat once the skills host is bound.
// ENGINEERING DECISION (ED-NEW-cloud-sk-14): the card is above the composer, not inline in the reply, because Offer A's page is not in the repository's design sources.
[Collection("Headless")]
public sealed class SkillsOfferMountTests(HeadlessSession headless)
{
    private static void LoadLogin() => System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();

    private readonly List<Avalonia.Controls.Window> windows = [];

    private static void Settle(Avalonia.Controls.Window window)
    {
        for (var pass = 0; pass < 6; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    private ComposerArea Mount(ThreadRuntimePart12Harness h, bool withFlow, out SkillsOfferFlow? flow)
    {
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        SkillsOfferFlow? made = null;
        var area = h.Main.OnMain(() =>
        {
            if (withFlow) made = new SkillsOfferFlow(h.Runtime, SkillsCatalogueLoader.LoadEmbedded(), () => AddonsState.Empty, _ => { }, new SkillsOfferDownloadFake());
            var area = new ComposerArea(h.Runtime, workingDirectory: null);
            AppEnvironment.SetAppModel(area, h.App);
            return area;
        });
        flow = made;
        var window = new Avalonia.Controls.Window { Content = new Avalonia.Controls.Border { Child = area, Width = 700 }, Width = 800, Height = 400 };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        windows.Add(window);
        return area;
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_chat_with_an_offer_flow_draws_its_card_in_the_composer_area_and_the_card_shows_an_offer_made_for_it() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var h = new ThreadRuntimePart12Harness();
        var area = Mount(h, true, out var flow);
        var card = Assert.Single(h.Main.OnMain(() => area.GetVisualDescendants().OfType<OfferCardControl>().ToArray()));
        Assert.Equal("none", h.Main.OnMain(() => card.Kind));
        h.Main.OnMain(() => flow!.Observe(new CommandFinished("soffice --headless", 127, true, "soffice: command not found"), null));
        h.Main.OnMain(() => Settle(windows[0]));
        Assert.Equal("asked", h.Main.OnMain(() => card.Kind));
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_chat_without_an_offer_flow_draws_no_card() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var h = new ThreadRuntimePart12Harness();
        var area = Mount(h, false, out _);
        Assert.Empty(h.Main.OnMain(() => area.GetVisualDescendants().OfType<OfferCardControl>().ToArray()));
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void With_a_bound_skills_host_AppModel_gives_each_chat_one_offer_flow_and_without_one_it_gives_none()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "sk14 mount " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var unbound = new ThreadRuntimeTurnRig()) Assert.Null(unbound.Runtime.SkillsOffers);
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production().WithDownloads(root));
            using var binding = SkillsSeams.Host.Override(host);
            using var bound = new ThreadRuntimeTurnRig();
            var flow = Assert.IsType<SkillsOfferFlow>(bound.Runtime.SkillsOffers);
            Assert.Same(flow, bound.H.Main.OnMain(() => bound.App.Runtime(bound.InitialThread.Id)).SkillsOffers);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
