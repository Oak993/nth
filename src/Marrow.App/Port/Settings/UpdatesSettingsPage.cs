// PORT-ONLY, first-release UE-4 (BUILD-PLAN.md 2.4; OD-0100 place A: the Tools page of OD-0075 grown into Updates, words U1). The Updates Settings page: a header with the state in a line, a line of detail and the one
// button; a banner that offers Restart Marrow after Marrow's own update; and the table of everything "Update everything" covers, in sections, with Installed and Latest columns, each row's status, its own
// Update and, when it failed, a line with the command to run by hand and Retry. Picked design: E:\Droppy\work\design\decisions-2026-10-04\update-everything (variant A). Every change is transitioned
// (OD-0076) on the frame clock and stills under Reduce Motion. The page lists nothing until the app gives it items (UpdatesRunnerSource); SA-1's shell had it hidden, and this unit sets IsReady.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views;
using Marrow.App.Views.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>The Updates page: OD-0100 place A, the Tools page of OD-0075 grown into Updates.</summary>
internal sealed class UpdatesSettingsPage : Decorator, ISettingsPageChrome
{
    /// <summary>The page is built (UE-4): it is listed in the Settings sidebar and found by its search. Was false while SA-1's shell stood empty.</summary>
    internal const bool IsReady = true;

    /// <summary>Seconds between refreshes of the "Checked ... ago" sentence while the page is open.</summary>
    private const double TickSeconds = 30;

    private static readonly string[] ColumnLabels = ["Item", "Installed", "Latest", "Status"];

    private readonly StackPanel sections = new() { Orientation = Orientation.Vertical, Spacing = 14 };
    private readonly Dictionary<string, (UpdatesRowControl Row, CzFoldRow Fold, StackPanel Stack)> rows = [];
    private readonly Dictionary<string, (CzFoldRow Fold, StackPanel Stack)> sectionBoxes = [];
    private readonly List<string> sectionTitles = [];
    private WorkItem? tick;
    private bool built;

    public UpdatesSettingsPage() : this(UpdatesRunnerSource.Shared)
    {
    }

    internal UpdatesSettingsPage(IUpdatesRunner runner)
    {
        Controller = new UpdatesController(runner);
        var view = Controller.View;

        TitleText = new CzCrossfadeText(view.Header.Title) { FontSize = 19, FontWeight = FontWeight.SemiBold, Wrap = true };
        DetailText = new CzCrossfadeText(view.Header.Detail) { FontSize = 12, Secondary = true, Wrap = true };
        MainButton = new UpdatesMainButton(view.Header.ButtonLabel, ModeOf(view.Header)) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        MainButton.Invoked += () => MainActor.Spawn(Controller.Press);
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { TitleText, DetailText } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(0, 2, 0, 14), Children = { text, MainButton } };
        Grid.SetColumn(MainButton, 1);

