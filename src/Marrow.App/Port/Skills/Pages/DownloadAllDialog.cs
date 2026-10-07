// PORT-ONLY: first-release feature unit SK-15 (DL-17, OD-0099 "Download all A"). The Download all dialog, in every phase of the design: the confirmation (the large-download warning above 100 MB, the plain size at or below it, the
// facts, every item included, Cancel and Continue), the run (the bar, each item's state, Hide, Cancel, Pause), Paused, Done and partly failed with Retry. Nothing downloads before Continue. Every part that comes and goes
// folds open and shut, text that changes crossfades, the bar eases (OD-0076).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class DownloadAllDialog : Decorator
{
    internal const double Width440 = 440;

    private readonly LibraryModel model;
    private readonly Action hide;
    private readonly bool plugins;
    private readonly CzCrossfadeText title = new("") { FontSize = 15, FontWeight = FontWeight.SemiBold };
    private readonly CzCrossfadeText sub = new("") { FontSize = 12, Secondary = true };
    private readonly CzCrossfadeText warning = new("") { FontSize = 12.5, Wrap = true, FontWeight = FontWeight.Medium };
    private readonly CzCrossfadeText plain = new("") { FontSize = 12.5, Wrap = true, FontWeight = FontWeight.Medium };
    private readonly CzCrossfadeText room = new("") { FontSize = 12, Wrap = true };
    private readonly CzCrossfadeText heading = new("") { FontSize = 12, Secondary = true };
    private readonly CzCrossfadeText foot = new("") { FontSize = 12, Secondary = true, Wrap = true };
    private readonly CzCrossfadeText download = new("") { FontSize = 12 };
    private readonly CzCrossfadeText onDisk = new("") { FontSize = 12 };
    private readonly CzCrossfadeText free = new("") { FontSize = 12 };
    private readonly CzCrossfadeText freeLabel = new("") { FontSize = 12, Secondary = true };
    private readonly CzCrossfadeText from = new("") { FontSize = 12, Wrap = true };
    private readonly CzCrossfadeText progress = new("") { FontSize = 12, Secondary = true };
    private readonly LibraryBar bar = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CzFoldRow confirmSection, runSection, largeBox, plainBox, roomFold, freeRow;
    private readonly CrossfadeSlot lines = new();
    private readonly CrossfadeSlot buttons = new();
    private string? buttonsKey;
    private RunView view = RunView.Closed;
    private IReadOnlyList<string> lineIds = [];
    private readonly Dictionary<string, DialogLineView> lineViews = [];

    public DownloadAllDialog(LibraryModel model, Action hide, bool plugins)
    {
        this.model = model;
        this.hide = hide;
        this.plugins = plugins;
        var first = model.Dialog; // the folds start where the dialog opens: the sheet grows in, nothing folds on arrival
        var icon = LibraryUi.Tile("arrow.down.circle.fill", 36, 10);
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), ColumnSpacing = 12 };
        Grid.SetColumn(words, 1);
        header.Children.Add(icon);
        header.Children.Add(words);

        var warn = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        var warnGlyph = LibraryUi.Glyph("exclamationmark.triangle.fill", 13, ChromeTokens.Warning);
        warnGlyph.VerticalAlignment = VerticalAlignment.Top;
        warnGlyph.Margin = new Thickness(0, 2, 0, 0);
        Grid.SetColumn(warning, 1);
        warn.Children.Add(warnGlyph);
        warn.Children.Add(warning);
        var warnPanel = LibraryUi.Panel(warn, 10, 0, new Thickness(12, 10));
        warnPanel.Background = ChromeTokens.Warning.Opacity(0.14).Brush(warnPanel);
        largeBox = new CzFoldRow(warnPanel, first.Phase == RunPhase.Confirm && first.Large);
        plainBox = new CzFoldRow(LibraryUi.Panel(plain, 10, 0.06, new Thickness(12, 10)), first.Phase == RunPhase.Confirm && !first.Large);
        roomFold = new CzFoldRow(room, first.RoomNote is not null);

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("84,*,70,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 5 };
        Fact(facts, 0, 0, LibraryUi.Text(LibraryWords.FactDownload, 12, secondary: true), download);
        Fact(facts, 0, 2, LibraryUi.Text(LibraryWords.FactOnDisk, 12, secondary: true), onDisk);
        Fact(facts, 1, 0, freeLabel, free);
        Fact(facts, 1, 2, LibraryUi.Text(LibraryWords.FactFrom, 12, secondary: true), from);
        freeRow = new CzFoldRow(facts, first.FreeFact is not null);

        confirmSection = new CzFoldRow(new StackPanel { Spacing = 10, Children = { largeBox, plainBox, roomFold, freeRow } }, first.Phase == RunPhase.Confirm);

        progress.Margin = new Thickness(0, 7, 0, 0);
        runSection = new CzFoldRow(new StackPanel { Spacing = 0, Children = { bar, progress } }, first.IsRunning);

        var listBox = new StackPanel { Spacing = 6, Children = { heading, LibraryUi.Panel(LibraryUi.Scroller(lines, 170), 10, 0.06, new Thickness(10, 4)) } };

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
        foot.VerticalAlignment = VerticalAlignment.Center;
        buttons.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(foot);
        footer.Children.Add(buttons);

        var estimate = LibraryUi.Text(LibraryWords.SizeEstimateNote, 11.5, secondary: true, wrap: true);
        var stack = new StackPanel { Spacing = 12, Children = { header, estimate, confirmSection, runSection, listBox, footer } };
        if (!model.Controller.CanFetch) stack.Children.Insert(1, LibraryUi.Text(LibraryWords.DownloadsUnavailable, 11.5, secondary: true, wrap: true));
        var surface = new Border { Child = stack, Padding = new Thickness(18), Width = Width440 }.RoundedRectangle(22, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
        Child = surface;
        this.Named("Download all", "dialog");
        Apply(model.Dialog);
    }

    private static void Fact(Grid grid, int row, int column, Control label, Control value)
    {
        label.VerticalAlignment = VerticalAlignment.Top;
        value.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(label, row); Grid.SetColumn(label, column);
        Grid.SetRow(value, row); Grid.SetColumn(value, column + 1);
        grid.Children.Add(label);
        grid.Children.Add(value);
    }

    public RunView View => view;

    public string TitleShown => title.Text;

    public string SubShown => sub.Text;

    public string StatementShown => view.Large ? warning.Text : plain.Text;

    public string FootShown => foot.Text;

    public string IncludedShown => heading.Text;

    public string ProgressShown => progress.Text ?? "";

    public string DownloadShown => download.Text ?? "";

    public string OnDiskShown => onDisk.Text ?? "";

    public string FromShown => from.Text ?? "";

    public string FreeShown => free.Text ?? "";

    public string RoomShown => room.Text;

    internal CzFoldRow ConfirmFold => confirmSection;

    internal CzFoldRow RunFold => runSection;

    public bool LargeOpen => largeBox.Open;

    public bool PlainOpen => plainBox.Open;

    public bool ConfirmOpen => confirmSection.Open;

    public bool RunOpen => runSection.Open;

    public LibraryBar Bar => bar;

    public IReadOnlyDictionary<string, DialogLineView> Lines => lineViews;

    public string? ButtonsKey => buttonsKey;

    public Control? ButtonsLive => buttons.Live;

    public void Apply(RunView next)
    {
        var previousPhase = view.Phase;
        view = next;
        if (!next.IsOpen) return;
        title.SetText(next.Title);
        sub.SetText(next.Sub);
        foot.SetText(next.Foot);
        heading.SetText(next.IncludedHeading);
        var confirming = next.Phase == RunPhase.Confirm;
        confirmSection.Open = confirming;
        runSection.Open = next.Phase is RunPhase.Downloading or RunPhase.Paused;
        if (confirming)
        {
            largeBox.Open = next.Large;
            plainBox.Open = !next.Large;
            if (next.Large) warning.SetText(next.Statement ?? ""); else plain.SetText(next.Statement ?? "");
            download.SetText(next.DownloadFact);
            onDisk.SetText(next.DiskFact);
            from.SetText(next.HostsFact);
            freeRow.Open = next.FreeFact is not null;
            freeLabel.SetText(next.FreeLabel is { } drive ? LibraryWords.FactFree(drive) : "");
            free.SetText(next.FreeFact ?? "");
            roomFold.Open = next.RoomNote is not null;
            if (next.RoomNote is { } note) room.SetText(note);
        }
        if (next.Phase is RunPhase.Downloading or RunPhase.Paused or RunPhase.Done or RunPhase.Partial)
        {
            bar.Set(next.Overall, animated: previousPhase != RunPhase.Confirm && previousPhase != RunPhase.None);
            progress.SetText(next.ProgressText ?? "");
        }
        ApplyLines(next);
        ApplyButtons(next);
    }

    private void ApplyLines(RunView next)
    {
        var ids = next.Lines.Select(l => l.Id).ToList();
        if (!ids.SequenceEqual(lineIds))
        {
            lineIds = ids;
            var group = new StackPanel { Spacing = 0 };
            lineViews.Clear();
            foreach (var line in next.Lines)
            {
                var row = new DialogLineView(line, plugins);
                lineViews[line.Id] = row;
                group.Children.Add(row);
            }
            lines.Show(group, LibraryUi.Ease(this, LibraryUi.Fade));
            return;
        }
        foreach (var line in next.Lines) lineViews[line.Id].Apply(line);
    }

    private void ApplyButtons(RunView next)
    {
        var controller = model.Controller;
        var key = next.Phase + (next.Phase == RunPhase.Partial ? ":" + next.Failed : "");
        if (key == buttonsKey)
        {
            if (next.Phase == RunPhase.Confirm && buttons.Live is StackPanel { Children: [_, Button go] }) go.IsEnabled = next.CanContinue;
            return;
        }
        var first = buttonsKey is null;
        buttonsKey = key;
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        void Add(Button b) => group.Children.Add(b);
        switch (next.Phase)
        {
            case RunPhase.Confirm:
                Add(LibraryButtons.Make(LibraryWords.Cancel, controller.CancelConfirmation, ButtonLook.Plain, "dialog-cancel"));
                var go = LibraryButtons.Make(LibraryWords.Continue, () => _ = controller.Continue(), ButtonLook.Primary, "dialog-continue");
                go.IsEnabled = next.CanContinue;
                Add(go);
                break;
            case RunPhase.Downloading:
                Add(LibraryButtons.Make(LibraryWords.Hide, Hide, ButtonLook.Plain, "dialog-hide"));
                Add(LibraryButtons.Make(LibraryWords.Cancel, controller.StopRun, ButtonLook.Plain, "dialog-stop"));
                Add(LibraryButtons.Make(LibraryWords.Pause, controller.Pause, ButtonLook.Accent, "dialog-pause"));
                break;
            case RunPhase.Paused:
                Add(LibraryButtons.Make(LibraryWords.Hide, Hide, ButtonLook.Plain, "dialog-hide"));
                Add(LibraryButtons.Make(LibraryWords.Cancel, controller.StopRun, ButtonLook.Plain, "dialog-stop"));
                Add(LibraryButtons.Make(LibraryWords.Resume, controller.Resume, ButtonLook.Primary, "dialog-resume"));
                break;
            case RunPhase.Done:
                Add(LibraryButtons.Make(LibraryWords.Close, controller.CloseRun, ButtonLook.Primary, "dialog-close"));
                break;
            default:
                Add(LibraryButtons.Make(LibraryWords.Close, controller.CloseRun, ButtonLook.Plain, "dialog-close"));
                var retry = LibraryButtons.Make(LibraryWords.RetryCount(next.Failed), () => _ = controller.RetryFailed(), ButtonLook.Primary, "dialog-retry");
                retry.IsEnabled = controller.CanFetch;
                Add(retry);
                break;
        }
        buttons.Show(group, first ? null : LibraryUi.Ease(this, LibraryUi.Fade));
    }

    private void Hide()
    {
        model.DialogHidden = true;
        hide();
    }
}
