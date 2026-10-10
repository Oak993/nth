using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-4.3: the preview composer, a runtime-free composer made of the app composer's own visual parts. Its numbers: stage.json (measured from the locked
// design by tests/fixtures/customization/stage.generate.cjs) and the real composer (ComposerView, built by the same tests that already hold its frame).
[Collection("Headless")]
public sealed class ComposerPreviewTests(HeadlessSession headless)
{
    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/stage.json")));
    }

    // the stage the preview sits in: 496 x 212 (CZ-4.1)
    private static Border Stage(ComposerPreview preview) => new() { Width = 496, Height = 212, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = preview };

    private static void Advance(TestRuntime runtime, ManualFrameSource frames, double milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
    }

    private static Rect Frame(Visual control, Visual relativeTo) => new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T46, CZ-4.3: the placeholder is the real composer's string for a Claude thread
    [Trait("Spec", "CZ-T46")]
    public void The_placeholder_is_the_real_composers_for_a_Claude_thread_and_the_designs() => headless.OnUiThread(() =>
    {
        using var fixture = Fixture();
        Assert.Equal(ComposerPreview.Placeholder, fixture.RootElement.GetProperty("placeholder").GetString());
        using var h = RuntimeHarness();
        var view = Pill(h, h.Runtime);
        h.Main.OnMain(() =>
        {
            h.App.Settings.HydraEnabled = true;
            h.App.Settings.HydraAlwaysHeads = false;
            var thread = h.Thread with { Provider = ProviderKind.Claude, HydraEnabled = true };
            Assert.Equal(ComposerPreview.Placeholder, view.Placeholder(thread));
        });
        var preview = new ComposerPreview(new PreviewSession(ProviderKind.Claude));
        Assert.Equal(ComposerPreview.Placeholder, preview.Hint.Placeholder);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T37, CZ-4.3: 472 wide at left 12, bottom 12; the light's rectangle is the pill's and its fade edge the stage's bottom
    [Trait("Spec", "CZ-T37")]
    public void The_composer_sits_472_wide_12_from_the_left_and_the_bottom_and_the_light_is_given_its_rectangle() => headless.OnUiThread(() =>
    {
        using var fixture = Fixture();
        var geometry = fixture.RootElement.GetProperty("geometry");
        var preview = new ComposerPreview(new PreviewSession(ProviderKind.Claude));
        PreviewHost.Show(Stage(preview), (window, _) =>
        {
            window.UpdateLayout();
            var pill = Frame(preview.Pill, preview);
            Assert.Equal(geometry.GetProperty("composerWidth").GetProperty("inside").GetDouble(), pill.Width);
            Assert.Equal(geometry.GetProperty("composerLeft").GetProperty("inside").GetDouble(), pill.X);
            Assert.Equal(geometry.GetProperty("playgroundComposer").GetProperty("bottom").GetDouble(), 212 - pill.Bottom);
            Assert.Equal(212, preview.Bounds.Height);
            Assert.Equal(pill, preview.Light.ComposerRect);
            Assert.Equal(212, preview.Light.FadeBottom);
            Assert.False(preview.Light.IsHitTestVisible);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T45, CZ-4.3; voice-SPEC V-14.1: at width 472 the preview matches the real pill (46 high, attach at left, trailing chip/ring/send)
    [Trait("Spec", "CZ-T45")]
    public void At_472_the_preview_is_laid_out_as_a_real_composer_of_the_same_width() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel = false);
        var real = Pill(h, h.Runtime, null);
        var realWindow = h.Main.OnMain(() => Show(real, h.App, 472));
        Pump(h, realWindow);
        Rect pillR = default, attachR = default, chipR = default, ringR = default, sendR = default, discR = default, placeholderR = default;
        double realFont = 0;
        h.Main.OnMain(() =>
        {
            var controls = One<ComposerControls>(real);
            pillR = Frame(real, realWindow);
            attachR = Frame(Named(real, "Attach files"), real);
            chipR = Frame(One<ModelEffortSlot>(controls), real);
            ringR = Frame(One<ContextMeter>(controls), real);
            sendR = Frame(One<SendButton>(controls), real);
            discR = Frame(RealComposerRig.SendDisc(controls), real);
            var text = One<ComposerNSTextView>(real);
            placeholderR = Frame(text, real);
            realFont = text.FontSize;
        });
        Close(h, realWindow);

        var preview = new ComposerPreview(new PreviewSession(ProviderKind.Claude));
        PreviewHost.Show(Stage(preview), (window, _) =>
        {
            window.UpdateLayout();
            var pill = preview.Pill;
            Assert.Equal(pillR.Width, pill.Bounds.Width, .5);
            Assert.Equal(pillR.Height, pill.Bounds.Height, .5);
            var row = preview.Controls;
            Assert.Equal(new[] { "PreviewComposerChip", "PreviewRing", "PreviewSendButton" }, row.Children.Select(c => c.GetType().Name).ToArray());
            Assert.Equal(2, row.Spacing);
            Rect Rel(Visual v) => Frame(v, pill);
            var attach = Named(preview, "Attach files");
            var chip = One<PreviewComposerChip>(row);
            var ring = One<PreviewRing>(row);
            var send = One<PreviewSendButton>(row);
            Assert.DoesNotContain(attach, LogicalQuery.SelfAndDescendants<Button>(row));
            Assert.Equal(new Rect(8, (pill.Bounds.Height - 28) / 2, 34, 28), Rel(attach)); // ED-NEW-gate-fixes-2 (OD-0132): centred on the composer, as the real inside button is, not 8 above its foot
            Assert.Equal(attachR.Size.Width, Rel(attach).Width, .5);
            Assert.Equal(attachR.Size.Height, Rel(attach).Height, .5);
            Assert.Equal(attachR.X, Rel(attach).X);
            Assert.Equal(attachR.Y, Rel(attach).Y);
            Assert.Equal(chipR.Height, Rel(chip).Height, .5);
            Assert.Equal(chipR.Y, Rel(chip).Y, .5);
            Assert.Equal(ringR.Width, Rel(ring).Width, .5);
            Assert.Equal(pillR.Width - ringR.X, pill.Bounds.Width - Rel(ring).X, 1.5);
            Assert.Equal(sendR.Width, Rel(send).Width, .5);
            Assert.Equal(pillR.Width - sendR.X, pill.Bounds.Width - Rel(send).X, 1.5);
            var disc = Frame(preview.Send, pill);
            Assert.Equal((discR.Width, discR.Height), (disc.Width, disc.Height));
            Assert.Equal(pillR.Width - discR.Right, pill.Bounds.Width - disc.Right, .5);
            Assert.Equal(placeholderR.X, Rel(preview.Hint).X, .5);
            Assert.Equal(placeholderR.Y, Rel(preview.Hint).Y, .5);
            Assert.Equal(realFont, preview.Hint.FontSize);
            // the real chip and the preview's are one control: neither is shorter than the other
            Assert.Equal(send.Bounds.Height, sendR.Height, .5);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T45, CZ-4.3: the rendered placeholder and the ring and send end of the plate, against the real composer's pixels
    [Trait("Spec", "CZ-T45")]
    public void The_rendered_placeholder_and_the_control_end_match_a_real_composers_pixels() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel = false);
        var real = Pill(h, h.Runtime, null);
        var realWindow = h.Main.OnMain(() => Show(real, h.App, 472));
        Pump(h, realWindow);
        byte[] realText = [], realEnd = [];
        var realWords = "";
        h.Main.OnMain(() =>
        {
            realWords = One<ComposerNSTextView>(real).Placeholder; // the harness's thread is a Codex one: the same words go in the preview, so the glyphs compare
            var text = Frame(One<ComposerNSTextView>(real), realWindow);
            realText = PreviewPixels.Region(realWindow, new Rect(text.X, text.Y, 120, text.Height));
            var send = Frame(RealComposerRig.SendDisc(real), realWindow);
            realEnd = PreviewPixels.Region(realWindow, send);
        });
        Close(h, realWindow);

        var preview = new ComposerPreview(new PreviewSession(ProviderKind.Claude)) { Settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Off } };
        preview.Hint.Placeholder = realWords;
        var stage = Stage(preview);
        stage.VerticalAlignment = VerticalAlignment.Bottom;
        stage.Margin = new Thickness(-12, 0, 0, -12); // the plate lands where the real one stands in its window
        using var runtime = new TestRuntime();
        using var bound = FrameClock.Source.Override(new ManualFrameSource());
        var plain = new HostWindow { Width = 1400, Height = 900, Content = stage }; // a plain window, as the real composer's tests use
        plain.Show();
        try
        {
            var window = plain;
            window.UpdateLayout();
            var text = Frame(preview.Hint, window);
            var previewText = PreviewPixels.Region(window, new Rect(text.X, text.Y, 120, text.Height));
            var previewEnd = PreviewPixels.Region(window, Frame(preview.Send, window));
            var (rl, rt, rr, rb) = PreviewPixels.InkBounds(realText, 120, 40, skip: 2);
            var (pl, pt, pr, pb) = PreviewPixels.InkBounds(previewText, 120, 40, skip: 2);
            Assert.True(rr > 60 && pr > 60, "the placeholder's ink is found in both");
            Assert.InRange(pl, rl - 1, rl + 1); Assert.InRange(pt, rt - 1, rt + 1); Assert.InRange(pr, rr - 1, rr + 1); Assert.InRange(pb, rb - 1, rb + 1);
            Assert.InRange(PreviewPixels.MeanDifference(PreviewPixels.AgainstItsBackground(realEnd), PreviewPixels.AgainstItsBackground(previewEnd)), 0, 2);
        }
        finally { plain.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T35, CZ-4.3: Typing types the playground's string one character every 0.055 s from empty and stays; leaving crossfades the placeholder back
    [Trait("Spec", "CZ-T35")]
    public void Typing_types_the_playgrounds_string_at_0_055_seconds_a_character_and_leaving_it_brings_the_placeholder_back() => headless.OnUiThread(() =>
    {
        using var fixture = Fixture();
        var text = fixture.RootElement.GetProperty("typing").GetProperty("text").GetString()!;
        var pace = fixture.RootElement.GetProperty("typing").GetProperty("secondsPerChar").GetDouble();
        Assert.Equal(.055, pace);
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        PreviewHost.Show(Stage(preview), (window, _) =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource(); using var binding = FrameClock.Source.Override(frames);
            Assert.Equal("", preview.TypedText);
            Assert.Equal(1, preview.HintOpacity);
            Assert.Equal(0, preview.Typed.Opacity);
            session.Pick(PreviewState.Typing);
            Advance(runtime, frames, 1);
            Assert.Equal("", preview.TypedText);
            Advance(runtime, frames, 169); // 170 ms in: three characters
            Assert.Equal(text[..3], preview.TypedText);
            Advance(runtime, frames, 55); // 225 ms in: four
            Assert.Equal(text[..4], preview.TypedText);
            Assert.InRange(preview.HintOpacity, 0, .001); // the placeholder is gone in 0.15 s
            Assert.Equal(1, preview.Typed.Opacity);       // the typed text is in in 0.12 s
            Advance(runtime, frames, 5000);
            Assert.Equal(text, preview.TypedText);
            Assert.True(preview.Caret.IsVisible);
            // leaving Typing: the placeholder and the typed text cross over 0.25 s
            session.Pick(PreviewState.Rest);
            Advance(runtime, frames, 100);
            Assert.InRange(preview.HintOpacity, .01, .99);
            Assert.InRange(preview.Typed.Opacity, .01, .99);
            Advance(runtime, frames, 400);
            Assert.Equal(1, preview.HintOpacity);
            Assert.Equal(0, preview.Typed.Opacity);
            Assert.False(preview.Caret.IsVisible);
            // entering Typing again starts from empty
            session.Pick(PreviewState.Typing);
            Advance(runtime, frames, 1);
            Assert.Equal("", preview.TypedText);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T35, CZ-4.3, CZ-14.1: Running shows the stop square, the dots, the spinning ring; the light is given the phase
    public void Running_shows_stop_the_dots_and_a_spinning_ring_and_the_light_follows_the_phase() => headless.OnUiThread(() =>
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        PreviewHost.Show(Stage(preview), (window, _) =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource(); using var binding = FrameClock.Source.Override(frames);
            Assert.Equal(ComposerLightPhase.Rest, preview.Light.Phase);
            Assert.Equal("arrow.up", preview.SendSymbol);
            Assert.False(preview.Send.IsEnabled);
            Assert.False(preview.DotsShown);
            Assert.Equal(0, frames.Pending); // nothing moves at rest, so nothing asks for a frame
            session.Pick(PreviewState.Typing);
            Assert.Equal(ComposerLightPhase.Typing, preview.Light.Phase);
            Assert.True(preview.Send.IsEnabled);
            session.Pick(PreviewState.Running);
            Assert.Equal(ComposerLightPhase.Running, preview.Light.Phase);
            Assert.Equal("stop.fill", preview.SendSymbol);
            Assert.True(preview.Send.IsEnabled);
            Assert.True(preview.DotsShown);
            // the ring: eased in with time constant 0.35 s, 330 degrees a second at full
            var before = preview.RingAngle;
            Advance(runtime, frames, 16);
            var early = preview.RingAngle - before;
            Advance(runtime, frames, 2000);
            var settledStart = preview.RingAngle;
            Advance(runtime, frames, 100);
            var settledRate = (preview.RingAngle - settledStart + 360) % 360 / .1;
            Assert.InRange(settledRate, 300, 340); // ~330 deg/s once eased in
            Assert.True(early < 330 * .016);       // slower than full while it eases in
            session.Pick(PreviewState.Rest);
            Assert.False(preview.DotsShown);
            Advance(runtime, frames, 6000);
            var rest = preview.RingAngle;
            Advance(runtime, frames, 100);
            Assert.Equal(rest, preview.RingAngle, 3);
            Assert.Equal(0, frames.Pending); // and it stops asking for frames when the ring has stopped
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-4.3, CZ-4.5.5: clicking the composer's chip steps to the next maker; the light wears the maker's palette
    [Trait("Spec", "CZ-T32")]
    public void The_chip_steps_the_maker_and_the_light_wears_that_makers_palette_and_the_settings_given() => headless.OnUiThread(() =>
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        PreviewHost.Show(Stage(preview), (window, _) =>
        {
            window.UpdateLayout();
            Assert.Equal("Claude", preview.Chip.NameLabel.Text);
            var palette = preview.Light.Palette;
            Assert.Equal(PreviewMakers.Palette("Claude", palette.AccentDark, palette.AccentLight), palette);
            preview.Chip.Activate();
            Assert.Equal("OpenAI", session.Maker);
            Assert.Equal("OpenAI", preview.Chip.NameLabel.Text);
            Assert.Equal(PreviewMakers.Palette("OpenAI", palette.AccentDark, palette.AccentLight), preview.Light.Palette);
            var settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura };
            preview.Settings = settings;
            Assert.Equal(settings, preview.Light.Settings);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T36, CZ-4.8: the preview opens no draft, thread, microphone, network or model; its text and controls are inert
    [Trait("Spec", "CZ-T36")]
    public void The_preview_holds_no_runtime_no_draft_and_no_editable_text_and_its_controls_are_inert() => headless.OnUiThread(() =>
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session);
        PreviewHost.Show(Stage(preview), (window, overlay) =>
        {
            window.UpdateLayout();
            var parts = preview.GetVisualDescendants().ToArray();
            Assert.DoesNotContain(parts, p => p is ComposerView or ComposerArea or ComposerControls or ContextMeter or SendButton or ModelEffortSlot);
            Assert.True(preview.Hint.IsReadOnly);
            Assert.False(preview.Hint.Focusable);
            Assert.False(preview.Hint.IsHitTestVisible);
            Assert.False(preview.Hint.AcceptsReturn);
            Assert.False(Avalonia.Input.DragDrop.GetAllowDrop(preview.Hint));
            Assert.False(preview.Send.IsHitTestVisible);
            Assert.False(preview.Ring.IsHitTestVisible);
            Assert.False(preview.Attach.IsHitTestVisible);
            foreach (var state in new[] { PreviewState.Typing, PreviewState.Running, PreviewState.Rest }) session.Pick(state);
            preview.Chip.Activate();
            Assert.Empty(overlay.Popups); // the composer's chip opens nothing: it steps
            Assert.Equal("", preview.Hint.Text ?? "");
        });
    });
}
