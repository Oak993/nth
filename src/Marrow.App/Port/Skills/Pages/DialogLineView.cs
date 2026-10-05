// PORT-ONLY: first-release feature unit SK-15 (DL-17). One line of the Download all dialog's "Included" list: the set's name and, at the end, what is happening to it.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class DialogLineView : Decorator
{
    private readonly TextBlock name;
    private readonly StatusMark mark = new();

    public DialogLineView(RunLine line, bool plugins)
    {
        Id = line.Id;
        var tile = LibraryUi.Tile(plugins ? "powerplug" : "wand.and.sparkles", 20, 6);
        name = LibraryUi.Text(line.Title, 12.5, trim: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), ColumnSpacing = 9, Height = 28 };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(mark, 2);
        grid.Children.Add(tile);
        grid.Children.Add(name);
        grid.Children.Add(mark);
        Child = grid;
        this.Named(line.Title, "line-" + line.Id);
        Apply(line);
    }

    public string Id { get; }

    public StatusMark Mark => mark;

    public void Apply(RunLine line)
    {
        name.Text = line.Title;
        AutomationProperties.SetHelpText(this, line.Right);
        switch (line.State)
        {
            case RunLineState.Downloading: mark.Show(StatusMark.Mark.Ring, "", line.Fraction); break;
            case RunLineState.Checking: mark.Show(StatusMark.Mark.Spinner, line.Right, null); break;
            case RunLineState.Downloaded: mark.Show(StatusMark.Mark.Check, line.Right, null); break;
            case RunLineState.Failed: mark.Show(StatusMark.Mark.Warning, line.Right, null); break;
            default: mark.Show(StatusMark.Mark.Words, line.Right, null); break;
        }
    }
}
