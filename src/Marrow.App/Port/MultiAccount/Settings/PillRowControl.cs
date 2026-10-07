// PORT-ONLY: T11 step 2. One pill in the list of Settings > Pills: the pill drawn as a pill (its name and its steps' marks), the steps in words under it, and a ⋯ menu (Edit, Duplicate, Rename, Delete). Pressing the row edits the pill. Delete asks
// first, in the row: "Delete “name”? Threads on it go back to your accounts in order." with Cancel and Delete (the page's confirmation); Rename edits the name in place (up to 24 characters).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class PillRowControl : Decorator
{
    private readonly PillsSettingsModel model;
    private readonly Guid id;
    private readonly Action<Guid> edit;
    private readonly TextBlock name = LibraryUi.Text("", 13, weight: SwiftFont.Weight.Medium);
    private readonly Border nameHost = new();
    private readonly PillShape shape;
    private readonly CzCrossfadeText summary = new("") { FontSize = 12, Secondary = true };
    private readonly CzCrossfadeText confirmText = new("") { FontSize = 12, Wrap = true };
    private readonly CzFoldRow confirmFold;
    private PillRowView view;
    private bool renaming;

    internal PillRowControl(PillsSettingsModel model, PillRowView first, Action<Guid> edit)
    {
        this.model = model;
        this.edit = edit;
        id = first.Id;
        view = first;
        nameHost.Child = name;
        shape = new PillShape(nameHost);
        var key = id.ToString("N");
        var more = MenuPopover.More("More", "pill-more-" + key, () =>
            [new("edit", MultiAccountWords.Edit), new("duplicate", MultiAccountWords.Duplicate), new("rename", MultiAccountWords.Rename), MenuEntry.Line, new("delete", MultiAccountWords.Delete, Quiet: true)], Pick);
        more.VerticalAlignment = VerticalAlignment.Center;
        var words = new StackPanel { Spacing = 4, Children = { shape, summary } };
        var open = PlainControls.Button(new Border { Background = Avalonia.Media.Brushes.Transparent, Padding = new Thickness(0, 10), Child = words }, () => { if (!renaming) edit(id); });
        open.Focusable = true;
        open.HorizontalAlignment = HorizontalAlignment.Stretch;
        open.HorizontalContentAlignment = HorizontalAlignment.Left;
        open.Cursor = new Cursor(StandardCursorType.Hand);
        open.Named(first.Name, "pill-row-" + key);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,22"), ColumnSpacing = 8 };
        Grid.SetColumn(more, 1);
        line.Children.Add(open);
        line.Children.Add(more);

        var cancel = LibraryButtons.Make(MultiAccountWords.Cancel, () => confirmFold!.Open = false, ButtonLook.Plain, "pill-delete-cancel-" + key);
        var delete = LibraryButtons.Make(MultiAccountWords.Delete, () => model.Delete(id), ButtonLook.Danger, "pill-delete-confirm-" + key);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, delete } };
        confirmFold = new CzFoldRow(LibraryUi.Panel(new StackPanel { Spacing = 8, Children = { confirmText, buttons } }, 10, 0.06, new Thickness(12, 10)), false);
        Child = new Border { Padding = new Thickness(16, 0, 12, 0), Child = new StackPanel { Children = { line, confirmFold } } };
        Apply(first);
    }

    internal Guid Id => id;

    internal PillRowView View => view;

    internal PillShape Shape => shape;

    internal string NameShown => name.Text ?? "";

    internal string SummaryShown => summary.Text;

    internal bool ConfirmOpen => confirmFold.Open;

    internal bool Renaming => renaming;

    internal void Apply(PillRowView next)
    {
        view = next;
        if (!renaming) name.Text = next.Name;
        shape.Apply(next.Steps);
        summary.SetText(next.Summary);
        ToolTip.SetTip(this, next.Tooltip);
        AutomationProperties.SetName(this, next.Name);
        confirmText.SetText(MultiAccountWords.ConfirmDelete(next.Name));
    }

    internal void Pick(string key)
    {
        switch (key)
        {
            case "edit": edit(id); break;
            case "duplicate": model.Duplicate(id); break;
            case "rename": StartRename(); break;
            case "delete": confirmFold.Open = true; break;
        }
    }

    private void StartRename()
    {
        if (renaming) return;
        renaming = true;
        var box = new TextBox { Text = view.Name, MaxLength = 24, MinWidth = 130, FontSize = 13, Padding = new Thickness(4, 1) };
        AutomationProperties.SetName(box, MultiAccountWords.PillNameLabel);
        AutomationProperties.SetAutomationId(box, "pill-rename-" + id.ToString("N"));
        var done = false;
        void Finish(bool commit)
        {
            if (done) return;
            done = true;
            renaming = false;
            if (commit) model.Rename(id, box.Text ?? "");
            nameHost.Child = name;
            name.Text = model.Rows.FirstOrDefault(r => r.Id == id)?.Name ?? view.Name;
        }
        box.KeyDown += (_, e) =>
        {
            e.Handled = true;
            if (e.Key == Key.Enter) Finish(true);
            else if (e.Key == Key.Escape) Finish(false);
        };
        box.LostFocus += (_, _) => Finish(true);
        nameHost.Child = box;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
    }
}
