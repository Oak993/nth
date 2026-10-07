using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Capsule;

namespace Marrow.App.Tests.Port.Settings;

// MA-9 (step 3a, OD-0115, OD-0119): "Pill in the composer" on the Customization page, where the pinned block has let go, and its pick being the app's one stored setting.
[Collection("Headless")]
public sealed class CustomizationPillSectionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_page_carries_the_section_after_its_sticky_ones_with_merged_capsule_picked() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var section = rig.Get(() => Assert.Single(rig.Page.Rest.Children.OfType<PillCapsuleSection>()));
        Assert.Equal("1", rig.Get(() => section.Picked));
        Assert.Empty(rig.Page.Sticky.Children.OfType<PillCapsuleSection>());
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pick_on_the_page_is_the_apps_stored_setting_and_a_stored_pick_is_shown() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var section = rig.Get(() => rig.Page.Rest.Children.OfType<PillCapsuleSection>().Single());
        rig.Do(() => section.Group.Select("8"));
        Assert.Equal("8", rig.Get(() => rig.App.Settings.CapsuleStyle.Value));
        Assert.Contains(CapsuleStyleSetting.SettingsKey, rig.Defaults.Writes);
        rig.Do(() => rig.App.Settings.CapsuleStyle.Value = "3");
        Assert.Equal("3", rig.Get(() => section.Picked));
    });
}
