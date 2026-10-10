// PORT-ONLY, first-release UE-4 (OD-0100 place A, words U1; OD-0076). One item of the Updates table: its mark, its name and the line under it, Installed, Latest, its status, its own Update, and below the row
// two folds: the bar while it runs and the failure line with Retry (the picked design's .ur, .rbar, .rfail). Versions and the status cross-fade, the folds ease open and shut, the Update comes and goes by
// fading and scaling; nothing cuts (OD-0076). Columns are sized so no cell meets another at the Settings page's width (the owner's bar: no overlaps).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>The table's columns, shared by the header and the rows. The sum with the gaps and the row's side padding leaves the name column 98 pt of the 520 pt a page has in the Settings window.</summary>
internal static class UpdatesColumns
{
    public const double Icon = 24;
    public const double Installed = 64;
    public const double Latest = 64;
    public const double Status = 152;
    public const double Action = 66;
    public const double Gap = 6;
    public static readonly Thickness RowPadding = new(12, 9, 10, 9);

    public static ColumnDefinitions Definitions() => new($"{Icon},*,{Installed},{Latest},{Status},{Action}");
}

internal sealed class UpdatesRowControl : Border
{
    /// <summary>The symbols of the rows that have no mark of their own: the user's developer tools and the skills and tools group.</summary>
    public const string DevToolSymbol = "wrench.and.screwdriver";
    public const string SkillsSymbol = "wand.and.sparkles";

    private readonly Run lead = new(), command = new(), tail = new();
    private readonly TextBlock failureBlock;

