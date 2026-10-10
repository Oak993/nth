// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5). Settings › Ignis, the locked design's section
// (phoenix-designs.html "Settings › Ignis"): the lead sentence; one card of rows (Ignis default mode, Automatic, Include
// Hydra heads, Smart helpers, Show (i) explanations, Show keep-warm pings, Keep word for word); "Savings history"; and
// "Rebuild points" (Model points with Update now, Update automatically, the table of model / cheapest point / why, and
// the hint). Built from the Settings pages' own parts (ChromeSection, ChromeCard, ChromeRow, ChromeSegmentedPicker,
// SettingsSwitch, CzMenuButton, CzCapsuleButton), as Hydra's page is. Every word is IgnisWords'; every setting is
// IgnisSettings' (the one "ignis" key); the history reads ISavingsLedger and the table IIgnisPoints, through the bound
// Ignis controller (IgnisSeams.Controller). No keyboard shortcuts (owner).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Ignis.Settings;

public sealed class IgnisSettingsPage : Decorator
{
    /// <summary>The page is built (the SA-1 pattern): listed in the sidebar, found by its search, opened by a request.</summary>
    public const bool IsReady = true;

    private sealed class PreferencesState : ObservableModel
    {
        private IgnisPreferences value;
        public PreferencesState(IgnisPreferences value) => this.value = value;
        public IgnisPreferences Value { get => Read(value); set => Set(ref this.value, value); }
    }

    private readonly IgnisSettings settings;
    private readonly ISavingsLedger? ledger;
    private readonly IIgnisPoints? points;
    private readonly Func<DateOnly> today;
    private readonly Func<(ProviderKind Provider, string? Model)?>? chatInUse;
    private readonly PreferencesState state;
    private readonly Decorator historyHost = new();
    private readonly Decorator tableHost = new();
    private readonly ChromeRow modelPointsRow;
    private readonly TextBlock? modelPointsDetailText;
    private bool checking;

    /// <summary>The page Settings opens: the bound Ignis controller's settings, ledger and points; unbound, the "ignis" key in the app's settings and an empty history.</summary>
    public IgnisSettingsPage()
        : this(IgnisSeams.Controller.Current() is { } ignis ? ignis.Settings : new IgnisSettings(UserDefaults.Standard.Current),
            IgnisSeams.Controller.Current()?.Ledger, IgnisSeams.Controller.Current()?.Points)
    {
    }

    /// <param name="today">The local day the history ends on; default: SwiftDate.Now's local day.</param>
    /// <param name="chatInUse">The provider and model of the chat in front, for the table's "in use"; default: the app's selected chat.</param>
    public IgnisSettingsPage(IgnisSettings settings, ISavingsLedger? ledger, IIgnisPoints? points, Func<DateOnly>? today = null, Func<(ProviderKind Provider, string? Model)?>? chatInUse = null)
    {
        this.settings = settings;
        this.ledger = ledger;
        this.points = points;
        this.today = today ?? (() => DateOnly.FromDateTime(SwiftDate.Now.ToLocalTime().DateTime));
        this.chatInUse = chatInUse;
        state = new PreferencesState(settings.Value);
        var value = state.Value;

        // The default-mode segmented picker (.seg, aria "Default mode").
        ModePicker = new ChromeSegmentedPicker<IgnisMode>(
            new ChromeSegmentedOption<IgnisMode>[] { new(IgnisMode.Off, IgnisWords.ModeName(IgnisMode.Off)), new(IgnisMode.KeepWarm, IgnisWords.ModeName(IgnisMode.KeepWarm)), new(IgnisMode.WarmAndTidy, IgnisWords.ModeName(IgnisMode.WarmAndTidy)) },
            new Binding<IgnisMode>(() => state.Value.DefaultMode, mode => Change(p => p with { DefaultMode = mode })));
        AutomationProperties.SetName(ModePicker, IgnisWords.DefaultModeAccessibleName);

        KeepTurnsMenu = new CzMenuButton(IgnisWords.RowKeepWordForWord, KeepTurnsOptions(value.KeepTurns), value.KeepTurns.ToString(System.Globalization.CultureInfo.InvariantCulture));
        KeepTurnsMenu.SelectionChanged += id => { if (int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var turns)) Change(p => p with { KeepTurns = turns }); };

