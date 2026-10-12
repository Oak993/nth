// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5 "Activity view"; section 3.3 "Lineage: the Activity
// log filtered to this mission"; unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-6: the Activity log view.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Marrow.Core.Port;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>
/// The Activity log (12.5): every action an assistant, an agent or the owner's tools took, in the keyed chain the audit log
/// keeps. It opens by checking the chain, off the UI thread, and says so first: "Verified", or the first line that fails with
/// the reason and a button that brings that line into sight. Below it: filters (assistant, mission, class, day, search), the
/// Log and Lineage switch (mission → agent → action → grant), the list, and the detail of the selected line. Export writes the
/// lines that pass the filter to JSON or CSV. The home's header menu opens it (AS-8), and a mission card's Lineage button
/// opens it with <see cref="ActivityLogModel.ShowMissionAsync"/>.
/// <para>Narrow windows (under 900 px) stack the detail under the list, as the Board does (2.2). Nothing in the view moves but the
/// strip's colour, the hover fills and a jump to a line, and with Reduce motion the strip and the jump are at once.</para>
/// </summary>
public sealed class ActivityLogView : Decorator
{
    public const double NarrowWidth = 900;
    public const double Gutter = 16;
    public const double DetailWidth = 340;

    private readonly ActivityLogModel model;
    private readonly TimeZoneInfo zone;
    private readonly Func<ActivityExportFormat, string, Task<string?>> pickPath;
    private readonly ActivityStatusStrip strip;
    private readonly ActivityFilterBar filters;
    private readonly ActivityRowList list;
    private readonly ActivityDetailPanel detail;
    private readonly TextBlock title = new() { Text = ActivityWords.Title };
    private readonly TextBlock subtitle = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock exportNote = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Grid body = new();
    private readonly Border listCard;
    private readonly Border detailCard;
    private readonly ExportMenuState exportMenu = new();
    private bool stacked;

    private sealed class ExportMenuState : MainActorModel
    {
        private bool isOpen;

        public bool IsOpen { get => Read(isOpen); set => Set(ref isOpen, value); }
    }

    /// <param name="model">What the view shows; the view opens it when it is first shown.</param>
    /// <param name="zone">The zone times are shown in (the PC's).</param>
    /// <param name="pickExportPath">Asks the owner where to save an export: the format and a suggested name in, a path or null (cancelled) out. The default is the system save dialog.</param>
    public ActivityLogView(ActivityLogModel model, TimeZoneInfo? zone = null, Func<ActivityExportFormat, string, Task<string?>>? pickExportPath = null)
    {
        this.model = model;
        this.zone = zone ?? TimeZoneInfo.Local;
        pickPath = pickExportPath ?? PickWithSystemDialog;
        strip = new ActivityStatusStrip(model, () => _ = model.ShowFailureAsync());
        filters = new ActivityFilterBar(model);
        list = new ActivityRowList(model, this.zone);
        detail = new ActivityDetailPanel(model, this.zone);
        listCard = Card(list);
        detailCard = Card(detail);

        SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold).Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(subtitle);
        subtitle.Foreground = Chrome.SecondaryText.Brush(subtitle);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(exportNote);
        exportNote.Foreground = Chrome.SecondaryText.Brush(exportNote);
        AutomationProperties.SetLiveSetting(exportNote, AutomationLiveSetting.Polite);

        var heading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { title, subtitle, exportNote } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top, Children = { RefreshButton(), ExportButton() } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(buttons, 1);
        header.Children.Add(heading);
        header.Children.Add(buttons);

