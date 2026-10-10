using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift, the three `ProgressView().controlSize(.mini)` sites: :22 (the plan limits header),
// :109 (the credits row) and :355 ("Resetting…"). The same control is on screen in 2-chat-use.mov at S 145.0-145.6 s (usage popover
// open 144.722-146.76 s, labels_C.md A15): cropped 64x64 px at (1894,1148) from frames 145.0, 145.2, 145.4 and 145.6 s and thresholded
// against the card colour it is 10 x 10 px = 10 pt (one px is one pt in that video, ED-275), eight spokes. PR-CHAT-099 ruling (E_rulings.md).
[Collection("Headless")]
public sealed class UsageViewsMiniSpinnerTests(HeadlessSession headless)
{
    private static void AssertMini(ProgressBar spinner)
    {
        // OD-0136 amendment 2: the nth tile-trace spinner draws from 12 pt (the old mini control was 10 pt, 2-chat-use.mov 145.0-145.6 s)
        Assert.Equal(12, spinner.Width);
        Assert.Equal(12, spinner.Height);
        Assert.Equal(12, spinner.Bounds.Width);
        Assert.Equal(12, spinner.Bounds.Height);
        var drawing = Assert.Single(spinner.GetVisualDescendants().OfType<Marrow.UI.Port.Nth.NthSpinner>());
        Assert.Equal(12, drawing.Bounds.Width);
        Assert.Equal(12, drawing.Bounds.Height);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // UsageViews.swift:22 ProgressView().controlSize(.mini): the nth spinner at 12 pt (was 10 pt, 2-chat-use.mov 145.0-145.6 s)
    public void The_plan_limits_header_spinner_is_the_12_point_nth_spinner() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishLimits(ProviderKind.Zai, Limits(planName: "Max (20x)"));
        rig.SetLoadingLimits(ProviderKind.Zai, true);
        var view = rig.Show(new PlanLimitsView(ProviderKind.Zai), width: 400);

        AssertMini(Assert.Single(Spinners(view)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // UsageViews.swift:109 ProgressView().controlSize(.mini): the nth spinner at 12 pt (was 10 pt, 2-chat-use.mov 145.0-145.6 s)
    public void The_credits_row_spinner_is_the_12_point_nth_spinner() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        rig.PublishCredits(ProviderKind.Deepseek, Balance(12.34, monthly: 12.34));
        rig.SetLoadingCredits(ProviderKind.Deepseek, true);
        var view = rig.Show(new CreditsView(ProviderKind.Deepseek), width: 420);

        AssertMini(Assert.Single(Spinners(view)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // UsageViews.swift:355 ProgressView().controlSize(.mini): the nth spinner at 12 pt (was 10 pt, 2-chat-use.mov 145.0-145.6 s)
    public void The_resetting_row_spinner_is_the_12_point_nth_spinner() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = rig.Show(new BankedResetsView(ProviderKind.Codex,
            Resets(2, Credit("a", title: "5-hour reset"), Credit("b", title: "Weekly reset"))));
        SetPhase(block, new BankedResetsView.Phase.Applying("a"));
        rig.Settle();

        AssertMini(Assert.Single(Spinners(block)));
    });
}
