using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T104 (voice-SPEC V-2.6, V-14.3): with VoiceFeature.Enabled true the orb appears in every composer: the main one, the compact one and a Hydra head's (no changes tab), each with its own pill layer, glow and
// language question; with it off none has any. Both VT-10's ambient override and VT-15's process SwitchOn are exercised.
[Collection("Headless")]
public sealed class ComposerVoiceOrbEverywhereTests(HeadlessSession headless)
{
    private static (ComposerArea Area, Avalonia.Controls.Window Window) Open(VoiceComposerRig rig, bool compact, bool changes)
    {
        var area = rig.Main.OnMain(() =>
        {
            var made = new ComposerArea(rig.Runtime, workingDirectory: null, compactModelChip: compact, takesFocusOnAppear: false, showsChanges: changes);
            AppEnvironment.SetAppModel(made, rig.App);
            return made;
        });
        var window = rig.Main.OnMain(() => Show(area, rig.App, 500));
        rig.PumpWindow(window);
        return (area, window);
    }

    [Theory] // V-T104
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [Trait("Decision", "ED-292")]
    public void With_the_feature_enabled_every_composer_has_the_orb_its_pill_layer_its_glow_and_its_question(bool compact, bool changes)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(); // the rig enables the feature for its own flow
            Assert.True(VoiceFeature.Enabled);
            var (area, window) = Open(rig, compact, changes);
            rig.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(area));
                Assert.Single(LogicalQuery.SelfAndDescendants<VoicePillSlot>(area));
                Assert.Single(LogicalQuery.SelfAndDescendants<VoiceGlowLayer>(area));
                Assert.Single(LogicalQuery.SelfAndDescendants<VoiceLanguageQuestion>(area));
                Assert.Single(LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(area));
                var slot = One<VoiceOrbSlot>(area);
                Assert.True(slot.Bounds.Width > 0 && slot.Bounds.Height > 0);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T104, V-2.6: and with it off, none
    [Trait("Decision", "ED-292")]
    public void With_the_feature_off_no_composer_has_any_of_it()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            using var off = VoiceFeature.Switch.Override(new VoiceFeatureSwitch(false));
            Assert.False(VoiceFeature.Enabled);
            var (area, window) = Open(rig, compact: false, changes: true);
            rig.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(area));
                Assert.Empty(LogicalQuery.SelfAndDescendants<VoicePillSlot>(area));
                Assert.Empty(LogicalQuery.SelfAndDescendants<VoiceGlowLayer>(area));
                Assert.Empty(LogicalQuery.SelfAndDescendants<VoiceLanguageQuestion>(area));
            });
            rig.Close(window);
        });
    }

    [Theory] // V-T104: VT-15's production switch, with the ambient explicitly off
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [Trait("Decision", "ED-292")]
    public void SwitchOn_installs_the_orb_in_main_compact_and_Hydra_composers(bool compact, bool changes)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            using var off = VoiceFeature.Switch.Override(new VoiceFeatureSwitch(false));
            VoiceFeature.Reset();
            try
            {
                Assert.False(VoiceFeature.Enabled);
                VoiceFeature.SwitchOn();
                Assert.True(VoiceFeature.Enabled);
                var (area, window) = Open(rig, compact, changes);
                try
                {
                    rig.Main.OnMain(() =>
                    {
                        var slot = Assert.Single(LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(area));
                        Assert.True(slot.Bounds.Width > 0 && slot.Bounds.Height > 0);
                        Assert.Single(LogicalQuery.SelfAndDescendants<VoicePillSlot>(area));
                        Assert.Single(LogicalQuery.SelfAndDescendants<VoiceGlowLayer>(area));
                        Assert.Single(LogicalQuery.SelfAndDescendants<VoiceLanguageQuestion>(area));
                    });
                }
                finally { rig.Close(window); }
            }
            finally { VoiceFeature.Reset(); }
        });
    }
}