        var rows = new StackPanel();
        rows.Children.Add(new ChromeRow(IgnisWords.RowDefaultMode, () => ModePicker, IgnisWords.RowDefaultModeDetail));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(Toggle(IgnisWords.RowAutomatic, IgnisWords.RowAutomaticDetail, IgnisWords.RowAutomatic, p => p.Auto, (p, on) => p with { Auto = on }));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(Toggle(IgnisWords.RowHeads, IgnisWords.RowHeadsDetail, IgnisWords.RowHeads, p => p.IncludeHeads, (p, on) => p with { IncludeHeads = on }));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(Toggle(IgnisWords.RowSmartHelpers, IgnisWords.RowSmartHelpersDetail(HelperModelName()), IgnisWords.RowSmartHelpersAccessibleName, p => p.SmartHelpers, (p, on) => p with { SmartHelpers = on }));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(Toggle(IgnisWords.RowExplanations, IgnisWords.RowExplanationsDetail, IgnisWords.RowExplanations, p => p.ShowExplanations, (p, on) => p with { ShowExplanations = on }));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(Toggle(IgnisWords.RowShowPings, IgnisWords.RowShowPingsDetail, IgnisWords.RowShowPings, p => p.ShowPings, (p, on) => p with { ShowPings = on }));
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(new ChromeRow(IgnisWords.RowKeepWordForWord, () => KeepTurnsMenu, IgnisWords.RowKeepWordForWordDetail));

