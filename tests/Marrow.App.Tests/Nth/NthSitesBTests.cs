using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Port.Updates;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Settings;
using SkiaSharp;
using Xunit;

namespace Marrow.App.Tests.Nth;

// nth motion pack, group B: the download, update and first-run sites (steps 3, 4, 10 and 15 of nth-motion-review.html). The pack's primitives are drawn on the render thread, so these tests assert that each
// site hosts the right primitive at the right size and drives it with the right state, and (for the About install capsule, which is drawn by a painter of this group) read the painter's pixels against
// values derived from the spec's build notes.

/// <summary>The About install capsule's painter into a bitmap: positions and colours come from the spec (step 10 build notes), not from a picture of the code.</summary>
public sealed class NthSitesBInstallCapsulePixelTests
{
    private const int W = 168, H = 26;

    private static SKBitmap Render(double value, bool dark, bool calm, double now, double? doneAgo = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var painter = new UpdateInstallCapsulePainter();
        var state = new UpdateInstallCapsuleState(NthSpring.At(value), doneAgo is { } ago ? now - ago : null);
        painter.Draw(canvas, new NthFrame(new Size(W, H), dark, calm, TimeProvider.System, 0, state), now);
        return bitmap;
    }

    // vertical gradient --ftop to --fbot over the capsule's height, sampled at the pixel's centre
    private static SKColor FillAt(NthPalette palette, int y)
    {
        var t = (y + .5) / H;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return new SKColor(Mix(palette.FillTop.Red, palette.FillBottom.Red), Mix(palette.FillTop.Green, palette.FillBottom.Green), Mix(palette.FillTop.Blue, palette.FillBottom.Blue));
    }

