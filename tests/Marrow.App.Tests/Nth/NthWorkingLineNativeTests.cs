using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth.Beads;
using SkiaSharp;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Nth;

// Calculi WU5 (DESIGN.md section 14, WU5 "a native run comparing frames with the canvas"; ED-NEW-calculi-wu5-1). The working line drawn by the real
// composition path, frame by frame, against the canvas's drawing of the same engine sample (NthBeadsPainter is the canvas's drawGlyph and metaball,
// 1:1, WU3): the beads sit in the line's slot, unclipped and unfaded, every frame of a turn; Reduce Motion holds one still formation and leaves no ink
// after the exit. It renders real text and composition frames, so it is a Native test (the lead runs it in a quiet window).
[Collection("Headless")]
public sealed class NthWorkingLineNativeTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("5eed0002-abcd-1234-abcd-123456789abc");

    private sealed record Shot(int Width, int Height, byte[] Bgra)
    {
        public (int B, int G, int R) At(int x, int y) { var i = (y * Width + x) * 4; return (Bgra[i], Bgra[i + 1], Bgra[i + 2]); }
    }

    private static void Frames(TimelineMount mount, int count)
    {
        for (var i = 0; i < count; i++) { mount.Advance(16); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    private static Shot Capture(TimelineMount mount)
    {
        mount.Window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        using var bitmap = mount.Window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = bitmap.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        return new Shot(buffer.Size.Width, buffer.Size.Height, pixels);
    }

    // The canvas's drawing of one sample on a transparent envelope-sized bitmap (alpha is the coverage).
    private static SKBitmap Canvas(in NthBeadsSample sample, bool dark)
    {
        var size = NthBeadsPainter.EnvelopeSize(NthWorkingBeads.GlyphSize);
        var bitmap = new SKBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        new NthBeadsPainter().Paint(canvas, sample, NthWorkingBeads.GlyphSize, dark);
        return bitmap;
    }

    // The frame's beads against the canvas's: every pixel the canvas covers fully is inked on screen, and the screen has no ink where the canvas
    // has none (one pixel of slack for edge rounding), and no ink at all outside the slot's envelope.
    private static void Matches(Shot shot, Rect slot, SKBitmap expected, string because)
    {
        int x0 = (int)Math.Round(slot.X), y0 = (int)Math.Round(slot.Y);
        var backdrop = shot.At(Math.Max(0, x0 - 3), y0);
        bool Inked(int x, int y) { var p = shot.At(x, y); return Math.Abs(p.R - backdrop.R) + Math.Abs(p.G - backdrop.G) + Math.Abs(p.B - backdrop.B) > 36; }
        bool Covered(int x, int y, byte at) => x >= 0 && y >= 0 && x < expected.Width && y < expected.Height && expected.GetPixel(x, y).Alpha > at;
        var strong = 0;
        for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                if (Covered(x, y, 230)) { strong++; Assert.True(Inked(x0 + x, y0 + y), $"{because}: the canvas covers ({x}, {y}) and the frame does not"); }
                var near = false;
                for (var dy = -1; dy <= 1 && !near; dy++) for (var dx = -1; dx <= 1 && !near; dx++) near = Covered(x + dx, y + dy, 8);
                if (!near) Assert.False(Inked(x0 + x, y0 + y), $"{because}: ink at ({x}, {y}) where the canvas has none");
            }
        Assert.True(strong > 0, $"{because}: the canvas drew no bead");
    }

    [Fact]
    [Trait("Design", "Calculi WU5")]
    public void Native_every_frame_of_a_turn_on_the_working_line_matches_the_canvas_drawing_of_its_sample() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", Turn, "go") };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var beads = NthWorkingBeads.For(mount.Runtime).Beads;
        var dark = mount.Window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        // Two seconds of the turn (the entrance, then thinking, then a tool and its steps), one frame in four compared.
        for (var frame = 0; frame < 120; frame++)
        {
            if (frame == 40) { entries.Add(ToolEntry("Reading ThreadTimeline.cs", Turn)); mount.SetEntries(entries); }
            if (frame == 80) { entries.Add(ToolEntry("Editing NthBeadsPainter.cs", Turn)); mount.SetEntries(entries); }
            Frames(mount, 1);
            if (frame % 4 != 0 || beads.Painter?.Driver.Engine is not { } engine) continue;
            var shot = Capture(mount);
            using var expected = Canvas(engine.Frame(), dark);
            var slot = new Rect(beads.TranslatePoint(default, mount.Window)!.Value, beads.Bounds.Size);
            Matches(shot, slot, expected, $"frame {frame}");
        }
    });

    [Fact]
    [Trait("Design", "Calculi WU5")]
    public void Native_reduce_motion_holds_one_still_formation_and_the_exit_leaves_no_ink() => ThreadTimelineRig.Run(headless, mount =>
    {
        EnvironmentValues.SetAccessibilityReduceMotion(mount.Window, true);
        var entries = new List<TimelineEntry> { User("prompt", Turn, "go") };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var beads = NthWorkingBeads.For(mount.Runtime).Beads;
        var dark = mount.Window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        Frames(mount, 30); // past the 0.24 s scale-up
        var engine = beads.Painter!.Driver.Engine!;
        Assert.True(engine.IsStill, "Reduce Motion holds one still formation");
        var first = Capture(mount);
        Frames(mount, 30);
        var second = Capture(mount);
        Assert.Equal(first.Bgra, second.Bgra); // nothing moves, nothing breathes
        using (var expected = Canvas(engine.Frame(), dark)) Matches(second, new Rect(beads.TranslatePoint(default, mount.Window)!.Value, beads.Bounds.Size), expected, "still");
        // the turn ends: a 0.2 s scale-down in the finished header, then nothing
        entries.Add(new TimelineEntry(new Marrow.Core.Models.TimelineItem(Turn, new Marrow.Core.Models.TimelineItem.Content.TurnEnd(new Marrow.Core.Models.TurnSummary
            { TurnID = Turn, Status = Marrow.Core.Models.TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 }), "end")));
        mount.SetEntries(entries); mount.SetTurnStartedAt(null); mount.SetPhase(RuntimePhase.Idle);
        Frames(mount, 24);
        Assert.Equal(NthBeadsMode.Gone, engine.Mode);
        var gone = Capture(mount);
        var slot = new Rect(beads.TranslatePoint(default, mount.Window)!.Value, beads.Bounds.Size);
        var backdrop = gone.At((int)slot.X - 3, (int)slot.Y);
        for (var y = (int)slot.Y; y < (int)slot.Bottom; y++)
            for (var x = (int)slot.X; x < (int)slot.Right; x++)
                Assert.Equal(backdrop, gone.At(x, y));
    });
}
