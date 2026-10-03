using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Capture comparisons against the reference runtime: the owner's video of the real Droppy Code 1.7.0
// (E:/Droppy/work/reference-video/2-chat-use.mov, about 1 px = 1 pt; INDEX.md). The port is rendered headless at one
// pixel per point (Skia, CaptureRenderedFrame) and measured the same way the video frame was
// (E:/Droppy/work/parity/evidence/TimelineRows/, measured with scratch measure_bubble.py; never in git). Set
// MARROW_PARITY_EVIDENCE to a folder to save the port's frames beside the video's for the side-by-side images.
// The recorder's theme is Monokai (accent F92672); thread text is the recorder's own and implies no default.
[Collection("Headless")]
public sealed class TimelineRowsCaptureTests(HeadlessSession headless)
{
    private static readonly string? Evidence = Environment.GetEnvironmentVariable("MARROW_PARITY_EVIDENCE");

    private readonly record struct Frame(int Width, int Height, byte[] Pixels)
    {
        public (int R, int G, int B) At(int x, int y) => (Pixels[(y * Width + x) * 4], Pixels[(y * Width + x) * 4 + 1], Pixels[(y * Width + x) * 4 + 2]);
    }

    private static Frame Capture(AvaloniaWindow window, string? name)
    {
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        if (Evidence is { } folder)
        {
            Directory.CreateDirectory(folder);
#pragma warning disable CS0618 // the evidence image only; the encoder options are irrelevant to a PNG for viewing
            bitmap.Save(Path.Combine(folder, name + ".png"));
#pragma warning restore CS0618
        }
        using var buffer = bitmap.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        Assert.Equal(buffer.Size.Width * 4, buffer.RowBytes);
        return new Frame(buffer.Size.Width, buffer.Size.Height, pixels);
    }

    // ---------------------------------------------------------------- PR-CHAT-077

    private readonly record struct Bubble(int Top, int Bottom, int BodyLeft, int BodyRight, int TipRight, int InkLeft, int InkRight,
        int TopRowLeft, int ThirdRowLeft, int TopRowRight, bool Flares, bool Notch, (int R, int G, int B) Fill, int[] InkRows);

