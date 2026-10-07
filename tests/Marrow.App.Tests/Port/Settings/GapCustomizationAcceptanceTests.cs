using Marrow.App.Tests.Fixtures;
using Marrow.App.Port.Settings;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Settings;

// GAP-2: exact acceptance obligations not established by the existing fixture/control tests. No production behavior is changed.
[Collection("Headless")]
public sealed class GapCustomizationAcceptanceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-251")]
    [Trait("Spec", "CZ-T36")]
    public void GAP_The_preview_runs_with_microphone_capture_engine_and_transport_seams_that_throw_if_touched() => headless.OnUiThread(() =>
    {
        var forbidden = new GapPreviewVoiceSeams();
        using var microphone = VoiceSeams.Microphone.Override(forbidden);
        using var capture = VoiceSeams.Capture.Override(forbidden);
        using var engine = VoiceSeams.Engine.Override(forbidden);
        using var transport = VoiceSeams.ModelTransport.Override(forbidden);
        using var http = new GapPreviewHttpHandler();
        using var network = URLSession.Transport.Override(http);
        using var rig = new CustomizationPageRig();
        var thread = rig.Get(() => rig.App.Runtime(Guid.NewGuid()));
        var draft = new ComposerDraft { Text = "Existing draft must stay untouched" };
        rig.Do(() => thread.Draft = draft);
        var writes = rig.Defaults.Writes.Count;
        foreach (var state in new[] { "typing", "running", "rest" })
        {
            rig.Do(() => rig.Page.PreviewStatePick.Select(state));
            rig.Advance(5500);
            rig.Do(rig.Page.Stage!.Preview.Chip.Activate);
        }
        foreach (var maker in PreviewMakers.All) rig.Do(() => rig.Page.Session!.Pick(maker.Id));
        Assert.Equal(0, forbidden.Calls);
        Assert.Equal(0, http.Calls);
        Assert.Equal(draft, rig.Get(() => thread.Draft));
        Assert.Equal(writes, rig.Defaults.Writes.Count);
        Assert.Equal("", rig.Page.Stage!.Preview.Hint.Text ?? "");
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    [Trait("Spec", "CZ-T31")]
    public void GAP_The_actual_preview_palette_matches_the_composer_slot_for_every_brand() => headless.OnUiThread(() =>
    {
        var differences = new List<string>();
        foreach (var brand in Enum.GetValues<EffortBrand>())
        {
            // CZ-4.5.3 explicitly uses Claude for Purple, which has no maker of its own.
            var actualBrand = brand == EffortBrand.Purple ? EffortBrand.Claude : brand;
            var maker = JewelPalettes.For(actualBrand).Maker;
            var slot = ComposerLightSlot.PaletteFor(actualBrand);
            var preview = PreviewMakers.Palette(maker, slot.AccentDark, slot.AccentLight);
            if (slot != preview) differences.Add($"{brand} / {maker}: composer {slot}; preview {preview}");
        }
        Assert.Empty(differences);
    });

    [Theory]
    [InlineData("detail out")]
    [InlineData("placeholder out")]
    [Trait("Decision", "ED-251")]
    [Trait("Spec", "CZ-T40")]
    public void GAP_Each_transition_frame_is_monotonic_and_moves_at_most_fifteen_percent_of_its_travel(string transition) => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        rig.Advance(1500);
        Func<double> read;
        if (transition == "detail out")
        {
            read = () => rig.Page.Beam!.Motion.Row.Detail.OutgoingOpacity;
            rig.Do(() => rig.Page.Beam!.Motion.Control.Select("pulse"));
        }
        else
        {
            read = () => rig.Page.Stage!.Preview.HintOpacity;
            rig.Do(() => rig.Page.PreviewStatePick.Select("typing"));
        }
        var start = rig.Get(() => SwiftClock.Now);
        var frameStart = rig.Frames.Now;
        var frames = new List<double> { rig.Get(read) };
        var times = new List<TimeSpan> { TimeSpan.Zero };
        for (var i = 1; i <= 24; i++)
        {
            rig.Advance(FrameCurves.FrameMs, settle: false);
            var elapsed = rig.Get(() => SwiftClock.Now - start);
            Assert.Equal(rig.Frames.Now - frameStart, elapsed);
            frames.Add(rig.Get(read));
            times.Add(elapsed);
        }
        Assert.Equal(1, frames[0]);
        Assert.Equal(0, frames[^1]);
        for (var i = 1; i < frames.Count; i++)
        {
            Assert.True(frames[i] <= frames[i - 1], transition + ": opacity reverses");
            // OD-0126 applies to all GAP-2 motion: locked CSS ease at the actual
            // rendered times, with no added margin. Detail is .1 s; placeholder .15 s.
            var seconds = transition == "detail out" ? .1 : .15;
            var designStep = GapDetailFrameOracle.Opacity(times[i - 1], seconds)
                - GapDetailFrameOracle.Opacity(times[i], seconds);
            var step = frames[i - 1] - frames[i];
            Assert.True(step <= .15 + 1e-9 || step <= designStep,
                $"{transition}, frame {i} at 60 Hz: step {step:F9} exceeds 0.15 of the travel and the approved design step {designStep:F9}");
        }
    });
}