        BannerTitleText = new CzCrossfadeText(view.Banner.Title) { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Wrap = true };
        BannerDetailText = new CzCrossfadeText(view.Banner.Detail) { FontSize = 12.5, Wrap = true };
        LaterButton = new CzCapsuleButton("Later") { VerticalAlignment = VerticalAlignment.Center };
        LaterButton.Invoked += Controller.DismissBanner;
        var icon = AppIconImage.Make(30);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var bannerText = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { BannerTitleText, BannerDetailText } };
        var bannerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Children = { icon, bannerText, LaterButton } };
        Grid.SetColumn(bannerText, 1);
        Grid.SetColumn(LaterButton, 2);
        var banner = new Border { Padding = new Thickness(14, 11, 12, 11), Margin = new Thickness(0, 0, 0, 10), Child = bannerGrid };
        banner.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        banner.Background = Chrome.Accent.Opacity(.20).Brush(banner);
        BannerFold = new CzFoldRow(banner, view.Banner.Visible);

        Build(view);
        Child = new StackPanel { Orientation = Orientation.Vertical, Children = { new UpdateWhatsNew(runner), header, BannerFold, sections, new OptionalToolsPage() } };
        built = true;

        AttachedToVisualTree += (_, _) =>
        {
            Controller.ViewChanged += OnViewChanged;
            MainActor.Spawn(Controller.Attach);
            ScheduleTick();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Controller.ViewChanged -= OnViewChanged;
            Controller.Detach();
            tick?.Cancel();
            tick = null;
        };
    }

    /// <inheritdoc />
    public bool HasChromeControls => false;

    /// <inheritdoc />
    public IReadOnlyList<Control> ChromeAccessories { get; } = [];

    internal UpdatesController Controller { get; }

    internal CzCrossfadeText TitleText { get; }

    internal CzCrossfadeText DetailText { get; }

    internal UpdatesMainButton MainButton { get; }

    internal CzFoldRow BannerFold { get; }

    internal CzCrossfadeText BannerTitleText { get; }

    internal CzCrossfadeText BannerDetailText { get; }

    internal CzCapsuleButton LaterButton { get; }

    internal IReadOnlyList<string> SectionTitles => sectionTitles;

    internal IReadOnlyList<string> ColumnHeaders => ColumnLabels;

    internal IReadOnlyList<UpdatesRowControl> RowControls =>
        sectionTitles.SelectMany(title => sectionBoxes[title].Stack.Children.OfType<CzFoldRow>().Select(fold => fold.Child).OfType<UpdatesRowControl>()).ToList();

    internal UpdatesRowControl RowControl(string id) => rows[id].Row;

    internal CzFoldRow RowFold(string id) => rows[id].Fold;

    internal CzFoldRow SectionFold(string title) => sectionBoxes[title].Fold;

    private void OnViewChanged() => Apply(Controller.View);

    private void ScheduleTick() => tick = MainActor.AsyncAfter(TimeSpan.FromSeconds(TickSeconds), () =>
    {
        if (tick is null) return;
        Controller.Tick();
        ScheduleTick();
    });

    private static UpdatesButtonMode ModeOf(UpdatesHeaderView header) => header.ButtonBusy ? UpdatesButtonMode.Busy : header.ButtonQuiet ? UpdatesButtonMode.Quiet : UpdatesButtonMode.Primary;

    // The first build puts every section and row in place at rest; later views only change what changed.
    private void Build(UpdatesPageView view)
    {
        foreach (var section in view.Sections) AddSection(section, open: true, first: sectionTitles.Count == 0);
    }

    private void Apply(UpdatesPageView view)
    {
        TitleText.SetText(view.Header.Title);
        DetailText.SetText(view.Header.Detail);
        MainButton.Apply(view.Header.ButtonLabel, ModeOf(view.Header));
        if (view.Banner.Title.Length > 0) // the banner keeps its words while it folds shut
        {
            BannerTitleText.SetText(view.Banner.Title);
            BannerDetailText.SetText(view.Banner.Detail);
        }
        BannerFold.Open = view.Banner.Visible;
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in view.Sections)
        {
            if (!sectionBoxes.ContainsKey(section.Title)) AddSection(section, open: false, first: false);
            var box = sectionBoxes[section.Title];
            for (var index = 0; index < section.Rows.Count; index++)
            {
                var row = section.Rows[index];
                present.Add(row.Id);
                if (rows.TryGetValue(row.Id, out var existing))
                {
                    existing.Row.Apply(row);
                    existing.Fold.Open = true;
                }
                else AddRow(box.Stack, row, index, open: false);
            }
            box.Fold.Open = true;
        }
        foreach (var (id, entry) in rows)
            if (!present.Contains(id)) entry.Fold.Open = false; // an item that is gone folds shut
        foreach (var (title, box) in sectionBoxes)
            if (view.Sections.All(s => s.Title != title)) box.Fold.Open = false;
    }

    private void AddSection(UpdatesSectionView section, bool open, bool first)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var content = new StackPanel { Orientation = Orientation.Vertical };
        if (first) content.Children.Add(ColumnHeader());
        content.Children.Add(new ChromeCard(() => stack));
        var chrome = new ChromeSection(section.Title, () => content);
        var fold = new CzFoldRow(chrome, open);
        sectionTitles.Add(section.Title);
        sectionBoxes[section.Title] = (fold, stack);
        sections.Children.Add(fold);
        for (var index = 0; index < section.Rows.Count; index++) AddRow(stack, section.Rows[index], index, open: true);
        if (!open) fold.Open = true;
    }

    private void AddRow(StackPanel stack, UpdatesRowView view, int index, bool open)
    {
        var row = new UpdatesRowControl(view, firstInSection: index == 0);
        row.UpdateButton.Invoked += () => MainActor.Spawn(() => Controller.UpdateRow(view.Id));
        row.RetryButton.Invoked += () => MainActor.Spawn(() => Controller.RetryRow(view.Id));
        var fold = new CzFoldRow(row, open);
        stack.Children.Insert(Math.Min(index, stack.Children.Count), fold);
        rows[view.Id] = (row, fold, stack);
        if (!open && built) fold.Open = true;
    }

    // The design's .hdr: the column names over the first card, aligned to the row's cells.
    private static Control ColumnHeader()
    {
        var grid = new Grid { ColumnDefinitions = UpdatesColumns.Definitions(), ColumnSpacing = UpdatesColumns.Gap, Margin = new Thickness(UpdatesColumns.RowPadding.Left, 0, UpdatesColumns.RowPadding.Right, 6) };
        for (var index = 0; index < ColumnLabels.Length; index++)
        {
            var label = new TextBlock { Text = ColumnLabels[index], MaxLines = 1, HorizontalAlignment = index == 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            SwiftFont.System(11).Apply(label);
            label.Foreground = SwiftColor.Tertiary.Brush(label);
            Grid.SetColumn(label, index + 1);
            grid.Children.Add(label);
        }
        return grid;
    }
}
