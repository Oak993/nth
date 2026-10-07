// PORT-ONLY RR-18, locked sd-page.js:478: the separate Your own list retains its persisted identity.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Services.Port.Skills.Custom;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class CustomOwnRow : Decorator
{
    private readonly Border surface;
    private readonly AnimatedColor fill;
    private readonly TextBlock title;
    private bool selected, hovered;
    internal CustomOwnRow(CustomOwnModel model, CustomAddKind kind, OwnItem item)
    {
        title = LibraryUi.Text(item.Name, 12.5, trim: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        var mark = LibraryUi.Glyph("checkmark.circle.fill", 14, ChromeTokens.Success);
        mark.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto"), ColumnSpacing = 9, Height = 30,
            Children = { LibraryUi.Tile(Symbol(kind), 22, 6), title, mark } };
        Grid.SetColumn(title, 1); Grid.SetColumn(mark, 2);
        surface = new Border { Child = grid, Padding = new Thickness(8, 0) }.RoundedRectangle(8, RoundedCornerStyle.Continuous);
        fill = new AnimatedColor(surface);
        surface.Background = fill.Brush;
        Child = surface;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        this.Named(item.Name, "own-row-" + item.Id);
        PointerPressed += (_, e) => { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; model.Select(kind, item.Id); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key is not (Key.Enter or Key.Space)) return; model.Select(kind, item.Id); e.Handled = true; };
        this.OnHover(value => { hovered = value; Refill(); });
        Refill();
    }
    internal static string Symbol(CustomAddKind kind) => kind switch { CustomAddKind.Plugins => "powerplug", CustomAddKind.Skills => "wand.and.sparkles", _ => "wrench.and.screwdriver" };
    internal void Apply(OwnItem item, bool isSelected) { title.Text = item.Name; selected = isSelected; Refill(); }
    private void Refill() => fill.Set(ChromeTokens.Overlay(selected ? .12 : hovered ? .06 : 0), LibraryUi.Ease(this, Animation.EaseOut(.15)));
}
