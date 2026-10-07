// PORT-ONLY: first-release feature unit SK-15 (DL-17). The action row of a detail: Download, Waiting, the bar with its cancel, Checking, Installed with the trash, Update, Repair, Retry and the remove confirmation. A change of state
// crossfades (OD-0076); the bar and the percent of a running item only move.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class ActionArea : Decorator
{
    private readonly LibraryModel model;
    private readonly string id;
    private readonly CrossfadeSlot slot = new();
    private string? kind;
    private LibraryBar? bar;
    private TextBlock? percent;
    private Button? primary;

    public ActionArea(LibraryModel model, string id)
    {
        this.model = model;
        this.id = id;
        Child = slot;
        MinHeight = 30;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>The kind of action on show ("download", "waiting", "running", "checking", "installed", "remove", "update", "repair", "failed", "removing").</summary>
    public string? Kind => kind;

    public Control? Live => slot.Live;

    public void Apply(LibraryRow row, bool removeAsked)
    {
        var next = KindOf(row, removeAsked);
        if (kind != next)
        {
            var first = kind is null;
            kind = next;
            bar = null; percent = null; primary = null;
            slot.Show(Build(next, row), first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
        }
        if (bar is not null) bar.Set(row.Progress ?? 0, animated: true);
        if (percent is not null) percent.Text = (int)Math.Floor((row.Progress ?? 0) * 100) + "%";
        if (primary is not null) primary.IsEnabled = !row.TermsOwed && model.Controller.CanFetch;
    }

    private static string KindOf(LibraryRow row, bool removeAsked) => row.State switch
    {
        ItemState.Available => "download",
        ItemState.Queued => "waiting",
        ItemState.Downloading => "running",
        ItemState.Checking => "checking",
        ItemState.Removing => "removing",
        ItemState.Failed => "failed",
        _ when removeAsked => "remove",
        ItemState.UpdateAvailable => "update",
        ItemState.Damaged => "repair",
        _ => "installed",
    };

    private Control Build(string what, LibraryRow row)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        void Add(Control c) { c.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(c); }
        var controller = model.Controller;
        switch (what)
        {
            case "download":
                primary = LibraryButtons.Make(LibraryWords.Download(row.SizeMb), () => _ = controller.Download(id), ButtonLook.Primary, "action-download");
                primary.IsEnabled = !row.TermsOwed && controller.CanFetch;
                Add(primary);
                break;
            case "waiting":
                Add(LibraryUi.Text(LibraryWords.Waiting, 12.5, secondary: true));
                Add(LibraryButtons.Glyph("xmark", LibraryWords.Cancel, "action-cancel", () => controller.CancelItem(id)));
                break;
            case "running":
                bar = new LibraryBar { Width = 200 };
                bar.Set(row.Progress ?? 0, animated: false);
                percent = LibraryUi.Text("0%", 12.5, secondary: true);
                percent.MinWidth = 34;
                Add(bar);
                Add(percent);
                Add(LibraryButtons.Glyph("xmark", LibraryWords.Cancel, "action-cancel", () => controller.CancelItem(id)));
                break;
            case "checking":
                Add(new SystemSpinner { Width = 14, Height = 14 });
                Add(LibraryUi.Text(LibraryWords.Checking, 12.5, secondary: true));
                break;
            case "removing":
                Add(new SystemSpinner { Width = 14, Height = 14 });
                Add(LibraryUi.Text(LibraryWords.Removing, 12.5, secondary: true));
                break;
            case "failed":
                Add(LibraryUi.Glyph("exclamationmark.triangle.fill", 13, ChromeTokens.Warning));
                Add(LibraryUi.Text(LibraryWords.DidNotDownload, 12.5));
                primary = LibraryButtons.Make(LibraryWords.Retry, () => _ = controller.Download(id), ButtonLook.Accent, "action-retry");
                primary.IsEnabled = !row.TermsOwed && controller.CanFetch;
                Add(primary);
                break;
            case "remove":
                Add(LibraryButtons.Make(LibraryWords.Remove, () => _ = controller.ConfirmRemove(id), ButtonLook.Danger, "action-remove"));
                Add(LibraryButtons.Glyph("xmark", LibraryWords.Cancel, "action-remove-cancel", () => controller.CancelRemove(id)));
                break;
            case "update":
                Add(LibraryUi.Glyph("arrow.up.circle.fill", 13, ChromeTokens.Accent));
                Add(LibraryUi.Text(row.Update is { } change ? LibraryWords.UpdateAvailable + " " + change : LibraryWords.UpdateAvailable, 12.5, trim: true));
                Add(LibraryButtons.Make(LibraryWords.Update, () => _ = controller.Update(id), ButtonLook.Primary, "action-update"));
                Add(LibraryButtons.Glyph("trash", LibraryWords.Remove, "action-trash", () => controller.AskRemove(id)));
                break;
            case "repair":
                Add(LibraryUi.Glyph("exclamationmark.triangle.fill", 13, ChromeTokens.Warning));
                Add(LibraryUi.Text(LibraryWords.Damaged, 12.5));
                Add(LibraryButtons.Make(LibraryWords.Repair, () => _ = controller.Repair(id), ButtonLook.Primary, "action-repair"));
                Add(LibraryButtons.Glyph("trash", LibraryWords.Remove, "action-trash", () => controller.AskRemove(id)));
                break;
            default:
                Add(LibraryUi.Glyph("checkmark.circle.fill", 14, ChromeTokens.Success));
                Add(LibraryUi.Text(LibraryWords.Installed, 12.5));
                if (!row.Plugins && !model.Tools) Add(LibraryUi.Text(LibraryWords.ReadyInNewChats, 11.5, secondary: true));
                Add(LibraryButtons.Glyph("trash", LibraryWords.Remove, "action-trash", () => controller.AskRemove(id)));
                break;
        }
        // The reason beside the disabled button, in the same line, so the action's controls stay the line's own children.
        if (!controller.CanFetch && what is "download" or "failed") Add(LibraryUi.Text(LibraryWords.DownloadsUnavailable, 11.5, secondary: true, trim: true));
        return line;
    }
}
