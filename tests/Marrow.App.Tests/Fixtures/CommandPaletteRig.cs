using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Views.Palette;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The shared rig of the command-palette proof classes (the CommandPalette*Tests files beside CommandPaletteTests):
/// mount a CommandPalette in a headless window with the model in the environment, settle layout, split a row into
/// the parts the Swift row builds, and hand a fresh ShortcutStore. Source: DroppyCode/App/Views/Palette/CommandPalette.swift
/// (B3 52bc792), :33-147.
/// </summary>
public static class CommandPaletteRig
{
    public static (Border Host, AvaloniaWindow Window, CommandPalette Palette) Build(AppModel model, ThemeVariant? theme = null, double width = 900, double height = 700)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var palette = new CommandPalette();
        host.Child = palette;
        var window = new AvaloniaWindow { Content = host, Width = width, Height = height };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Settle(window);
        return (host, window, palette);
    }

    public static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>A row's parts, in the layout order the row builds them: an icon slot, the text stack (title, then
    /// subtitle) and the chord, inside the padded rounded background Border.</summary>
    public static (Panel Icon, TextBlock Title, MiddleTruncatingTextBlock Subtitle, TextBlock Chord, Border Background, StackPanel TextStack, Grid Grid) RowParts(Control rowControl)
    {
        var decorator = Assert.IsAssignableFrom<Decorator>(rowControl);
        var background = Assert.IsType<Border>(decorator.Child);
        var grid = Assert.IsType<Grid>(background.Child);
        var icon = Assert.IsType<Panel>(grid.Children[0]);
        var textStack = Assert.IsType<StackPanel>(grid.Children[1]);
        var title = Assert.IsType<TextBlock>(textStack.Children[0]);
        var subtitle = Assert.IsType<MiddleTruncatingTextBlock>(textStack.Children[1]);
        var chord = Assert.IsType<TextBlock>(grid.Children[2]);
        return (icon, title, subtitle, chord, background, textStack, grid);
    }

    public static string[] Titles(CommandPalette palette) =>
        palette.RowsPanel.Children.Select(row => RowParts(row).Title.Text ?? "").ToArray();

    public static Control RowTitled(CommandPalette palette, string title) =>
        palette.RowsPanel.Children.First(row => RowParts(row).Title.Text == title);

    public static Point Centre(Visual control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    public static void Click(AvaloniaWindow window, Point point)
    {
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    public static void Press(AvaloniaWindow window, Key key, PhysicalKey physical, string text = "")
    {
        window.KeyPress(key, RawInputModifiers.None, physical, text);
        window.KeyRelease(key, RawInputModifiers.None, physical, text);
    }

    /// <summary>A ShortcutStore with no stored overrides (the shared one is replaced for the test's duration).</summary>
    public static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;
}
