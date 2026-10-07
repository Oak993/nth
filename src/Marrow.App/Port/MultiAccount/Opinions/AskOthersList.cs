// PORT-ONLY: locked page :4805-4832, B multi-select list, exact account/model label and @all.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class AskOthersList : Decorator
{
    private readonly AskOthersModel model;
    private readonly StackPanel rows = new() { Spacing = 2 };
    private readonly Dictionary<Guid, Button> kept = [];
    private readonly Button all;
    internal IReadOnlyList<Guid> AccountIds => model.Rows.Select(r => r.Account.Id).ToArray();
    internal IReadOnlyList<string> RowLabels => model.Rows.Select(r => r.Text).ToArray();
    internal AskOthersList(AskOthersModel model, Action close)
    {
        this.model = model; Width = 270;
        all = Make("@all", "Everyone with room", "ask-others-all", model.ToggleAll);
        Child = new Border { Padding = new Thickness(6), Child = new StackPanel { Spacing = 4, Children = { LibraryUi.Text(model.ListHeading, 12, secondary: true), rows, all } } };
        AttachedToVisualTree += (_, _) => { model.Changed += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => model.Changed -= Changed;
        KeyDown += (_, e) => { if (e.Key != Key.Escape) return; close(); e.Handled = true; };
        Refresh();
    }
    private void Changed() { if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Refresh(); else Avalonia.Threading.Dispatcher.UIThread.Post(Refresh); }
    private void Refresh()
    {
        foreach (var id in kept.Keys.Where(id => !model.Rows.Any(r => r.Account.Id == id)).ToArray()) { rows.Children.Remove(kept[id]); kept.Remove(id); }
        var index = 0;
        foreach (var row in model.Rows)
        {
            if (!kept.TryGetValue(row.Account.Id, out var button))
            {
                var id = row.Account.Id; button = Make(row.Account.Mono + "  " + row.Account.Name, row.Model, "ask-others-" + id.ToString("N"), () => model.Toggle(id));
                kept.Add(id, button); rows.Children.Add(button);
            }
            button.IsEnabled = row.CanAsk && !model.Busy && model.Confirmation is null;
            button.Named(row.Text + (row.Selected ? ", selected" : ""), "ask-others-" + row.Account.Id.ToString("N"));
            if (button.Content is Grid grid)
            {
                if (grid.Children[0] is StackPanel words) { ((TextBlock)words.Children[0]).Text = row.Account.Mono + "  " + row.Account.Name; ((TextBlock)words.Children[1]).Text = row.Model; }
                if (grid.Children.Last() is TextBlock tick) tick.Text = row.Selected ? "\u2713" : "";
            }
            rows.Children.Move(rows.Children.IndexOf(button), index++);
        }
        all.IsEnabled = !model.Busy && model.Confirmation is null && model.Rows.Any(r => r.CanAsk);
        if (all.Content is Grid allGrid && allGrid.Children.Last() is TextBlock allTick) allTick.Text = model.AllSelected ? "✓" : "";
    }
    private static Button Make(string name, string detail, string id, Action click)
    {
        var words = new StackPanel { Children = { LibraryUi.Text(name, 12.5), LibraryUi.Text(detail, 11, secondary: true) } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12"), Margin = new Thickness(8, 4), MinHeight = 36 }; row.Children.Add(words);
        var tick = LibraryUi.Text("", 12); Grid.SetColumn(tick, 1); row.Children.Add(tick);
        var button = PlainControls.Button(row, click); button.Focusable = true; button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; button.Named(name, id); return button;
    }
}
