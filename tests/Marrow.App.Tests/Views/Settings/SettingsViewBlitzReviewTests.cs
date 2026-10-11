using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// REVIEW (adversarial review of unit/blitz-settingsview, 2026-10-04): tests that fail against the unit's head and name the clause they hold.
[Collection("Headless")]
public sealed class SettingsViewBlitzReviewTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    // UI-SETTINGS-012 as drawn under OD-0141 (ED-NEW-nocta-1; it replaces the Mac's pressed lens of the reference runtime, ED-280 (5), whose two REVIEW
    // tests stood here): pressing an ON switch scales the obsidian dome 1.08 about its own centre (.t-noc --px/--py, the .kp transform), with the
    // deeper contact shadow (.shl) under it, and its lit lume stays lit; nothing commits until release.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: UI-SETTINGS-012 (the switch's pressed look, on)
    public void REVIEW_a_pressed_on_switch_scales_its_dome_about_its_centre_and_deepens_its_shadow()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = true; Drain(w); Tick(h, 1200);
            var toggle = Assert.Single(Nodes<SmallSwitch>(Row(view, "Show thinking")));
            var rest = toggle.KnobNow(); Assert.Equal(new Rect(22, 2, 22, 22), rest);
            var at = At(toggle, w); w.MouseMove(at); Drain(w); w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 600);
            var held = toggle.KnobNow();
            Assert.Equal(22 * 1.08, held.Width, 6); Assert.Equal(22 * 1.08, held.Height, 6);
            Assert.Equal(rest.Center.X, held.Center.X, 6); Assert.Equal(rest.Center.Y, held.Center.Y, 6);
            Assert.Equal(1, toggle.LookNow().Deep); Assert.Equal(1, toggle.LookNow().Lights); Assert.True(s.ShowReasoning);
            Assert.True(NoctaProbe.DrawsLitLume(toggle, toggle.LumeColor()));
            using var pixels = NoctaProbe.Snapshot(toggle, 3);
            Assert.InRange(NoctaProbe.Luma(NoctaProbe.At(pixels, 3, 33, 13)), 0.85, 1); // the lume's white core, at the dome's centre still
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.False(s.ShowReasoning); Tick(h, 1200);
        });
    }

    // The same for an OFF switch: the pressed dome keeps its empty lume ring (on and off read without colour while held, too).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: UI-SETTINGS-012 (the switch's pressed look, off)
    public void REVIEW_a_pressed_off_switch_scales_its_dome_and_keeps_its_empty_lume_ring()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 1200);
            var toggle = Assert.Single(Nodes<SmallSwitch>(Row(view, "Show thinking")));
            var at = At(toggle, w); w.MouseMove(at); Drain(w); w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 600);
            Assert.False(s.ShowReasoning);
            var held = toggle.KnobNow(); Assert.Equal(22 * 1.08, held.Width, 6); Assert.Equal(13, held.Center.X, 6); Assert.Equal(13, held.Center.Y, 6);
            Assert.Equal(0, toggle.LookNow().Lights); Assert.False(NoctaProbe.DrawsLitLume(toggle, toggle.LumeColor()));
            using var pixels = NoctaProbe.Snapshot(toggle, 3);
            var centre = NoctaProbe.Luma(NoctaProbe.At(pixels, 3, 13, 13)); var ring = NoctaProbe.Luma(NoctaProbe.At(pixels, 3, 13 + 2.1 * 1.08, 13));
            Assert.InRange(centre, 0, 0.2); Assert.True(ring - centre > 0.12, $"the held off lume is still an empty ring ({ring:F3} against {centre:F3})");
            w.MouseUp(at, MouseButton.Left); Drain(w); Tick(h, 1200);
        });
    }

    // OD-0076: "Every transition runs on the frame clock ... and the system's reduce-motion setting stills them." The clear button's fade is the
    // unit's OD-0076 deviation (UI-SETTINGS-009, UI-SETTINGS-010, UI-MCP-090): under reduce motion it must not fade.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: UI-SETTINGS-009 / UI-SETTINGS-010 / UI-MCP-090 (OD-0076 reduce motion)
    public void REVIEW_under_reduce_motion_the_search_clear_button_appears_and_goes_without_a_fade()
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            Click(w, RowButton(view, "Models")); Drain(w);
            var search = Assert.Single(Nodes<ChromeSearchField>(view)); var field = Assert.Single(Nodes<TextBox>(search)); var clear = Assert.Single(Nodes<Button>(search));
            Type(w, field, "ab"); Drain(w);
            Assert.Equal(1, clear.Opacity); // no frame has run: reduce motion shows it at once
            Press(w, Key.Escape); Drain(w);
            Assert.Equal(0, clear.Opacity);
        });
    }

    // OD-0076 (as above) for UI-THEME-027: the settle row's dim to 0.5 rides Chrome.hover in the unit; under reduce motion it is stilled.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: UI-THEME-027 (OD-0076 reduce motion)
    public void REVIEW_under_reduce_motion_the_settle_row_dims_at_once_when_the_finish_action_becomes_archive()
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            var row = Row(view, "Sound when settling");
            var archive = Nodes<ChromeVisualTile>(Row(view, "Done with a thread")).Single(tile => AutomationProperties.GetName(tile) == "Archive");
            Click(w, archive); Drain(w);
            Assert.Equal(ThreadFinishAction.Archive, model.Settings.ThreadFinishAction);
            Assert.Equal(0.5, row.Opacity); // no frame has run
        });
    }
}
