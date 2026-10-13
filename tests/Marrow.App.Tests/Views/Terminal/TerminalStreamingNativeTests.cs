using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.UI.Port;
using Xunit;
using Xunit.Abstractions;

namespace Marrow.App.Tests.Views.Terminal;

// ED-NEW-perf-motion-1: what a streaming output frame costs in wall-clock time over a full scrollback, measured rather than counted. Each
// frame applies one new line, lays the terminal out and paints it into a bitmap, for two seconds of 60 Hz frames, against what the same
// frame cost before: the per-cell paint (LegacyTerminalPaint, the old Render verbatim) and the whole scrollback laid out as text (the
// old semantic SelectableTextBlock; the old string building is left out, so the comparison favours the old path). Wall-clock time
// depends on the machine and on what else runs, so it is a Native test (the lead runs it in a quiet window).
[Collection("Headless")]
public sealed class TerminalStreamingNativeTests(HeadlessSession headless, ITestOutputHelper output)
{
    private const int Width = 900;
    private const int Height = 480;

    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-1")]
    public void Native_a_streaming_output_frame_over_a_full_scrollback_costs_a_third_of_the_old_frame_or_less()
    {
        headless.OnUiThread(() =>
        {
            var view = new TerminalHostView
            {
                Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular),
                NativeForegroundColor = new SwiftColor(1, 1),
                NativeBackgroundColor = new SwiftColor(0, 1),
                CaretColor = new SwiftColor(0, 1),
            };
            Output(view, Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 600).Select(i =>
                $"history line {i:D4} \x1b[32mgreen\x1b[0m \x1b[1mbold\x1b[0m \x1b[4;33munderlined\x1b[0m some plain words to fill the row\r\n"))));
            Lay(view);
            var surface = (TerminalSurface)view.Child!;
            using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
            var oldText = new SelectableTextBlock { FontFamily = surface.TextControl.FontFamily, FontSize = surface.TextControl.FontSize };
            var now = new List<double>();
            var before = new List<double>();
            for (var frame = 0; frame < 120; frame++)
            {
                var line = Encoding.UTF8.GetBytes($"compiling unit {frame:D3} \x1b[32mok\x1b[0m {new string('.', frame % 40)}\r\n");
                var clock = Stopwatch.StartNew();
                Output(view, line);
                Lay(view);
                using (var context = bitmap.CreateDrawingContext()) surface.Render(context);
                now.Add(clock.Elapsed.TotalMilliseconds);

                clock.Restart();
                oldText.Text = surface.Text; // the old join (TerminalPaintBudgetTests: the text equals it)
                oldText.Measure(new Size(Width, double.PositiveInfinity));
                oldText.Arrange(new Rect(oldText.DesiredSize));
                using (var context = bitmap.CreateDrawingContext()) LegacyTerminalPaint.Render(surface, context);
                before.Add(clock.Elapsed.TotalMilliseconds);
            }
            var (frameNow, frameBefore) = (Median(now), Median(before));
            output.WriteLine($"median frame {frameNow:0.00} ms, the old frame {frameBefore:0.00} ms; slowest {now.Max():0.00} ms against {before.Max():0.00} ms");
            Assert.True(frameNow * 3 <= frameBefore, $"a streaming frame costs {frameNow:0.00} ms, the old one {frameBefore:0.00} ms");
        });
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted[sorted.Length / 2];
    }

    private static void Lay(TerminalHostView view)
    {
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
    }

    private static void Output(TerminalHostView view, byte[] bytes)
    {
        typeof(TerminalHostView).GetMethod("OnOutput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [bytes]);
        Dispatcher.UIThread.RunJobs();
    }
}
