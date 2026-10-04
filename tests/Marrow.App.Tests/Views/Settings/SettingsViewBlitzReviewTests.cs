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
    // UI-SETTINGS-012, SOURCE FACT (the reference runtime): pressing an ON switch widens the knob about its own centre and turns it into a lens
    // about half see-through. work/reference-video/frames/w-1-settings-tour-24.0/win, row y 346: the knob is 728-753 (centre 740.5) at rest
    // (000000-000006); held, 727-754 (000008), 726-755 (000010), 725-757 (000012), the centre 740.5-741.0 throughout, overhanging the track's
    // right end (755); its centre pixel goes (255,255,255) -> (249,194,212) -> (242,141,173) over the accent (246,37,103): about 0.48 white.
    // CATALOGUE settings.switch-toggle: "widens 25 -> 29-30 px (+2 px tall) in ~80-100 ms with its centre fixed".
    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: UI-SETTINGS-012 (the system switch's pressed look)
    public void REVIEW_a_pressed_on_switch_widens_about_its_centre_into_a_half_see_through_lens()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = true; Drain(w); Tick(h, 400);
            var toggle = Assert.Single(Nodes<SmallSwitch>(Row(view, "Show thinking")));
            var rest = toggle.KnobNow(); var restCentre = rest.X + rest.Width / 2;
            var at = At(toggle, w); w.MouseMove(at); Drain(w); w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 150);
            var held = toggle.KnobNow();
            Assert.True(held.Width > rest.Width, "the knob widens when pressed");
            Assert.InRange(held.X + held.Width / 2, restCentre - 0.75, restCentre + 0.75); // the reference keeps the centre (740.5 -> 740.5/741.0)
            // the lens over the accent track: the reference's centre pixel is (242,141,173), green about 0.48 of the way from the accent's to white
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)toggle.Bounds.Width, (int)toggle.Bounds.Height));
            using (var context = bitmap.CreateDrawingContext()) toggle.Render(context);
            using var stream = new MemoryStream(); bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
            var centre = pixels.GetPixel((int)(held.X + held.Width / 2), (int)(held.Y + held.Height / 2));
            var accent = Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant);
            var white = (centre.Green - accent.G) / (255.0 - accent.G);
            Assert.InRange(white, 0.33, 0.65);
            w.MouseUp(at, MouseButton.Left); Drain(w); Tick(h, 400);
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