    // A UserMessageRow in the 820-point timeline column over the video's opaque black window (backdrop 5,5,5), Monokai.
    private Bubble RenderBubble(ThreadRuntimePart12Harness h, string text, string name)
    {
        var row = new UserMessageRow(UserEntry(text, Guid.NewGuid()), h.Runtime, canRevert: false);
        var window = new AvaloniaWindow
        {
            Width = 820, Height = 420, RequestedThemeVariant = ThemeVariant.Dark, // the 820-point timeline column
            Background = new SolidColorBrush(Color.FromRgb(5, 5, 5)),
            Content = new Border { Padding = new Thickness(0, 38, 0, 0), Child = row },
        };
        window.Show();
        try
        {
            var frame = Capture(window, name);
            // The same mask as the video's measurement: the accent wash over black, red clearly above green.
            bool Tinted(int x, int y) { var (r, g, _) = frame.At(x, y); return r - g > 12 && r > 18; }
            bool Ink(int x, int y) { var (r, g, b) = frame.At(x, y); return r + g + b > 300; }
            var rows = Enumerable.Range(0, frame.Height).Where(y => Enumerable.Range(0, frame.Width).Any(x => Tinted(x, y))).ToArray();
            var top = rows.Min(); var bottom = rows.Max();
            int Left(int y) => Enumerable.Range(0, frame.Width).First(x => Tinted(x, y));
            int Right(int y) => Enumerable.Range(0, frame.Width).Last(x => Tinted(x, y));
            var mid = (top + bottom) / 2;
            var bodyLeft = Left(mid); var bodyRight = Right(mid);
            var inkColumns = Enumerable.Range(bodyLeft, bodyRight - bodyLeft).Where(x => Enumerable.Range(top, bottom - top).Any(y => Ink(x, y))).ToArray();
            var inkRows = Enumerable.Range(top, bottom - top).Where(y => Enumerable.Range(bodyLeft, bodyRight - bodyLeft).Any(x => Ink(x, y))).ToArray();
            var notch = Enumerable.Range(bottom - 3, 4).Count(y => !Tinted(bodyRight - 6, y) || !Tinted(bodyRight - 7, y)) >= 2;
            return new Bubble(top, bottom, bodyLeft, bodyRight, Right(bottom), inkColumns.Min(), inkColumns.Max(),
                Left(top), Left(top + 2), Right(top), Right(bottom - 4) >= bodyRight, notch, frame.At(bodyLeft + 4, mid), inkRows);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    // Reference runtime, 2-chat-use.mov, measured at about 1 px = 1 pt (evidence in E:/Droppy/work/parity/evidence/TimelineRows):
    // - 102.000 s, "test 4 heads" over the opaque black window (crop 240x100 at 1460,1110; mac_bubble_102): the tinted shape
    //   34 tall; the body 104 wide = text ink 75 + 29 (ink 14 in from the body's left, 15 from its right); the tail tip 4
    //   past the body on the bottom row; full rounds on the top corners (row 0 starts 13 in, row 2 starts 8 in, top-right
    //   row 0 ends 12 in); the bottom-right corner flares outward from 6 rows above the bottom with a concave notch 6-7 in
    //   from the body edge in the last rows; fill 40, 9, 21 (the accent at 0.14 over 5, 5, 5). Port: port_bubble_short.png;
    //   side by side hstack_bubble_102.png.
    // - 168.0 s, "hi": the body 38 wide = ink + 29 (catalogue static.user-bubble, frames/gap-R2-03/hi_168_z10.png).
    // - 10.6-13 s, a long pasted message: the body widens to 720 (left edge 96 inside the 820 column, right edge 4 inside it,
    //   the tail tip on the column edge), the text wraps at a 16-point line pitch (catalogue static.user-bubble,
    //   frames/gap-R2-03/panel_bl_10.6_z4.png, panel_br_z14.png). Port: port_bubble_long.png.
    // The text face is the port's ruled family (OD-0009 Inter for SF Pro), so ink widths differ from the video's; every
    // inset, the 29-point hug, the tail, the corners, the 720 cap and the tint are compared, not glyph shapes.
    [Fact]
    [Trait("Req", "PR-CHAT-077")] // TimelineRows.swift:118-129, :163-164, :1167-1207
    public void The_sent_bubble_matches_the_reference_frames_at_three_text_lengths() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var previous = ThemeManager.Current;
        try
        {
            h.Main.OnMain(() =>
            {
                ThemeManager.Current = AppTheme.Monokai;
                foreach (var (text, name) in new[] { ("hi", "port_bubble_hi"), ("test 4 heads", "port_bubble_short") })
                {
                    var b = RenderBubble(h, text, name);
                    Assert.InRange(b.Bottom - b.Top + 1, 33, 35);                 // single line: 9 + one 16-point line + 9
                    Assert.InRange(b.InkLeft - b.BodyLeft, 13, 15);               // :124 14 leading
                    Assert.InRange(b.BodyRight - b.InkRight, 13, 16);             // :126 14 trailing (the tail hangs past it)
                    Assert.InRange(b.BodyRight - b.BodyLeft + 1 - (b.InkRight - b.InkLeft + 1), 27, 31); // hugs: ink + 29
                    Assert.InRange(b.TipRight - b.BodyRight, 3, 5);               // the tail tip, 4 past the body
                    Assert.InRange(b.TipRight, 818, 820);                              // trailing-aligned: the tip on the column edge
                    Assert.InRange(b.TopRowLeft - b.BodyLeft, 12, 14);            // a full 18-point round, top-left
                    Assert.InRange(b.ThirdRowLeft - b.BodyLeft, 7, 9);
                    Assert.InRange(b.BodyRight - b.TopRowRight, 11, 14);          // and top-right
                    Assert.True(b.Flares, "the bottom-right corner flares outward instead of rounding in");
                    Assert.True(b.Notch, "no notch under the tail");
                    Assert.InRange(b.Fill.R, 36, 44); Assert.InRange(b.Fill.G, 7, 12); Assert.InRange(b.Fill.B, 17, 24);
                }
                var longText = string.Join(" ", Enumerable.Repeat("A long pasted message wraps inside the bubble at its widest.", 30));
                var wide = RenderBubble(h, longText, "port_bubble_long");
                // :164 .padding(.leading, 96): never wider than 720; the bubble hugs its longest wrapped line, so a long text sits
                // within a few points of the cap (the video's panel lines reach 1100, this text's longest line 3 points short).
                Assert.InRange(wide.BodyLeft, 95, 104);
                Assert.InRange(wide.BodyRight, 814, 816);                               // 720 wide, 4 inside the column
                Assert.InRange(wide.TipRight, 818, 820);
                Assert.InRange(wide.InkLeft - wide.BodyLeft, 13, 15);
                // Wrapped lines at a 16-point pitch: the first ink row of each line, one line apart.
                var lineStarts = wide.InkRows.Where((y, i) => i == 0 || y - wide.InkRows[i - 1] > 3).ToArray();
                Assert.True(lineStarts.Length >= 4, "the long message did not wrap");
                Assert.All(lineStarts.Zip(lineStarts.Skip(1)), pair => Assert.InRange(pair.Second - pair.First, 15, 17));
            });
        }
        finally { h.Main.OnMain(() => ThemeManager.Current = previous); }
    });
}