        body.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(listCard);
        body.Children.Add(detailCard);
        LayOut(stackedLayout: false);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(Gutter, Gutter, Gutter, Gutter), RowSpacing = 12 };
        Grid.SetRow(strip, 1);
        Grid.SetRow(filters, 2);
        Grid.SetRow(body, 3);
        root.Children.Add(header);
        root.Children.Add(strip);
        root.Children.Add(filters);
        root.Children.Add(body);
        Child = root;

        SizeChanged += (_, e) => LayOut(e.NewSize.Width < NarrowWidth);
        AttachedToVisualTree += (_, _) =>
        {
            model.Changed -= OnChanged;
            model.Changed += OnChanged;
            model.RevealRequested -= OnReveal;
            model.RevealRequested += OnReveal;
            ShowAll();
            if (model.Phase == ActivityLoadPhase.Idle) _ = model.OpenAsync(); // "verifies the chain on open"
        };
        DetachedFromVisualTree += (_, _) =>
        {
            model.Changed -= OnChanged;
            model.RevealRequested -= OnReveal;
            model.Release(); // a hidden view holds none of the log; it reads and checks it again when it is shown
        };
        ShowAll();
    }

    /// <summary>The view's model (a host tells it to show a mission, or to refresh).</summary>
    public ActivityLogModel Model => model;

    internal ActivityStatusStrip Strip => strip;

    internal ActivityFilterBar Filters => filters;

    internal ActivityRowList List => list;

    internal ActivityDetailPanel Detail => detail;

    internal Button RefreshControl { get; private set; } = null!;

    internal Button ExportControl { get; private set; } = null!;

    /// <summary>The export menu's two choices, as the owner reads them.</summary>
    public IReadOnlyList<string> ExportChoices { get; } = [ActivityWords.ExportJson, ActivityWords.ExportCsv];

    public string SubtitleText => subtitle.Text ?? "";

    public string ExportNote => exportNote.Text ?? "";

    public bool IsStacked => stacked;

    /// <summary>Asks where to save, then writes the export (what choosing a format in the Export menu does).</summary>
    public async Task ExportAsync(ActivityExportFormat format)
    {
        exportMenu.IsOpen = false;
        var path = await pickPath(format, model.SuggestedFileName(format));
        if (string.IsNullOrWhiteSpace(path)) return; // the owner cancelled
        await model.ExportAsync(format, path);
    }

    // ---- model --------------------------------------------------------------------------------------------------------

    private void OnChanged(ActivityChange change)
    {
        if ((change & (ActivityChange.Phase | ActivityChange.Snapshot)) != 0) strip.Refresh();
        if ((change & (ActivityChange.Items | ActivityChange.Phase | ActivityChange.Snapshot)) != 0)
        {
            list.Refresh();
            ShowSubtitle();
        }
        if ((change & (ActivityChange.Filter | ActivityChange.Mode)) != 0) ShowSubtitle();
        if ((change & ActivityChange.Selection) != 0)
        {
            list.RefreshSelection();
            detail.Refresh();
        }
        if ((change & ActivityChange.Export) != 0) ShowExport();
    }

    private void OnReveal(ActivityRow row) => list.ScrollTo(model.IndexOf(row));

    private void ShowAll()
    {
        strip.Refresh(animate: false);
        list.Refresh();
        detail.Refresh();
        ShowSubtitle();
        ShowExport();
    }

    private void ShowSubtitle()
    {
        if (model.Phase != ActivityLoadPhase.Ready || model.Snapshot is not { } snapshot)
        {
            subtitle.Text = "";
            subtitle.IsVisible = false;
            return;
        }
        subtitle.IsVisible = true;
        var total = snapshot.Rows.Count;
        subtitle.Text = model.Filter.IsEmpty
            ? ActivityWords.Lines(snapshot.TotalLines)
            : ActivityWords.Count(model.MatchCount, "line matches", "lines match") + " of " + ActivityWords.Lines(total) + " · " + model.Filter.Describe();
    }

    private void ShowExport()
    {
        var state = model.Export;
        var note = state.Phase switch
        {
            ActivityExportPhase.Writing => "Exporting…",
            ActivityExportPhase.Done => $"Exported {ActivityWords.Lines(state.Rows)} to {state.Path}",
            ActivityExportPhase.Failed => "The export failed: " + state.Message,
            _ => "",
        };
        exportNote.Text = note;
        exportNote.IsVisible = note.Length > 0;
        exportNote.Foreground = (state.Phase == ActivityExportPhase.Failed ? Chrome.Danger : Chrome.SecondaryText).Brush(exportNote);
    }

    // ---- layout -------------------------------------------------------------------------------------------------------

    private void LayOut(bool stackedLayout)
    {
        stacked = stackedLayout;
        body.ColumnDefinitions.Clear();
        body.RowDefinitions.Clear();
        if (stackedLayout)
        {
            body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            body.RowDefinitions.Add(new RowDefinition(new GridLength(260)));
            body.RowSpacing = 12;
            body.ColumnSpacing = 0;
            Grid.SetRow(listCard, 0);
            Grid.SetColumn(listCard, 0);
            Grid.SetRow(detailCard, 1);
            Grid.SetColumn(detailCard, 0);
        }
        else
        {
            body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(DetailWidth)));
            body.RowSpacing = 0;
            body.ColumnSpacing = 12;
            Grid.SetRow(listCard, 0);
            Grid.SetColumn(listCard, 0);
            Grid.SetRow(detailCard, 0);
            Grid.SetColumn(detailCard, 1);
        }
    }

    private static Border Card(Control child)
    {
        var card = new Border { Child = child, ClipToBounds = true, MinHeight = 160 };
        card.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        card.Background = Chrome.Overlay(0.03).Brush(card);
        return card;
    }

    // ---- header buttons -----------------------------------------------------------------------------------------------

    private Control RefreshButton()
    {
        var icon = SwiftImage.SystemName("arrow.clockwise").Font(SwiftFont.System(12.5, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.PrimaryText);
        var face = new Border { Width = Chrome.CapsuleContentHeight, Height = Chrome.CapsuleContentHeight, Child = icon, Background = Brushes.Transparent };
        RefreshControl = PlainControls.Button(face, () => _ = model.RefreshAsync());
        AutomationProperties.SetName(RefreshControl, ActivityWords.Refresh);
        ToolTip.SetTip(RefreshControl, ActivityWords.Refresh);
        return RefreshControl.ChromeGlassCircle();
    }

    private Control ExportButton()
    {
        var label = new TextBlock { Text = ActivityWords.Export, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Chrome.PrimaryText.Brush(label);
        var chevron = SwiftImage.SystemName("chevron.down").Font(Chrome.ChevronFont).ForegroundStyle(Chrome.SecondaryText);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, chevron } };
        var face = new Border { Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), Height = Chrome.CapsuleContentHeight, Child = row, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        ExportControl = PlainControls.Button(face, () => exportMenu.IsOpen = !exportMenu.IsOpen);
        AutomationProperties.SetName(ExportControl, "Export the lines shown, as JSON or CSV");
        var popover = PlainControls.Popover(ExportControl, new Binding<bool>(() => exportMenu.IsOpen, value => exportMenu.IsOpen = value), () => new PopoverMenu(() =>
        {
            var items = new StackPanel();
            items.Children.Add(new PopoverItem(ActivityWords.ExportJson, () => _ = ExportAsync(ActivityExportFormat.Json), symbol: "curlybraces"));
            items.Children.Add(new PopoverItem(ActivityWords.ExportCsv, () => _ = ExportAsync(ActivityExportFormat.Csv), symbol: "list.bullet"));
            return items;
        }));
        return new Panel { Children = { ExportControl.ChromeGlassCapsule(), popover } };
    }

    private async Task<string?> PickWithSystemDialog(ActivityExportFormat format, string suggested)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
                ? await Pick(main, format, suggested)
                : null;
        return await Pick(top, format, suggested);
    }

    private static async Task<string?> Pick(TopLevel top, ActivityExportFormat format, string suggested)
    {
        var json = format == ActivityExportFormat.Json;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the activity log",
            SuggestedFileName = suggested,
            DefaultExtension = json ? "json" : "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices = [json ? new FilePickerFileType("JSON") { Patterns = ["*.json"] } : new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        return file?.TryGetLocalPath();
    }
}
