using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// UsageViews.swift:232-235 Text("· \(countText)").contentTransition(.numericText()), under .animation(Chrome.panelSlide,
// value: credits) (:256): after a spend the count morphs in place.
[Collection("Headless")]
public sealed class UsageCountMorphTests(HeadlessSession headless)
{
    private const ProviderKind Provider = ProviderKind.Zai;

    [Fact]
    [Trait("Req", "UI-PROVREG-024")]
    [Trait("Req", "UI-USAGE-021")]
    public void The_banked_resets_count_morphs_in_place_when_the_credits_change() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(Provider, Limits(windows: [Meter("w", "5-hour limit", 25)], resets: Resets(2, Credit("WEEK:3", title: "Weekly reset"), Credit("WEEK:4", title: "Weekly reset"))));
        var view = rig.Show(new PlanLimitsView(Provider));
        var block = All<BankedResetsView>(view).Single();
        var count = All<MorphingTextBlock>(block).Single();
        Assert.Equal("\u00b7 2 available", count.Text);

        rig.PublishLimits(Provider, Limits(windows: [Meter("w", "5-hour limit", 25)], resets: Resets(1, Credit("WEEK:3", title: "Weekly reset"))));

        Assert.Same(count, All<MorphingTextBlock>(block).Single()); // the same label, so it can morph
        Assert.Equal("\u00b7 1 available", count.Text);
        Assert.True(count.IsMorphing);
    });
}