        var lead = new TextBlock { Text = IgnisWords.SettingsLead, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        SwiftFont.Subheadline.Apply(lead);
        lead.Foreground = Chrome.SecondaryText.Brush(lead);

        // Rebuild points: Model points (version, Update now) and Update automatically.
        UpdateNowButton = new CzCapsuleButton(IgnisWords.UpdateNow) { MinWidth = 96 };
        if (UpdateNowButton.Child is TextBlock face) face.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(UpdateNowButton, IgnisWords.UpdateNow);
        UpdateNowButton.Invoked += () => _ = UpdateNow();
        UpdateNowButton.Dimmed = points is null;
        modelPointsRow = new ChromeRow(IgnisWords.ModelPoints, () => UpdateNowButton, IgnisWords.ModelPointsDetail(points?.Current.Version ?? "—"));
        modelPointsDetailText = modelPointsRow.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault(text => text.Text == ModelPointsDetail); // the row's own detail line, updated in place
        var pointsCard = new StackPanel();
        pointsCard.Children.Add(modelPointsRow);
        pointsCard.Children.Add(new ChromeRowDivider());
        pointsCard.Children.Add(Toggle(IgnisWords.UpdateAutomatically, IgnisWords.UpdateAutomaticallyDetail, IgnisWords.UpdateAutomaticallyAccessibleName, p => p.AutoUpdatePoints, (p, on) => p with { AutoUpdatePoints = on }));
        var hint = new TextBlock { Text = IgnisWords.PointMovesHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0) };
        SwiftFont.Subheadline.Apply(hint);
        hint.Foreground = Chrome.SecondaryText.Brush(hint);
        var rebuild = new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { new ChromeCard(() => pointsCard), tableHost, hint } };

        Child = new StackPanel
        {
            Spacing = Chrome.SectionSpacing,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { lead, new ChromeCard(() => rows) } },
                new ChromeSection(IgnisWords.SavingsHistory, () => historyHost),
                new ChromeSection(IgnisWords.RebuildPoints, () => rebuild),
            },
        };
        RebuildHistory();
        RebuildTable();

        Action settingsChanged = () => MainActor.Post(() => state.Value = settings.Value);
        Action ledgerChanged = () => MainActor.Post(RebuildHistory);
        Action pointsChanged = () => MainActor.Post(() => { RebuildTable(); RefreshVersion(); });
        AttachedToVisualTree += (_, _) =>
        {
            settings.Changed += settingsChanged;
            if (ledger is not null) ledger.Changed += ledgerChanged;
            if (points is not null) points.Changed += pointsChanged;
            state.Value = settings.Value;
            RebuildHistory();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            settings.Changed -= settingsChanged;
            if (ledger is not null) ledger.Changed -= ledgerChanged;
            if (points is not null) points.Changed -= pointsChanged;
        };
        KeepTurnsMenu.Track(() => state.Value.KeepTurns, turns => KeepTurnsMenu.SetOptions(KeepTurnsOptions(turns), turns.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public ChromeSegmentedPicker<IgnisMode> ModePicker { get; }

    public CzMenuButton KeepTurnsMenu { get; }

    public CzCapsuleButton UpdateNowButton { get; }

    /// <summary>The history as it stands (rebuilt when the ledger changes).</summary>
    public IgnisSavingsHistoryView? History => historyHost.Child as IgnisSavingsHistoryView;

    /// <summary>The table as it reads: model, cheapest point, why, and whether the chat in front uses it.</summary>
    public IReadOnlyList<(string Model, string Point, string Why, bool InUse)> PointsTable { get; private set; } = [];

    /// <summary>"Update now": checks the feed and installs a newer valid file. The button reads "Checking…" while it runs and "Up to date" after, as the design's does; a failure gives "Update now" back.</summary>
    public async Task UpdateNow()
    {
        if (points is null || checking) return;
        checking = true;
        SetUpdateLabel(IgnisWords.Checking, enabled: false);
        IgnisPointsCheck check;
        try { check = await points.CheckAsync(install: true, CancellationToken.None); }
        catch (Exception error) when (error is not OperationCanceledException) { check = new IgnisPointsCheck(points.Current.Version, null, false, error.Message); }
        checking = false;
        RefreshVersion();
        RebuildTable();
        if (check.Error is null) SetUpdateLabel(IgnisWords.UpToDate, enabled: false);
        else SetUpdateLabel(IgnisWords.UpdateNow, enabled: true);
    }

    /// <summary>The button's words now ("Update now", "Checking…", "Up to date").</summary>
    public string UpdateNowLabel { get; private set; } = IgnisWords.UpdateNow;

    /// <summary>The Model points row's line ("… Version 2026.10.08.").</summary>
    public string ModelPointsDetail => IgnisWords.ModelPointsDetail(points?.Current.Version ?? "—");

    // The row keeps one button (its width fixed, so the words change without a jump) and swaps its label's text.
    private void SetUpdateLabel(string label, bool enabled)
    {
        UpdateNowLabel = label;
        if (UpdateNowButton.Child is TextBlock text) text.Text = label;
        AutomationProperties.SetName(UpdateNowButton, label);
        UpdateNowButton.Dimmed = !enabled;
    }

    private void RefreshVersion()
    {
        if (modelPointsDetailText is not null) modelPointsDetailText.Text = ModelPointsDetail;
    }

    private void Change(Func<IgnisPreferences, IgnisPreferences> change)
    {
        settings.Update(change);
        state.Value = settings.Value;
    }

    private Control Toggle(string title, string detail, string accessibleName, Func<IgnisPreferences, bool> read, Func<IgnisPreferences, bool, IgnisPreferences> write)
    {
        var toggle = new SettingsSwitch(new Binding<bool>(() => read(state.Value), on => Change(p => write(p, on))));
        AutomationProperties.SetName(toggle, accessibleName);
        return new ChromeRow(title, () => toggle, detail);
    }

    private static IReadOnlyList<(string Id, string Text)> KeepTurnsOptions(int current)
    {
        var choices = IgnisSettings.KeepTurnsChoices.Contains(current) ? IgnisSettings.KeepTurnsChoices : IgnisSettings.KeepTurnsChoices.Append(current).Order().ToList();
        return choices.Select(turns => (turns.ToString(System.Globalization.CultureInfo.InvariantCulture), IgnisWords.KeepTurns(turns))).ToList();
    }

    // The helper model's name for the Smart helpers line: a Claude model's helperModel in the points, by its display name; else the design's.
    private string HelperModelName()
    {
        if (points is null) return IgnisWords.DefaultHelperModel;
        var helper = points.Current.Models.Where(model => model.Provider == "claude").Select(model => model.HelperModel).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        return helper is null ? IgnisWords.DefaultHelperModel : points.Find(ProviderKind.Claude, helper)?.DisplayName ?? helper;
    }

    private void RebuildHistory()
    {
        var keep = History?.Selected;
        var history = IgnisSavingsHistory.From(ledger, today());
        var view = new IgnisSavingsHistoryView(history);
        if (keep is { } index && index < history.Months.Count - 1) view.Select(index);
        historyHost.Child = view;
    }

    // .tbl-wrap: a card with the header row and one row per model; the model the chat in front uses carries "in use".
    private void RebuildTable()
    {
        var lines = new List<(string, string, string, bool)>();
        var table = new StackPanel();
        table.Children.Add(TableRow(IgnisWords.ColumnModel, IgnisWords.ColumnCheapestPoint, IgnisWords.ColumnWhy, header: true, inUse: false));
        var inUse = InUseModel();
        foreach (var model in points?.Current.Models ?? (IEnumerable<IgnisModelPoints>)[])
        {
            var point = IgnisRebuildPoints.For.Current(model);
            var used = inUse is not null && ReferenceEquals(inUse, model);
            table.Children.Add(new ChromeRowDivider { Margin = default });
            table.Children.Add(TableRow(model.DisplayName, IgnisWords.Tokens(point.AtTokens), point.Why, header: false, used));
            lines.Add((model.DisplayName, IgnisWords.Tokens(point.AtTokens), point.Why, used));
        }
        PointsTable = lines;
        tableHost.Child = new ChromeCard(() => table);
    }

    private IgnisModelPoints? InUseModel()
    {
        if (points is null) return null;
        (ProviderKind Provider, string? Model)? chat = null;
        if (chatInUse is not null) chat = chatInUse();
        else
        {
            try { if (AppEnvironment.GetAppModel(this).SelectedThread is { } thread) chat = (thread.Provider, thread.Model); }
            catch (InvalidOperationException) { }
        }
        return chat is { } found ? points.Find(found.Provider, found.Model) : null;
    }

    private static Control TableRow(string model, string point, string why, bool header, bool inUse)
    {
        var size = header ? 11.5 : 12.5;
        TextBlock Cell(string text, SwiftColor color, SwiftFont.Weight? weight = null)
        {
            var cell = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            SwiftFont.System(size, header ? SwiftFont.Weight.Semibold : weight).Apply(cell);
            cell.Foreground = color.Brush(cell);
            return cell;
        }
        Control first = Cell(model, header ? Chrome.SecondaryText : Chrome.PrimaryText);
        if (inUse)
        {
            var chip = new Border { Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = IgnisSettingsWords.InUse, FontSize = 10.5 } };
            chip.Background = Chrome.Accent.Opacity(0.13).Brush(chip);
            ((TextBlock)chip.Child).Foreground = Chrome.Accent.Brush(chip);
            first = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { first, chip } };
        }
        var pointCell = Cell(point, header ? Chrome.SecondaryText : Chrome.PrimaryText);
        var whyCell = Cell(why, Chrome.SecondaryText);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,12,100,12,*"), Margin = new Thickness(12, 9), Children = { first, pointCell, whyCell } };
        Grid.SetColumn(pointCell, 2);
        Grid.SetColumn(whyCell, 4);
        return grid;
    }
}
