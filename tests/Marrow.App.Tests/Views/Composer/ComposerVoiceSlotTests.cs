using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-10, V-T71 and V-T87 (voice-SPEC V-14.2, V-14.3, V-2.6; OD-0091, OD-0086): the voice orb sits where the attachment button was, first in the controls; the
// feature flag decides whether any composer shows it. Every session is scripted: no microphone, no engine (R-TEST-10).
[Collection("Headless")]
public sealed class ComposerVoiceSlotTests(HeadlessSession headless)
{
    private static VoiceOrbSlot SlotOf(ComposerView view) => Assert.IsType<VoiceOrbSlot>(Assert.IsType<StackPanel>(One<ComposerControls>(view).Child).Children[0]);

    [Fact] // V-T87
    [Trait("Decision", "ED-292")]
    public void With_the_feature_off_no_composer_shows_the_orb()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            using var off = VoiceFeature.Switch.Override(new VoiceFeatureSwitch(false));
            var (view, window) = rig.Open();
            rig.Main.OnMain(() =>
            {
                Assert.Null(view.Voice);
                Assert.Empty(LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view));
                Assert.Empty(LogicalQuery.SelfAndDescendants<VoiceAnnouncer>(view));
                Assert.IsNotType<VoiceOrbSlot>(Assert.IsType<StackPanel>(One<ComposerControls>(view).Child).Children[0]); // the row starts with the model chip
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T87, V-14.3
    [Trait("Decision", "ED-292")]
    public void With_the_feature_on_every_composer_shows_its_own_orb_first_in_the_controls_even_the_compact_one()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (view, window) = rig.Open();
            var compact = rig.Main.OnMain(() =>
            {
                var made = new ComposerView(rig.Runtime, workingDirectory: null, compactModelChip: true);
                Marrow.App.Port.AppEnvironment.SetAppModel(made, rig.App);
                return made;
            });
            var compactWindow = rig.Main.OnMain(() => Show(compact, rig.App, 400));
            rig.PumpWindow(compactWindow);
            rig.Main.OnMain(() =>
            {
                Assert.NotNull(view.Voice);
                Assert.NotNull(compact.Voice);
                Assert.NotSame(view.Voice, compact.Voice);
                Assert.Single(LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view));
                SlotOf(view);
                SlotOf(compact);
            });
            rig.Close(compactWindow);
            rig.Close(window);
        });
    }

    [Theory] // V-T71
    [InlineData(32, VoiceOrbKind.Siri, "Siri Wave")]
    [InlineData(26, VoiceOrbKind.Membrane, "Voice Membrane")]
    [Trait("Decision", "ED-292")]
    public void The_slot_is_D_square_with_a_72_view_centred_on_it_a_hit_target_4_larger_all_round_focusable_and_named(int size, VoiceOrbKind kind, string orbName)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { OrbSize = size, Orb = kind });
            var (view, window) = rig.Open();
            rig.Main.OnMain(() =>
            {
                var slot = SlotOf(view);
                Assert.Equal(new Size(size, size), slot.Bounds.Size);
                var hit = Frame(slot.HitTarget, slot);
                Assert.Equal(-4, hit.X, 0.5);
                Assert.Equal(-4, hit.Y, 0.5);
                Assert.Equal(size + 8, hit.Width, 0.5);
                Assert.Equal(size + 8, hit.Height, 0.5);
                Assert.True(slot.HitTarget.Focusable);
                Assert.True(slot.HitTarget.IsTabStop);
                Assert.Equal("Voice input", ToolTip.GetTip(slot.HitTarget));
                Assert.Equal($"Start voice input ({orbName})", AutomationProperties.GetName(slot.HitTarget));
                var orb = Frame(slot.Orb, slot);
                Assert.Equal(72, orb.Width, 0.5);
                Assert.Equal(72, orb.Height, 0.5);
                Assert.Equal(size / 2.0, orb.Center.X, 0.5);
                Assert.Equal(size / 2.0, orb.Center.Y, 0.5);
                Assert.Equal(size, slot.Orb.SlotDiameter);
                Assert.Equal(kind, slot.Orb.Kind);
                Assert.Equal(OrbMotionMode.Rest, slot.Orb.Mode);
                // the gap from the slot to the model chip is the design's 14
                Assert.Equal(14, Frame(One<ModelEffortSlot>(view), view).X - Frame(slot, view).Right, 0.5);
                // the slot does not make the one-line pill taller than it was: it sits in the 28 point row
                Assert.Equal(44, view.Bounds.Height, 0.5);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T71: names per state, the pressed state while Listening
    [Trait("Decision", "ED-292")]
    public void The_name_follows_the_session_and_the_toggle_is_pressed_while_listening()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            var slot = rig.Main.OnMain(() => SlotOf(view));
            string Name() => rig.Main.OnMain(() => AutomationProperties.GetName(slot.HitTarget)!);
            ToggleState Pressed() => rig.Main.OnMain(() => Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(slot.HitTarget)).ToggleState);
            OrbMotionMode Mode() => rig.Main.OnMain(() => slot.Orb.Mode);
            Assert.Equal("Start voice input (Siri Wave)", Name());
            Assert.Equal(ToggleState.Off, Pressed());

            rig.Main.OnMain(() => Click(slot.HitTarget));
            rig.Elapse(window, 0.1);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            Assert.Equal("Stop and finish the text", Name());
            Assert.Equal(ToggleState.On, Pressed());
            Assert.Equal(OrbMotionMode.Listening, Mode());

            rig.Elapse(window, 3.0);
            rig.Main.OnMain(() => Click(slot.HitTarget));
            rig.Elapse(window, 0.05);
            Assert.Equal(VoiceSessionState.Processing, view.Voice.Session.State);
            Assert.Equal("Finishing the text", Name());
            Assert.Equal(ToggleState.Off, Pressed());
            Assert.Equal(OrbMotionMode.Processing, Mode());
            rig.Main.OnMain(() => Click(slot.HitTarget)); // the orb is not pressable while it finishes
            rig.Elapse(window, 0.05);
            Assert.Equal(VoiceSessionState.Processing, view.Voice.Session.State);

            rig.Elapse(window, 10);
            Assert.Equal(VoiceSessionState.Rest, view.Voice.Session.State);
            Assert.Equal("Start voice input (Siri Wave)", Name());
            Assert.Equal(OrbMotionMode.Rest, Mode());
            rig.Close(window);
        });
    }

    [Theory] // V-T71: Enter and Space toggle
    [InlineData(Key.Enter, PhysicalKey.Enter)]
    [InlineData(Key.Space, PhysicalKey.Space)]
    [Trait("Decision", "ED-292")]
    public void Enter_and_Space_on_the_focused_orb_start_and_stop_listening(Key key, PhysicalKey physical)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            rig.Speak(VoiceComposerRig.SwitchTheme());
            var (view, window) = rig.Open();
            var slot = rig.Main.OnMain(() => SlotOf(view));
            rig.Main.OnMain(() => Assert.True(slot.HitTarget.Focus()));
            void Press()
            {
                rig.Main.OnMain(() =>
                {
                    window.KeyPress(key, RawInputModifiers.None, physical, key == Key.Space ? " " : null);
                    window.KeyRelease(key, RawInputModifiers.None, physical, key == Key.Space ? " " : null);
                });
            }
            Press();
            rig.Elapse(window, 0.1);
            Assert.Equal(VoiceSessionState.Listening, view.Voice!.Session.State);
            rig.Elapse(window, 3.0);
            Press();
            rig.Elapse(window, 0.05);
            Assert.Equal(VoiceSessionState.Processing, view.Voice.Session.State);
            rig.Close(window);
        });
    }

    [Fact] // V-T71, V-11.5: the setting is followed, and a size change is a spring, not a cut
    [Trait("Decision", "ED-292")]
    public void A_size_or_orb_change_in_Settings_reaches_the_slot_and_the_size_springs()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var (view, window) = rig.Open();
            var slot = rig.Main.OnMain(() => SlotOf(view));
            double Side() => rig.Main.OnMain(() => slot.Bounds.Width);
            Assert.Equal(32, Side(), 0.5);
            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { OrbSize = 26, Orb = VoiceOrbKind.Membrane });
            rig.PumpWindow(window);
            Assert.Equal(VoiceOrbKind.Membrane, rig.Main.OnMain(() => slot.Orb.Kind));
            Assert.Equal(26, rig.Main.OnMain(() => slot.Orb.SlotDiameter));
            var sides = new List<double> { Side() };
            for (var frame = 0; frame < 60; frame++)
            {
                rig.Elapse(window, 1.0 / 60);
                sides.Add(Side());
            }
            Assert.Equal(26, sides[^1], 0.5);
            Assert.Contains(sides, side => side is > 26.5 and < 31.5); // it moved through the sizes in between
            for (var i = 1; i < sides.Count; i++)
                Assert.True(Math.Abs(sides[i] - sides[i - 1]) <= 0.15 * 6 + 0.01, $"no frame moves it over 15 % of its travel: {sides[i - 1]} then {sides[i]}");
            Assert.Equal("Start voice input (Voice Membrane)", rig.Main.OnMain(() => AutomationProperties.GetName(slot.HitTarget)!));
            rig.Close(window);
        });
    }
}
