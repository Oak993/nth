using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-lean-21 (lean pass L21): the heatmap draws its 371 cells with one cached brush per colour and keeps its cell buffer
// between frames, so a frame allocates (almost) nothing, and it still draws exactly what a brush per cell drew.
// Budgets are bytes allocated on the UI thread per Render into a headless Skia drawing context (Debug build, after a warm-up).
[Collection("Headless")]
public sealed class TokenActivityHeatmapBudgetTests(HeadlessSession headless)
{
    private const double Width = 700;

    // A mid-year grid (future cells included) with every ladder step, and a second grid to blend from.
    private static (TokenActivityGrid Grid, TokenActivityGrid Previous) Grids()
    {
        var built = TokenActivityGrid.Build(ValueMap<string, long>.Empty, TokenActivityMode.Daily, new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        TokenActivityGrid With(Func<int, int, long> value) => built with
        {
            Values = new ValueList<ValueList<long>>(built.Values.Select((values, c) => new ValueList<long>(values.Select((_, r) => value(c, r))))),
            MaxValue = 8,
        };
        return (With((c, r) => (c * 7 + r) % 9), With((c, r) => (c + r * 3) % 9));
    }

    // The canvas laid out at a fixed width, under the given appearance.
    private static (TokenActivityHeatmapCanvas Canvas, PixelSize Size) Laid(TokenActivityGrid grid, ThemeVariant theme)
    {
        var canvas = new TokenActivityHeatmapCanvas(grid) { UseLayoutRounding = false };
        var scope = new ThemeVariantScope { RequestedThemeVariant = theme, Child = canvas };
        scope.Measure(new Size(Width, double.PositiveInfinity));
        scope.Arrange(new Rect(scope.DesiredSize));
        Assert.Equal(theme, canvas.ActualThemeVariant);
        return (canvas, new PixelSize((int)Math.Ceiling(canvas.Bounds.Width), (int)Math.Ceiling(canvas.Bounds.Height)));
    }

    private static long BytesPerFrame(TokenActivityHeatmapCanvas canvas, PixelSize size, Action<int> before)
    {
        const int Frames = 48;
        using var bitmap = new RenderTargetBitmap(size);
        using var context = bitmap.CreateDrawingContext();
        for (var i = 0; i < 60; i++) { before(i); canvas.Render(context); } // warm-up: every colour of the sweep seen once
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Frames; i++) { before(i); canvas.Render(context); }
        return (GC.GetAllocatedBytesForCurrentThread() - start) / Frames;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-21")]
    public void A_steady_heatmap_frame_allocates_within_its_budget() => headless.OnUiThread(() =>
    {
        var (grid, _) = Grids();
        var (canvas, size) = Laid(grid, ThemeVariant.Dark);
        var bytes = BytesPerFrame(canvas, size, _ => { });
        Assert.True(bytes <= SteadyFrameBytes, $"a steady heatmap frame allocated {bytes} B (budget {SteadyFrameBytes} B)");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-21")]
    public void A_blending_heatmap_frame_allocates_within_its_budget() => headless.OnUiThread(() =>
    {
        var (grid, previous) = Grids();
        var (canvas, size) = Laid(grid, ThemeVariant.Light);
        // A mode switch held at one blend step per frame, the same twelve steps over and over (a brush cache can keep them).
        var bytes = BytesPerFrame(canvas, size, i => canvas.Update(grid, previous, (i % 12 + 0.5) / 12, 1));
        Assert.True(bytes <= BlendFrameBytes, $"a blending heatmap frame allocated {bytes} B (budget {BlendFrameBytes} B)");
    });

    // Pixel-equality guard: at 24 blend steps in each appearance the canvas draws byte for byte what the brush-per-cell drawing
    // drew before ED-NEW-lean-21 (reproduced below from TokenActivitySection.swift:243-271 as the port drew it).
    [Fact]
    [Trait("Decision", "ED-NEW-lean-21")]
    public void The_heatmap_draws_pixel_equal_to_a_brush_per_cell_drawing() => headless.OnUiThread(() =>
    {
        var (grid, previous) = Grids();
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var (canvas, size) = Laid(grid, theme);
            for (var step = 0; step <= 24; step++)
            {
                var blend = step / 24.0;
                canvas.Update(grid, step == 24 ? null : previous, blend, 1);
                var drawn = Pixels(size, canvas.Render);
                var expected = Pixels(size, context => BrushPerCell(context, canvas.Bounds.Width, grid, step == 24 ? null : previous, blend, theme));
                Assert.True(drawn.AsSpan().SequenceEqual(expected), $"{theme} blend {blend:F3}: the heatmap differs from the brush-per-cell drawing");
            }
        }
    });

    private static byte[] Pixels(PixelSize size, Action<DrawingContext> draw)
    {
        using var bitmap = new RenderTargetBitmap(size);
        using (var context = bitmap.CreateDrawingContext()) draw(context);
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), buffer, bytes.Length, stride);
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return bytes;
    }

    private static void BrushPerCell(DrawingContext context, double width, TokenActivityGrid grid, TokenActivityGrid? previous, double blend, ThemeVariant theme)
    {
        var columns = grid.Columns.Count;
        var gap = TokenActivityStyle.CellGap;
        var cell = (width - gap * (columns - 1)) / columns;
        var pitch = cell + gap;
        var progress = Math.Min(Math.Max(blend, 0), 1);
        for (var c = 0; c < columns; c++)
            for (var r = 0; r < TokenActivityGrid.Rows; r++)
            {
                if (grid.Columns[c][r] is not { } date) continue;
                var future = date >= grid.Tomorrow;
                var color = TokenActivityStyle.Color(grid.Values[c][r], grid.MaxValue, future).Resolve(theme);
                if (previous is { } prev && progress < 1 && c < prev.Values.Count)
                {
                    var old = TokenActivityStyle.Color(prev.Values[c][r], prev.MaxValue, future).Resolve(theme);
                    color = progress <= 0 ? old : TokenActivityHeatmapCanvas.Mix(old, color, progress);
                }
                context.DrawRectangle(new SolidColorBrush(color), null, new Rect(c * pitch, r * pitch, cell, cell), TokenActivityStyle.CellRadius, TokenActivityStyle.CellRadius);
            }
    }

    // Debug build. Before the fix (measured on claude/release-integration 0a946c558) and after; see ED-NEW-lean-21.
    private const long SteadyFrameBytes = 208; // measured 291,728 B before the fix, 160 B after (the platform accent read; see ED-NEW-lean-21)
    private const long BlendFrameBytes = 208; // measured 357,248 B before the fix, 160 B after
}
