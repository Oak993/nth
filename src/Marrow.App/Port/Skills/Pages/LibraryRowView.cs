// PORT-ONLY: first-release feature unit SK-15 (DL-17). One row of the library list: 30 points high, the set's glyph, its title and the status at its end; the selected row and the one under the pointer take
// a fill that eases in and out (OD-0076).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class LibraryRowView : Decorator
{
    internal const double RowHeight = 30;

    private readonly Border surface;
    private readonly AnimatedColor fill;
    private readonly TextBlock title;
    private readonly StatusMark mark = new();
    private bool selected, hovered;

    public LibraryRowView(LibraryModel model, LibraryRow row, Action? selecting = null)
    {
        Id = row.Id;
        // ED-NEW-dlhonest-1 (owner 2026-10-11): a title wraps instead of being cut ("Anthropic know…"); a one-line row stays 30 high, a longer title makes its row taller.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto"), ColumnSpacing = 9, MinHeight = RowHeight };
        var tile = LibraryUi.Tile(model.Tools ? "wrench.and.screwdriver" : row.Plugins ? "powerplug" : "wand.and.sparkles", 22, 6);
        title = LibraryUi.Text(row.Title, 12.5, trim: true, wrap: true);
        title.MaxLines = 3;
        title.Margin = new Thickness(0, 6);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        Grid.SetColumn(mark, 2);
        grid.Children.Add(tile);
        grid.Children.Add(title);
        grid.Children.Add(mark);
        surface = new Border { Child = grid, Padding = new Thickness(8, 0), Background = Brushes.Transparent }.RoundedRectangle(8, RoundedCornerStyle.Continuous);
        fill = new AnimatedColor(surface);
        surface.Background = fill.Brush;
        fill.Set(ChromeTokens.Overlay(0), null);
        Child = surface;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        this.Named(row.Title, "row-" + row.Id);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            selecting?.Invoke();
            model.Controller.Select(Id);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            selecting?.Invoke();
            model.Controller.Select(Id);
            e.Handled = true;
        };
        this.OnHover(over => { hovered = over; Refill(); });
        mark.Apply(row);
    }

    public string Id { get; }

    public StatusMark Mark => mark;

    public bool IsSelected => selected;

    public string Title => title.Text ?? "";

    public void Apply(LibraryRow row, bool isSelected)
    {
        title.Text = row.Title;
        AutomationProperties.SetName(this, row.Title);
        mark.Apply(row);
        if (selected == isSelected) return;
        selected = isSelected;
        Refill();
    }

    private void Refill() => fill.Set(ChromeTokens.Overlay(selected ? 0.12 : hovered ? 0.06 : 0), LibraryUi.Ease(this, Animation.EaseOut(duration: 0.15)));
}