    private static void Near(SKColor expected, SKColor actual, int tolerance, string what)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance && Math.Abs(expected.Blue - actual.Blue) <= tolerance,
            $"{what}: expected {expected}, drawn {actual}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_capsule_is_the_track_with_the_metal_fill_ending_in_a_round_nose_at_p_times_W(bool dark)
    {
        var palette = NthPalette.For(dark);
        using var bitmap = Render(.5, dark, calm: true, now: 100);
        // 168 x 26, r 13: the corners of the box are outside the capsule, the middle of each end is inside it
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(W - 1, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(0, H - 1).Alpha);
        Assert.Equal(255, bitmap.GetPixel(13, 13).Alpha);
        // past the fill, the capsule is --track
        Near(palette.Track, bitmap.GetPixel(150, 13), 1, "track");
        // inside the fill, the vertical gradient --ftop to --fbot (the label is drawn in the middle: x = 10 is clear of it)
        foreach (var y in new[] { 3, 13, 22 }) Near(FillAt(palette, y), bitmap.GetPixel(10, y), 3, $"fill at y {y}");
        Assert.True(bitmap.GetPixel(10, 3).Red > bitmap.GetPixel(10, 22).Red, "the fill is lighter on top");
        // the window's right end is a round nose of radius 13 at x = p * W = 84: at y = 1.5 it has come in by 13 - sqrt(13^2 - 11.5^2) = 6.94 px, so x = 77.06 is where the fill stops on that row
        Near(FillAt(palette, 1), bitmap.GetPixel(72, 1), 3, "inside the nose");
        Near(palette.Track, bitmap.GetPixel(82, 1), 1, "outside the nose");
        // at mid height the nose reaches x = 84
        Near(palette.Track, bitmap.GetPixel(88, 6), 1, "past the nose at y 6");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_label_is_drawn_twice_the_text_colour_on_the_track_and_the_on_colour_inside_the_fill(bool dark)
    {
        var palette = NthPalette.For(dark);
        using var bitmap = Render(.5, dark, calm: true, now: 100);
        int Count(int x0, int x1, Func<SKColor, bool> test)
        {
            var count = 0;
            for (var x = x0; x < x1; x++)
                for (var y = 6; y < 20; y++)
                    if (test(bitmap.GetPixel(x, y))) count++;
            return count;
        }
        // right of the nose (x > 84) the glyphs are --text on --track; the capsule's centre is the label's centre (the label is about 80 px wide at 11.5 px)
        var onTrack = Count(88, 125, c => Math.Abs(c.Red - palette.Text.Red) + Math.Abs(c.Green - palette.Text.Green) + Math.Abs(c.Blue - palette.Text.Blue) <= 60);
        Assert.True(onTrack >= 12, $"{onTrack} pixels of --text on the track side of the label");
        // left of the nose the glyphs are --on on the fill
        var inFill = Count(44, 80, c => Math.Abs(c.Red - palette.On.Red) + Math.Abs(c.Green - palette.On.Green) + Math.Abs(c.Blue - palette.On.Blue) <= 60);
        Assert.True(inFill >= 12, $"{inFill} pixels of --on in the fill side of the label");
    }

    [Fact]
    public void The_light_passes_inside_the_fill_only_white_at_half_in_light_and_at_85_percent_in_dark_and_stops_at_100_percent()
    {
        foreach (var dark in new[] { false, true })
        {
            var palette = NthPalette.For(dark);
            const double p = .5;
            var fillWidth = p * W;
            // find a moment when the band's centre is inside the fill (the cycle is 2.6 s, travelling for 72 % of it)
            double? centre = null; double at = 0;
            for (var t = .1; t < 1.9 && centre is null; t += .05)
                if (NthMotion.BarLight(t, fillWidth, W, p, false) is { } c && c > 20 && c < fillWidth - 20) { centre = c; at = t; }
            Assert.NotNull(centre);
            using var plain = Render(p, dark, calm: true, now: at);
            using var lit = Render(p, dark, calm: false, now: at);
            var x = (int)Math.Round(centre!.Value); const int y = 3; // above the label
            var baseline = plain.GetPixel(x, y);
            var shown = lit.GetPixel(x, y);
            // the band is --fill-light (white at 50 % / 85 %) at its centre over the fill
            var alpha = palette.FillLight.Alpha / 255.0;
            SKColor Over(SKColor under) => new((byte)Math.Round(under.Red + (255 - under.Red) * alpha), (byte)Math.Round(under.Green + (255 - under.Green) * alpha), (byte)Math.Round(under.Blue + (255 - under.Blue) * alpha));
            Near(Over(baseline), shown, 8, $"light at its centre (dark {dark})");
            // the track beyond the fill carries no light
            Near(plain.GetPixel(150, y), lit.GetPixel(150, y), 0, "track beyond the fill");
            // at 100 % the light stops: the same moment draws the plain fill
            using var fullLit = Render(1, dark, calm: false, now: at);
            using var fullPlain = Render(1, dark, calm: true, now: at);
            Near(fullPlain.GetPixel(60, y), fullLit.GetPixel(60, y), 0, "full capsule has no light");
        }
    }

    [Fact]
    public void At_100_percent_the_working_label_gives_way_to_the_done_label_over_a_quarter_of_a_second()
    {
        // the two labels cross-fade: before the done moment only the working label; after 0.25 s only the done label; the labels differ, so the pictures do
        using var working = Render(1, false, calm: true, now: 100);
        using var done = Render(1, false, calm: true, now: 100, doneAgo: 1);
        using var half = Render(1, false, calm: true, now: 100, doneAgo: .125);
        long Diff(SKBitmap a, SKBitmap b)
        {
            long sum = 0;
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                    sum += Math.Abs(a.GetPixel(x, y).Red - b.GetPixel(x, y).Red);
            return sum;
        }
        Assert.True(Diff(working, done) > 800, "the done label is a different picture");
        // halfway, the picture is between the two (the cross-fade is a fade, not a cut)
        Assert.True(Diff(working, half) > 0 && Diff(half, done) > 0);
        Assert.True(Diff(working, half) < Diff(working, done) && Diff(half, done) < Diff(working, done));
    }

    [Fact]
    public void The_capsule_painter_moves_only_while_the_fill_springs_or_the_light_passes_and_for_the_cross_fade()
    {
        using var painter = new UpdateInstallCapsulePainter();
        NthFrame Frame(UpdateInstallCapsuleState state, bool calm = false) => new(new Size(W, H), false, calm, TimeProvider.System, 0, state);
        Assert.False(painter.Moving(Frame(new(NthSpring.At(0), null)), 5)); // nothing to show, nothing to draw
        Assert.True(painter.Moving(Frame(new(NthSpring.At(.5), null)), 5)); // the light passes inside a partial fill
        Assert.False(painter.Moving(Frame(new(NthSpring.At(.5), null), calm: true), 5)); // calm: no light
        Assert.True(painter.Moving(Frame(new(NthSpring.At(.5).Retarget(.9, 4.9), null), calm: true), 5)); // the spring is on its way
        Assert.False(painter.Moving(Frame(new(NthSpring.At(1), 0)), 5)); // done long ago: at rest
        Assert.True(painter.Moving(Frame(new(NthSpring.At(1), 4.9)), 5)); // just done: the label cross-fades
    }
}

[Collection("Headless")]
public sealed class NthSitesBTests(HeadlessSession headless)
{
    // a control sits in a column at the top, as the sites put theirs (a window would stretch it over its whole content area)
    private static Avalonia.Controls.Window Show(Control content, double width = 320, double height = 200)
    {
        var column = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Children = { content } };
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = column };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    // ---- step 3: bars ----