    public UpdatesRowControl(UpdatesRowView view, bool firstInSection)
    {
        RowId = view.Id;
        IconCell = Icon(view);
        NameText = new TextBlock { Text = view.Title, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        SwiftFont.System(13).Apply(NameText);
        NameText.Foreground = Chrome.PrimaryText.Brush(NameText);
        SubText = new TextBlock { Text = view.Sub, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = view.Sub.Length > 0 };
        SwiftFont.System(11).Apply(SubText);
        SubText.Foreground = Chrome.SecondaryText.Brush(SubText);
        NameCell = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { NameText, SubText } };
        InstalledText = new CzCrossfadeText(view.Installed) { FontSize = 12, Secondary = true, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        LatestText = new CzCrossfadeText(view.Latest) { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        StatusCell = new UpdatesStatusCell(view.Status, view.StatusText) { VerticalAlignment = VerticalAlignment.Center };
        UpdateButton = new CzCapsuleButton("Update") { HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(UpdateButton, $"Update {view.Title}");
        UpdateReveal = new UpdatesAppear(UpdateButton, view.CanUpdate) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        ActionCell = UpdateReveal;

        ProgressBar = new UpdatesProgressBar { Margin = new Thickness(0, 2, 0, 8) };
        ProgressFold = new CzFoldRow(ProgressBar, view.ShowProgress);
        ProgressBar.SetDrawn(view.ShowProgress);
        ProgressFold.PropertyChanged += (_, e) => { if (e.Property == Visual.OpacityProperty) ProgressBar.SetDrawn(ProgressFold.Opacity > 0); }; // a shut fold draws no light

        failureBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12).Apply(failureBlock);
        failureBlock.Foreground = Chrome.PrimaryText.Brush(failureBlock);
        command.FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace");
        command.FontSize = 11.5;
        failureBlock.Inlines = new InlineCollection { lead, command, tail };
        RetryButton = new CzCapsuleButton("Retry") { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(RetryButton, $"Retry {view.Title}");
        var failure = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        failure.Children.Add(failureBlock);
        failure.Children.Add(RetryButton);
        Grid.SetColumn(RetryButton, 1);
        var failureBox = new Border { Padding = new Thickness(12, 9), Margin = new Thickness(0, 0, 0, 9), Child = failure };
        failureBox.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        failureBox.Background = Chrome.Warning.Opacity(.14).Brush(failureBox);
        FailureFold = new CzFoldRow(failureBox, view.FailureLead is not null);
        ShowFailure(view);

        var grid = new Grid { ColumnDefinitions = UpdatesColumns.Definitions(), ColumnSpacing = UpdatesColumns.Gap, RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        Place(grid, IconCell, 0);
        Place(grid, NameCell, 1);
        Place(grid, InstalledText, 2);
        Place(grid, LatestText, 3);
        Place(grid, StatusCell, 4);
        Place(grid, ActionCell, 5);
        Span(grid, ProgressFold, 1);
        Span(grid, FailureFold, 2);

        var divider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, IsVisible = !firstInSection, Margin = new Thickness(50, 0, 0, 0) };
        divider.Background = Chrome.Overlay(.12).Brush(divider);
        Child = new Panel { Children = { new Border { Padding = UpdatesColumns.RowPadding, Child = grid }, divider } };
        Apply(view);
    }

    public string RowId { get; }

    public Control IconCell { get; }

    public Control NameCell { get; }

    public TextBlock NameText { get; }

    public TextBlock SubText { get; }

    public CzCrossfadeText InstalledText { get; }

    public CzCrossfadeText LatestText { get; }

    public UpdatesStatusCell StatusCell { get; }

    public CzCapsuleButton UpdateButton { get; }

    public UpdatesAppear UpdateReveal { get; }

    public Control ActionCell { get; }

    public CzFoldRow ProgressFold { get; }

    public UpdatesProgressBar ProgressBar { get; }

    public CzFoldRow FailureFold { get; }

    public CzCapsuleButton RetryButton { get; }

    /// <summary>The failure line as it reads: lead, the command to run by hand, tail.</summary>
    public string FailureText => (lead.Text ?? "") + (command.Text ?? "") + (tail.Text ?? "");

    public void Apply(UpdatesRowView view)
    {
        InstalledText.SetText(view.Installed);
        LatestText.SetText(view.Latest);
        StatusCell.SetStatus(view.Status, view.StatusText);
        UpdateReveal.Shown = view.CanUpdate;
        ProgressBar.Active = view.ShowProgress;
        if (view.ShowProgress) ProgressBar.Set(view.Progress);
        ProgressFold.Open = view.ShowProgress;
        if (view.FailureLead is not null) ShowFailure(view);
        FailureFold.Open = view.FailureLead is not null;
        RetryButton.Dimmed = !view.CanRetry;
    }

    // The failure line keeps its words while the fold closes, so the text does not blank as it goes.
    private void ShowFailure(UpdatesRowView view)
    {
        if (view.FailureLead is null) return;
        lead.Text = view.FailureLead;
        command.Text = view.FailureCommand ?? "";
        tail.Text = view.FailureTail ?? "";
    }

    private static void Place(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, 0);
        grid.Children.Add(control);
    }

    private static void Span(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 0);
        Grid.SetColumnSpan(control, 6);
        grid.Children.Add(control);
    }

    private static Control Icon(UpdatesRowView view)
    {
        var tile = new Border { Width = UpdatesColumns.Icon, Height = UpdatesColumns.Icon, VerticalAlignment = VerticalAlignment.Center };
        tile.RoundedRectangle(7, RoundedCornerStyle.Continuous);
        tile.Background = Chrome.Overlay(.10).Brush(tile);
        tile.ClipToBounds = true;
        switch (view.Kind)
        {
            case UpdateItemKind.Marrow:
                tile.Child = new Image { Source = AppIconImage.Bitmap, Width = UpdatesColumns.Icon, Height = UpdatesColumns.Icon, Stretch = Stretch.Uniform };
                break;
            case UpdateItemKind.ModelPoints: // PORT-ONLY (OD-0134, ED-NEW-ignis-settings): Ignis's lit flame
                tile.Child = new Marrow.App.Port.Ignis.Settings.IgnisFlameMark { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                break;
            case UpdateItemKind.ProviderCli when ProviderOf(view.Id) is { } provider:
                tile.Child = new ProviderIcon { Provider = provider, Size = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                break;
            default:
                var symbol = SwiftImage.SystemName(view.Kind == UpdateItemKind.SkillsAndTools ? SkillsSymbol : DevToolSymbol).Font(SwiftFont.System(13)).ForegroundStyle(Chrome.PrimaryText);
                symbol.HorizontalAlignment = HorizontalAlignment.Center;
                symbol.VerticalAlignment = VerticalAlignment.Center;
                tile.Child = symbol;
                break;
        }
        return tile;
    }

    // The orchestrator's ids for provider CLIs are "cli:<raw provider id>" (ProviderCliUpdateItem).
    private static ProviderKind? ProviderOf(string id) =>
        id.StartsWith("cli:", StringComparison.Ordinal) ? SwiftEnum<ProviderKind>.AllCases.Cast<ProviderKind?>().FirstOrDefault(p => SwiftEnum<ProviderKind>.RawValue(p!.Value) == id[4..]) : null;
}
