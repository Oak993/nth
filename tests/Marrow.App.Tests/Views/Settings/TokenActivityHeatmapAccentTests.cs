using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// OD-0088 (ED-253, round 4, F6): the heatmap's cells resolve Chrome.accent as the canvas draws (TokenActivitySection.swift:257,
// TokenActivityStyle.color :20-26), so a pick of the same appearance (no theme variant change, only ThemeManager.didChange)
// redraws them in the new accent. Outside a window that turns over (the Settings window) the change is at once.
[Collection("Headless")]
public sealed class TokenActivityHeatmapAccentTests(HeadlessSession headless)
{
    // The colour of one window pixel, as RGB (the frame's own channel order read from its format).
    private static Color Pixel(Avalonia.Controls.Window window, Point at)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var bytes = new byte[4];
        Marshal.Copy(buffer.Address + (int)at.Y * buffer.RowBytes + (int)at.X * 4, bytes, 0, 4);
        return buffer.Format == PixelFormat.Bgra8888
            ? Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0])
            : Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]);
    }

    private static void Near(Color expected, Color seen, string what) =>
        Assert.True(Math.Abs(expected.R - seen.R) <= 1 && Math.Abs(expected.G - seen.G) <= 1 && Math.Abs(expected.B - seen.B) <= 1,
            $"{what}: {seen}, expected {expected}");

    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_cell_drawn_in_the_picked_accent_takes_a_later_pick_of_the_same_appearance() => headless.OnUiThread(() =>
    {
        var (theme, choice) = (ThemeManager.Current, AccentChoice.Current);
        // One past day at the grid's maximum: its cell is the accent itself (:26), opaque.
        var built = TokenActivityGrid.Build(ValueMap<string, long>.Empty, TokenActivityMode.Daily, new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        var (column, row) = Enumerable.Range(built.Columns.Count * 2 / 5, built.Columns.Count)
            .SelectMany(c => Enumerable.Range(0, TokenActivityGrid.Rows).Select(r => (c, r)))
            .First(cell => built.Columns[cell.c][cell.r] is { } date && date < built.Tomorrow);
        var grid = built with
        {
            Values = new ValueList<ValueList<long>>(built.Values.Select((values, c) =>
                new ValueList<long>(values.Select((_, r) => c == column && r == row ? 10L : 0L)))),
            MaxValue = 10,
        };
        var canvas = new TokenActivityHeatmapCanvas(grid);
        var window = new Avalonia.Controls.Window
        {
            Width = 720, Height = 200, Background = Brushes.Black, RequestedThemeVariant = ThemeVariant.Dark,
            Content = new Border { Padding = new Thickness(20), Child = canvas },
        };
        try
        {
            ThemeManager.Current = AppTheme.System;
            AccentChoice.Current = "orchid";
            window.Show();
            window.UpdateLayout();
            var gap = TokenActivityStyle.CellGap;
            var cell = (canvas.Bounds.Width - gap * (grid.Columns.Count - 1)) / grid.Columns.Count;
            var at = canvas.TranslatePoint(new Point(column * (cell + gap) + cell / 2, row * (cell + gap) + cell / 2), window)!.Value;
            Near(AccentPalette.Pick("orchid")!.Resolve(ThemeVariant.Dark), Pixel(window, at), "drawn in Orchid");
            AccentChoice.Current = "teal"; // the same appearance: no theme variant change, ThemeManager.didChange only
            Near(AccentPalette.Pick("teal")!.Resolve(ThemeVariant.Dark), Pixel(window, at), "after the pick of Teal");
        }
        finally
        {
            window.Content = null;
            window.Close();
            AccentChoice.Current = choice;
            ThemeManager.Current = theme;
            Dispatcher.UIThread.RunJobs();
        }
    });
}
