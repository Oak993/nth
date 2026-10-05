// PORT-ONLY: first-release feature unit SK-15 (DL-17, OD-0099 progressive disclosure). The "In every session" box of a detail: one switch per skill of a set (per plugin folder of a plugin item) and the words that say
// what a switch does. A switch slides on the frame clock (the app's switch); the count at the box's top right crossfades. A set that is not downloaded shows its skills with the switches waiting.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class SwitchesBox : Decorator
{
    private readonly LibraryModel model;
    private readonly string id;
    private readonly CzCrossfadeText listed = new("") { FontSize = 12, Secondary = true };
    private readonly List<(string Key, Control Row, AnimatedScalar Dim)> rows = [];

    public SwitchesBox(LibraryModel model, LibraryRow row)
    {
        this.model = model;
        id = row.Id;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 6) };
        var title = LibraryUi.Text(LibraryWords.InEverySession, 12, weight: SwiftFont.Weight.Semibold);
        listed.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(listed, 1);
        heading.Children.Add(title);
        heading.Children.Add(listed);
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(heading);
        if (row.Switches.Count > 0)
        {
            var list = new StackPanel { Spacing = 0 };
            foreach (var line in row.Switches) list.Children.Add(Row(line));
            stack.Children.Add(LibraryUi.Scroller(list, 150));
        }
        var note = LibraryUi.Text(LibraryWords.SwitchNote, 11.5, secondary: true, wrap: true);
        note.Margin = new Thickness(0, 7, 0, 0);
        stack.Children.Add(note);
        Child = LibraryUi.Panel(stack);
        Apply(row);
    }

    public string ListedText => listed.Text;

    public IReadOnlyList<(string Key, Control Row, AnimatedScalar Dim)> Rows => rows;

    private Control Row(SwitchLine line)
    {
        var key = line.Key;
        var toggle = new SettingsSwitch(new Binding<bool>(() => { _ = model.Revision; return Current(key)?.On ?? false; }, value => model.Controller.SetSwitch(key, value)));
        toggle.Named(line.Label, "switch-" + key);
        var name = LibraryUi.Mono(line.Label, 11.5);
        var detail = LibraryUi.Text(line.Detail, 11, secondary: true, trim: true);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,Auto"), ColumnSpacing = 8, Height = 28 };
        name.VerticalAlignment = VerticalAlignment.Center;
        detail.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        Grid.SetColumn(detail, 2);
        grid.Children.Add(toggle);
        grid.Children.Add(name);
        grid.Children.Add(detail);
        rows.Add((key, grid, new AnimatedScalar(grid, 0.45, v => grid.Opacity = v)));
        return grid;
    }

    private SwitchLine? Current(string key) => model.Controller.Rows.FirstOrDefault(r => r.Id == id)?.Switches.FirstOrDefault(l => l.Key == key);

    public void Apply(LibraryRow row)
    {
        listed.SetText(row.ListedText);
        var enabled = row.OnThisPc;
        foreach (var (_, grid, dim) in rows)
        {
            grid.IsEnabled = enabled;
            dim.Set(enabled ? 1 : 0.45, LibraryUi.Ease(this, LibraryUi.Fade));
        }
    }
}
