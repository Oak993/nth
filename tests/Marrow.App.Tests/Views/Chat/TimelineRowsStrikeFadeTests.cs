using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// PR-CHAT-114, the strike's open clause (ED-285 second round (2), ED-314 (1)): TimelineRows.swift:2357 `.strikethrough(step.status == .done, color: .secondary)`
// changes under :2372 `.animation(.snappy(duration: 0.2), value: steps)`, and no reference frame shows whether the line draws across or fades. OD-0105 (2),
// the owner's pick 2026-10-05: "Fade in". Sampled every 10 ms frame of the change: the strike is at its full length from its first frame (the pixels it
// darkens at the step's start and at its end are the same share of their final change, frame by frame, where a line drawn across would reach the end
// last) and its colour rises from clear to .secondary along the change, through the frames between, never in one frame.
[Collection("Headless")]
public sealed class TimelineRowsStrikeFadeTests(HeadlessSession headless)
{
    // The frame's pixels as packed RGBA.
    private static (int Width, byte[] Pixels) Frame(AvaloniaWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return (buffer.RowBytes / 4, bytes);
    }

    private static int Distance(byte[] a, byte[] b, int at) =>
        Math.Abs(a[at] - b[at]) + Math.Abs(a[at + 1] - b[at + 1]) + Math.Abs(a[at + 2] - b[at + 2]);

    private static int DistanceTo(byte[] a, int at, byte[] colour) =>
        Math.Abs(a[at] - colour[0]) + Math.Abs(a[at + 1] - colour[1]) + Math.Abs(a[at + 2] - colour[2]);

    [Fact]
    [Trait("Req", "PR-CHAT-114")] // TimelineRows.swift:2357, :2372; OD-0105 (2) the strike fades in
    public void Checking_a_step_off_fades_its_strike_in_at_full_length_along_the_cards_snappy_change() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        const string words = "a b c d e f g h i j k l m n o p";
        TimelineEntry entry = null!;
        TodoListRow row = null!;
        AvaloniaWindow window = null!;
        TextBlock text = null!;
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps((words, TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(new Border { Child = row, Width = 600 });
            text = Mounted<TextBlock>(row).Single(block => block.Text == words);
        });
        try
        {
            var (width, before) = h.Main.OnMain(() => Frame(window));
            var box = h.Main.OnMain(() => new Rect(text.TranslatePoint(default, window)!.Value, text.Bounds.Size));
            double Alpha() => h.Main.OnMain(() => (text.TextDecorations ?? []).Where(decoration => decoration.Location == TextDecorationLocation.Strikethrough)
                .Select(decoration => decoration.Stroke is ISolidColorBrush solid ? solid.Color.A / 255.0 * solid.Opacity : 1).DefaultIfEmpty(0).Max());
            Assert.Equal(0, Alpha());

            h.Main.OnMain(() => entry.Item = entry.Item with { TimelineItemContent = Steps((words, TodoStep.Status.Done), ("Two", TodoStep.Status.Pending)) });
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            var sampled = new List<(double Alpha, byte[] Pixels)>();
            for (var elapsed = 10; elapsed <= 300; elapsed += 10)
            {
                h.Main.AdvanceMilliseconds(10);
                frames.Advance(TimeSpan.FromMilliseconds(10));
                h.Main.OnMain(() => Settle(window));
                sampled.Add((Alpha(), h.Main.OnMain(() => Frame(window).Pixels)));
            }
            Animate(h.Main, frames, 700);
            var after = h.Main.OnMain(() => Frame(window).Pixels);
            var end = Alpha();
            Assert.True(end > 0.3, $"the done step is not struck through once settled ({end:F3})");

            // The strike's own pixels: inside the text's box, the window's background before (the gaps between the letters) and darkened after.
            int Offset(int x, int y) => (y * width + x) * 4;
            var background = before.AsSpan(Offset((int)box.Right + 6, (int)(box.Y + box.Height / 2)), 4).ToArray();
            var strike = new List<(int X, int At)>();
            for (var y = (int)box.Y; y < (int)box.Bottom; y++)
                for (var x = (int)box.X; x < (int)box.Right; x++)
                {
                    var at = Offset(x, y);
                    if (DistanceTo(before, at, background) <= 2 && Distance(after, before, at) > 30) strike.Add((x, at));
                }
            var left = strike.Where(point => point.X < box.X + box.Width / 3).ToArray();
            var right = strike.Where(point => point.X > box.Right - box.Width / 3).ToArray();
            Assert.True(left.Length > 3 && right.Length > 3, $"the strike's pixels were not found across the step ({left.Length} at its start, {right.Length} at its end)");
            double Share(byte[] now, (int X, int At)[] points) => points.Average(point => (double)Distance(now, before, point.At) / Distance(after, before, point.At));

            var shares = sampled.Select(sample => (Start: Share(sample.Pixels, left), End: Share(sample.Pixels, right), sample.Alpha)).ToArray();
            for (var index = 0; index < shares.Length; index++)
            {
                var (start, finish, alpha) = shares[index];
                // the whole line at once: its end is as far in as its start in every frame
                Assert.True(Math.Abs(start - finish) < 0.12, $"frame {index + 1} (+{(index + 1) * 10} ms): the strike is {start:F2} in at the step's start and {finish:F2} at its end, so it is drawn across rather than faded");
                if (index > 0)
                {
                    Assert.True(alpha >= shares[index - 1].Alpha - 0.01, $"frame {index + 1}: the strike's colour fell back from {shares[index - 1].Alpha:F3} to {alpha:F3}");
                    Assert.True(start >= shares[index - 1].Start - 0.05, $"frame {index + 1}: the strike faded back from {shares[index - 1].Start:F2} to {start:F2}");
                }
            }
            // it fades: frames part way, with neither the first frame whole nor the change at once
            Assert.True(shares[0].Start < 0.8, $"the strike is {shares[0].Start:F2} in 10 ms into the 0.2 s change: it cut in");
            Assert.Contains(shares, share => share.Start is > 0.2 and < 0.8);
            Assert.Contains(shares, share => share.Alpha > 0.1 * end && share.Alpha < 0.9 * end);
            Assert.True(shares[^1].Start > 0.95, $"300 ms in, the strike is only {shares[^1].Start:F2} of the way in");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