    [Fact]
    public void The_updates_rows_bar_is_a_4_px_nth_bar_unknown_length_while_the_updater_reports_nothing() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var bar = new UpdatesProgressBar();
        var window = Show(bar);
        try
        {
            Assert.Equal(4, bar.Bar.Height);
            Assert.Equal(4, bar.Bounds.Height, 3);
            Assert.True(bar.Indeterminate);
            Assert.False(bar.Bar.IsIndeterminate); // a bar that is not on show takes no frames
            bar.Active = true;
            Assert.True(bar.Bar.IsIndeterminate);
            bar.Set(.42);
            Assert.False(bar.Indeterminate);
            Assert.False(bar.Bar.IsIndeterminate);
            Assert.Equal(.42, bar.TargetFraction);
            Assert.Equal(.42, bar.Bar.Value);
            bar.Set(null); // the updater reports nothing again
            Assert.True(bar.Bar.IsIndeterminate);
            bar.Active = false;
            Assert.False(bar.Bar.IsIndeterminate);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_running_downloads_bar_springs_to_each_value_and_a_new_download_starts_from_the_left() => headless.OnUiThread(() =>
    {
        var bar = new LibraryProgressBar { Width = 200 };
        var window = Show(bar);
        try
        {
            Assert.Equal((200.0, 4.0), (bar.Bounds.Width, bar.Bounds.Height));
            bar.Set(.6, animated: true);
            Assert.Equal(.6, bar.Bar.Value);
            Assert.Equal(.6, bar.Target);
            bar.Set(.1, animated: true); // never backwards across the bar
            Assert.Equal(.1, bar.Bar.Value);
            Assert.Equal(.1, bar.Drawn, 9);
            bar.Set(.9, animated: false);
            Assert.Equal(.9, bar.Drawn, 9); // shown part way: in place
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_usage_meters_stay_the_still_library_bar_not_a_travelling_light() => headless.OnUiThread(() =>
    {
        // the account row's week bar and the service block's usage bar show how much of a quota is used: they are not a job in progress, so no light passes in them
        Assert.False(typeof(NthMotionControl).IsAssignableFrom(typeof(LibraryBar)));
        var meter = new LibraryBar { Width = 96 };
        var window = Show(meter);
        try
        {
            meter.Set(.4, animated: false);
            Assert.Equal(.4, meter.Drawn, 9);
            Assert.Empty(meter.GetLogicalDescendants().OfType<NthProgressBar>());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_voice_model_rows_bar_is_the_3_px_nth_bar_inset_16_each_side_that_fades_with_the_download() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var bar = new VoiceProgressBar();
        var window = Show(bar, width: 300);
        try
        {
            Assert.Equal((268.0, 3.0), (bar.Bounds.Width, bar.Bounds.Height));
            Assert.Equal(16, bar.Bounds.X, 3);
            Assert.Equal(0, bar.Visibility);
            Assert.False(bar.Bar.IsVisible); // out: not drawn at all
            bar.SetFraction(.37);
            Assert.Equal(.37, bar.Target);
            bar.SetFraction(.1); // a new download
            Assert.Equal(.1, bar.Fraction, 9);
        }
        finally { window.Close(); }
    });

    // ---- step 4: rings ----

    [Fact]
    public void The_skills_ring_is_the_14_px_nth_ring_and_never_runs_backwards_and_can_fill_into_the_done_disc() => headless.OnUiThread(() =>
    {
        var ring = new ProgressRing();
        var window = Show(ring);
        try
        {
            Assert.Equal((14.0, 14.0), (ring.Bounds.Width, ring.Bounds.Height));
            Assert.Equal((14.0, 14.0), (ring.Ring.Width, ring.Ring.Height));
            ring.Value = .5;
            Assert.Equal(.5, ring.Ring.Value);
            ring.Value = .2; // the next item of a run starts from nothing
            Assert.Equal(.2, ring.Ring.Value);
            Assert.False(ring.IsDone);
            ring.IsDone = true;
            Assert.True(ring.Ring.IsDone);
            ring.Width = ring.Height = 20; // the skills pill's ring
            window.UpdateLayout();
            Assert.Equal((20.0, 20.0), (ring.Ring.Bounds.Width, ring.Ring.Bounds.Height));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_skill_row_that_downloads_shows_the_ring_that_fills_into_the_disc_at_100_percent_and_its_checking_spinner_is_the_nth_one() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var mark = new StatusMark();
        var window = Show(mark);
        try
        {
            mark.Show(StatusMark.Mark.Ring, "", .4);
            var ring = Assert.Single(mark.GetLogicalDescendants().OfType<ProgressRing>());
            Assert.Equal(.4, mark.RingValue);
            Assert.False(ring.IsDone);
            mark.Show(StatusMark.Mark.Ring, "", 1);
            Assert.Equal(1, mark.RingValue);
            Assert.True(ring.IsDone); // step 4: at 100 % the ring fills into the disc and the tick draws in
            mark.Show(StatusMark.Mark.Spinner, "", null);
            Assert.Contains(mark.GetVisualDescendants().OfType<NthSpinner>(), s => s.Width == 14 && s.Height == 14);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void The_update_pills_ring_is_the_18_px_nth_ring_that_springs_to_the_run_and_starts_again_from_nothing() => headless.OnUiThread(() =>
    {
        var ring = new UpdateRing();
        var window = Show(ring);
        try
        {
            Assert.Equal(18.0, ring.Bounds.Height);
            Assert.Equal((18.0, 18.0), (ring.Ring.Width, ring.Ring.Height));
            ring.Set(.5, null);
            Assert.Equal(.5, ring.Fraction, 9); // no animation: in place
            ring.Set(.8, Animation.EaseOut(.35));
            Assert.Equal(.8, ring.Target);
            ring.Set(.1, Animation.EaseOut(.35)); // a new run
            Assert.Equal(.1, ring.Fraction, 9);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_row_bar_in_the_update_popover_is_the_3_px_nth_bar() => headless.OnUiThread(() =>
    {
        var bar = new UpdateProgressBar { Width = 200 };
        var window = Show(bar);
        try
        {
            Assert.Equal((200.0, 3.0), (bar.Bounds.Width, bar.Bounds.Height));
            Assert.Equal(3, bar.Bar.Height); // step 10: the sidebar's 3 px bar
            bar.Active = true;
            bar.Set(null, null);
            Assert.True(bar.IsSweeping);
            bar.Set(.4, null);
            Assert.False(bar.IsSweeping);
            Assert.Equal(.4, bar.Shown, 9);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_popover_rows_bar_is_drawn_only_while_its_slot_is_open() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Marrow.Core.Port.Updates.UpdatePillRow Row(Marrow.Core.Port.Updates.UpdateRowState state, double? progress) =>
            new("c", "Claude Code", Marrow.Core.Port.Updates.UpdateItemKind.ProviderCli, state, "1.0.0", "1.1.0", true, "Status", progress, null);
        var waiting = new UpdateRowView(Row(Marrow.Core.Port.Updates.UpdateRowState.Queued, null));
        Assert.False(waiting.Bar.IsVisible); // a shut slot draws no light
        Assert.False(waiting.BarOpen);
        var updating = new UpdateRowView(Row(Marrow.Core.Port.Updates.UpdateRowState.Updating, .3));
        Assert.True(updating.Bar.IsVisible);
        Assert.True(updating.BarOpen);
        Assert.Equal(.3, updating.Bar.Target);
        Assert.Equal(3, updating.Bar.Bar.Height);
    });

    [Fact]
    public void The_voice_preparation_ring_is_the_18_px_nth_ring() => headless.OnUiThread(() =>
    {
        var ring = new VoicePreparationRing();
        var window = Show(ring);
        try
        {
            Assert.Equal(18.0, ring.Bounds.Height);
            Assert.Equal((18.0, 18.0), (ring.Ring.Bounds.Width, ring.Ring.Bounds.Height));
            ring.Fraction = .36;
            Assert.Equal(.36, ring.Ring.Value);
            ring.Fraction = .52;
            Assert.Equal(.52, ring.Ring.Value);
            ring.Fraction = 0; // a new preparation
            Assert.Equal(0, ring.Fraction, 9);
        }
        finally { window.Close(); }
    });

    // ---- step 10: download and update ----

    [Fact]
    public void The_install_capsule_is_168_by_26_and_says_the_download_in_the_apps_words_then_the_done_label()
    {
        Assert.Equal((168.0, 26.0), (UpdateInstallCapsule.CapsuleWidth, UpdateInstallCapsule.CapsuleHeight));
        Assert.Equal("Downloading 0%", UpdateInstallCapsule.WorkingLabel(0));
        Assert.Equal("Downloading 42%", UpdateInstallCapsule.WorkingLabel(42)); // the Updates page's own "Downloading 42%"
        Assert.Equal("Up to date", UpdateInstallCapsule.DoneLabel); // the standing the About pill takes next; the app restarts itself, so 100 % is only reached in the new version
    }

    [Fact]
    public void The_install_capsule_springs_its_fill_stays_there_when_shown_part_way_and_is_done_only_when_told() => headless.OnUiThread(() =>
    {
        var capsule = new UpdateInstallCapsule();
        var window = Show(capsule, width: 200, height: 60);
        try
        {
            Assert.Equal((168.0, 26.0), (capsule.Bounds.Width, capsule.Bounds.Height));
            capsule.Jump(.3);
            Assert.Equal(.3, capsule.Shown, 9);
            capsule.Value = .8;
            Assert.Equal(.8, capsule.Value);
            Assert.False(capsule.IsDone);
            capsule.IsDone = true;
            Assert.True(capsule.IsDone);
            Assert.False(capsule.IsHitTestVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void When_the_run_ends_and_Marrow_waits_for_a_restart_the_button_rises_into_the_popover() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var gate = new TaskCompletionSource();
        using var harness = new AppModelHarness();
        var marrow = new FakeUpdateItem("marrow", "Marrow", Marrow.Core.Port.Updates.UpdateItemKind.Marrow, "1.0.0", "1.1.0") { NeedsRestart = true, Gate = gate.Task };
        var service = harness.Main.OnMain(() => new UpdateEverythingService([marrow], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        UpdatePopoverContent content = null!;
        Avalonia.Controls.Window window = null!;
        try
        {
            harness.Main.OnMain(() => service.CheckNow());
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() =>
            {
                content = new UpdatePopoverContent(service);
                window = new Avalonia.Controls.Window { Content = new Border { Child = content }, Width = 520, Height = 600 };
                window.Show();
                window.UpdateLayout();
                // before the run: a plain button, no rise
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(content.ActionControl!));
                service.Perform(Marrow.Core.Port.Updates.UpdatePillAction.UpdateEverything);
            });
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() => gate.SetResult());
            harness.Main.RunUntilIdle();
            harness.Main.AdvanceMilliseconds(1500);
            harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(1500)));
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Marrow.Core.Port.Updates.UpdatePillPhase.Restart, content.Shown!.Phase);
                // step 10: "Restart" rises 6 to 0 px and fades in over 0.32 s (NthArrive.Rise): a host around the button carries the opacity and render-transform transitions
                var host = Assert.IsType<Decorator>(content.ActionControl);
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(host));
                Assert.Contains(host.Transitions!, t => t is Avalonia.Animation.DoubleTransition { Property.Name: "Opacity" } d && Math.Abs(d.Duration.TotalSeconds - .32) < 1e-9);
                Assert.Contains(host.Transitions!, t => t is Avalonia.Animation.TransformOperationsTransition d && Math.Abs(d.Duration.TotalSeconds - .32) < 1e-9);
            });
        }
        finally { harness.Main.OnMain(() => window?.Close()); }
    });

    // ---- step 15: getting ready ----

    [Fact]
    public void Each_getting_ready_row_has_one_16_px_nth_status_mark_whose_done_is_the_disc_and_the_bar_is_the_4_px_nth_bar_with_a_mono_percentage() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        foreach (var row in rig.Sheet.Rows)
        {
            Assert.Same(row.StatusMark, row.Mark);
            Assert.Equal((16.0, 16.0), (row.StatusMark.Bounds.Width, row.StatusMark.Bounds.Height));
            Assert.Equal(NthDoneMark.Disc, row.StatusMark.DoneMark);
            Assert.Equal(14, row.StatusMark.MarkSize);
            Assert.Equal(NthStatus.Waiting, row.Status);
        }
        Assert.Equal(4, rig.Sheet.Bar.Height);
        Assert.Equal(4, rig.Rect(rig.Sheet.BarTrack).Height, 3);
        Assert.Equal("0%", rig.Sheet.PercentText);
        var mono = rig.Sheet.Percent.GetValue(TextElement.FontFamilyProperty);
        Assert.Contains("Mono", mono.Name, StringComparison.Ordinal);
        Assert.Equal(11, rig.Sheet.Percent.GetValue(TextElement.FontSizeProperty));
    });

    [Fact]
    public void The_getting_ready_labels_take_text3_while_waiting_text_while_working_and_text2_when_done() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var row = rig.Sheet.Rows[0];
        var theme = rig.Sheet.ActualThemeVariant;
        var waiting = row.LabelColour;
        Assert.Equal(Marrow.UI.Chrome.Chrome.PrimaryText.Opacity(.45).Resolve(theme), waiting); // the sheet's own dimmest ink (the page's --tx3 over --tx)
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Running, 0));
        rig.Run(.6);
        Assert.Equal(Marrow.UI.Chrome.Chrome.PrimaryText.Resolve(theme), row.LabelColour);
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, 6, FirstRunStepState.Done, 1));
        rig.Run(.6);
        Assert.Equal(Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(theme), row.LabelColour);
        Assert.Equal(NthStatus.Done, row.Status);
    });

    [Fact]
    public void The_getting_ready_percentage_follows_the_share_of_the_work_and_the_title_says_Ready_at_the_end() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        for (var index = 0; index < rig.Plan.Count; index++)
        {
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, rig.Plan.Count, FirstRunStepState.Running, 0));
            rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[index], index, rig.Plan.Count, FirstRunStepState.Done, rig.Plan[index].Parts));
            Assert.Equal((int)Math.Floor((index + 1) * 100.0 / rig.Plan.Count) + "%", rig.Sheet.PercentText);
        }
        Assert.Equal("Getting ready", rig.Sheet.TitleWord);
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        rig.Run(.05);
        Assert.Equal("Ready", rig.Sheet.TitleWord);
        rig.Run(.4);
        Assert.Equal((1.0, 1.0), (rig.Sheet.ReadyOpacity, rig.Sheet.ReadyRise));
    });
}
