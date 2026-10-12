using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.UI.Port;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Marrow.App.Tests.Views.Terminal;

// ED-NEW-perf-motion-1: an output frame costs what it changed, not the scrollback. The grid's paint is kept per row while the row shows, the
// semantic text reuses each row's line and is never laid out, and the cell is measured once per font and scale; the paint stays pixel-equal to
// the per-cell painting it replaces (LegacyTerminalPaint).
[Collection("Headless")]
public sealed class TerminalPaintBudgetTests(HeadlessSession headless, ITestOutputHelper output)
{
    private const int Width = 640;
    private const int Height = 240;

    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-1")]
    public void The_row_painter_draws_pixel_equal_to_the_per_cell_painting()
    {
        headless.OnUiThread(() =>
        {
            var view = MakeView();
            Output(view, Encoding.UTF8.GetBytes(
                "plain ASCII text, WMW __ ~~ ||\r\n" +
                "\x1b[1mbold\x1b[0m \x1b[4munderlined run\x1b[0m \x1b[7minverse\x1b[0m \x1b[31;44mred on blue\x1b[0m \x1b[1;4;32mall three\x1b[0m\r\n" +
                "wide 中文 emoji \U0001f642 combining é box ┌──┐ │x│ └──┘\r\n" +
                "\x1b[38;5;208mindexed\x1b[0m \x1b[48;5;22m background run \x1b[0m end"));
            var surface = Arrange(view);
            surface.TextControl.SelectionStart = 8;
            surface.TextControl.SelectionEnd = 70;
            var painted = Pixels(surface.Render);
            var legacy = Pixels(context => LegacyTerminalPaint.Render(surface, context));
            var differing = painted.Zip(legacy).Count(pair => pair.First != pair.Second);
            output.WriteLine($"{differing} of {painted.Length} pixels differ");
            Assert.True(painted.Distinct().Count() > 4, "the grid painted");
            Assert.True(surface.Painter.GlyphRunsBuilt > 0 && surface.Painter.ClippedCellsBuilt > 0, "both the merged and the clipped paths drew");
            Assert.Equal(legacy, painted);
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-1")]
    public void A_repaint_shapes_nothing_and_an_output_frame_rebuilds_only_the_rows_it_changed()
    {
        headless.OnUiThread(() =>
        {
            var view = MakeView();
            Output(view, Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 600).Select(i => $"history line {i:D4} \x1b[32mgreen\x1b[0m\r\n"))));
            var surface = Arrange(view);
            Pixels(surface.Render);
            var painter = surface.Painter;
            var (rows, texts) = (painter.RowsBuilt, painter.TextsShaped);
            Pixels(surface.Render);
            Assert.Equal(rows, painter.RowsBuilt);
            Assert.Equal(texts, painter.TextsShaped);

            Output(view, "\x1b[5;1Hchanged"u8.ToArray());
            Pixels(surface.Render);
            Assert.Equal(rows + 1, painter.RowsBuilt);
            Assert.True(painter.RowsHeld <= RowCount(surface), $"{painter.RowsHeld} row paints held");
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-1")]
    public void An_output_frame_over_a_full_scrollback_makes_only_the_changed_line_and_lays_out_no_text()
    {
        headless.OnUiThread(() =>
        {
            var view = MakeView();
            Output(view, Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 600).Select(i => $"history line {i:D4}\r\n"))));
            var surface = Arrange(view);
            var built = surface.RowTextsBuilt;
            Output(view, "\x1b[5;1Hchanged"u8.ToArray());
            Assert.Equal(built + 1, surface.RowTextsBuilt);
            Assert.Equal(LegacyText(surface), surface.Text);
            Assert.Contains("history line 0599", surface.Text, StringComparison.Ordinal);

            view.UpdateLayout();
            var text = Assert.IsType<TerminalSemanticText>(surface.TextControl);
            Assert.Single(text.TextLayout.TextLines);
            Assert.True(text.TextLayout.TextLines[0].Length <= 1, "the semantic text lays out nothing");
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-1")]
    public void The_cell_is_measured_once_per_font_and_not_per_paint_or_layout()
    {
        headless.OnUiThread(() =>
        {
            var view = MakeView();
            var surface = Arrange(view);
            for (var i = 0; i < 5; i++)
            {
                Output(view, Encoding.UTF8.GetBytes($"frame {i}\r\n"));
                view.UpdateLayout();
                Pixels(surface.Render);
            }
            Assert.Equal(1, view.CellMeasures);
            view.Font = SwiftFont.MonospacedSystemFont(14, SwiftFont.Weight.Regular);
            view.UpdateLayout();
            Pixels(surface.Render);
            Assert.Equal(2, view.CellMeasures);
        });
    }

    private static TerminalHostView MakeView() => new()
    {
        Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular),
        NativeForegroundColor = new SwiftColor(1, 1),
        NativeBackgroundColor = new SwiftColor(0, 1),
        CaretColor = new SwiftColor(0, 1),
    };

    private static TerminalSurface Arrange(TerminalHostView view)
    {
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
        return (TerminalSurface)view.Child!;
    }

    private static int RowCount(TerminalSurface surface) =>
        ((TerminalFrame)typeof(TerminalSurface).GetField("frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!).Rows;

    private static string LegacyText(TerminalSurface surface)
    {
        var frame = (TerminalFrame)typeof(TerminalSurface).GetField("frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
        return string.Join('\n', frame.Lines.Select(line => string.Concat(line.Select(cell => cell.Text ?? " ")).TrimEnd()));
    }

    private static SKColor[] Pixels(Action<DrawingContext> render)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
        using (var context = bitmap.CreateDrawingContext()) render(context);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(stream.ToArray());
        return pixels.Pixels;
    }

    // The production ProcessPipe.ReadabilityHandler boundary, then the frame it posts.
    private static void Output(TerminalHostView view, byte[] bytes)
    {
        typeof(TerminalHostView).GetMethod("OnOutput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [bytes]);
        Dispatcher.UIThread.RunJobs();
    }
}
