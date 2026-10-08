using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraDismissRegressionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_second_mark_click_closes_the_idle_explanation() => headless.OnUiThread(() =>
    {
        using var rig = new HydraButtonRig();
        rig.Click();
        Assert.IsType<HydraIdlePopover>(Assert.Single(rig.Presented()));
        rig.Click();
        Assert.Empty(rig.Presented());
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void The_owner_requested_intro_can_dismiss_outside_and_toggle_without_acknowledging_it() => headless.OnUiThread(() =>
    {
        using var rig = new HydraButtonRig(introRead: false);
        Assert.True(rig.PopupShowing<HydraIntroPopover>()!.IsLightDismissEnabled);
        rig.Click();
        Assert.Empty(rig.Presented());
        Assert.False(rig.Model.Settings.HasSeenHydraIntro);
        rig.Click();
        Assert.IsType<HydraIntroPopover>(Assert.Single(rig.Presented()));
    });
}
