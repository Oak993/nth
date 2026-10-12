// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5: "Filter by assistant, mission, class or date";
// unit AS-21). No Swift counterpart.
// ED-NEW-dextra-as21-6: the filter bar.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>
/// What the bar's controls read and write. A picker keeps its text from an observable value, so the filter lives here as
/// one, and the model's filter is copied in whenever it changes elsewhere ("Clear filters", a mission card's Lineage button,
/// "Show line"). A write from a control goes to the model; a copy from the model does not write back.
/// </summary>
internal sealed class ActivityFilterState : MainActorModel
{
    private string? assistant;
    private string? mission;
    private string? actionClass;
    private string? day;
    private string text = "";
    private ActivityViewMode mode;

    public string? Assistant { get => Read(assistant); set => Set(ref assistant, value); }

    public string? Mission { get => Read(mission); set => Set(ref mission, value); }

    public string? Class { get => Read(actionClass); set => Set(ref actionClass, value); }

    /// <summary>A day choice: null (any), <c>today</c>, <c>7</c> (the last 7 days), <c>30</c>, a day as <c>yyyy-MM-dd</c>, or <c>range</c>.</summary>
    public string? Day { get => Read(day); set => Set(ref day, value); }

    public string Text { get => Read(text); set => Set(ref text, value); }

    public ActivityViewMode Mode { get => Read(mode); set => Set(ref mode, value); }

    public const string Today = "today";
    public const string Last7 = "7";
    public const string Last30 = "30";
    public const string Range = "range";

    /// <summary>The day choice a filter's two days make.</summary>
    public static string? DayChoiceOf(ActivityFilter filter, DateOnly today)
    {
        if (filter.From is null && filter.To is null) return null;
        if (filter.From is { } from && filter.To is { } to)
        {
            if (from == to) return from == today ? Today : from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (to == today && from == today.AddDays(-6)) return Last7;
            if (to == today && from == today.AddDays(-29)) return Last30;
        }
        return Range;
    }

    /// <summary>The first and last day a day choice means.</summary>
    public static (DateOnly? From, DateOnly? To) DaysOf(string? choice, DateOnly today) => choice switch
    {
        null => (null, null),
        Today => (today, today),
        Last7 => (today.AddDays(-6), today),
        Last30 => (today.AddDays(-29), today),
        _ when DateOnly.TryParseExact(choice, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var one) => (one, one),
        _ => (null, null),
    };

    /// <summary>Copies a filter in; true when anything shown changed.</summary>
    public bool Apply(ActivityFilter filter, ActivityViewMode viewMode, DateOnly today)
    {
        var changed = false;
        void Put<T>(T current, T value, Action<T> set)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            set(value);
            changed = true;
        }
        var choice = DayChoiceOf(filter, today);
        Put(assistant, filter.Assistant, v => Assistant = v);
        Put(mission, filter.Mission, v => Mission = v);
        Put(actionClass, filter.Class, v => Class = v);
        Put(day, choice, v => Day = v);
        Put(mode, viewMode, v => Mode = v);
        // The text box keeps what is being typed: it is copied only when the filter's text is not what the box already means.
        if (filter.Text.Trim() != text.Trim()) Put(text, filter.Text, v => Text = v);
        return changed;
    }
}

/// <summary>
/// The filter bar of the Activity view (12.5: "Filter by assistant, mission, class or date"): four glass pickers whose choices
/// are the values the log holds (with the newest missions first), a search field, the Log and Lineage switch, and Clear
/// filters. The search waits 150 ms after the last key before it filters, so typing never queues a rebuild per key; Enter does
/// not wait. The bar keeps its size when a filter is set or cleared (Clear filters is laid out and only fades).
/// </summary>
internal sealed class ActivityFilterBar : Decorator
{
    public const double Spacing = 8;
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);
    private const int MissionChoices = 100;

    private readonly ActivityLogModel model;
    private readonly ActivityFilterState state = new();
    private readonly WrapPanel wrap = new() { Orientation = Orientation.Horizontal };
    private readonly Decorator assistantSlot = new();
    private readonly Decorator missionSlot = new();
    private readonly Decorator classSlot = new();
    private readonly Decorator daySlot = new();
    private readonly Decorator clearSlot = new();
    private readonly DispatcherTimer searchTimer;
    private bool applying;

    public ActivityFilterBar(ActivityLogModel model)
    {
        this.model = model;
        searchTimer = new DispatcherTimer { Interval = SearchDelay };
        searchTimer.Tick += (_, _) => FlushSearch();
        foreach (var slot in new[] { assistantSlot, missionSlot, classSlot, daySlot })
        {
            slot.Margin = new Thickness(0, 0, Spacing, Spacing);
            wrap.Children.Add(slot);
        }
        var search = new ChromeSearchField(new Binding<string>(() => state.Text, value =>
        {
            state.Text = value;
            if (!applying) searchTimer.Start(); // restarts the wait
        }), ActivityWords.SearchPrompt);
        search.Margin = new Thickness(0, 0, Spacing, Spacing);
        search.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            FlushSearch();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        wrap.Children.Add(search);
        var modePicker = new ChromeSegmentedPicker<ActivityViewMode>(
            [
                new ChromeSegmentedOption<ActivityViewMode>(ActivityViewMode.Log, ActivityWords.LogMode, "list.bullet"),
                new ChromeSegmentedOption<ActivityViewMode>(ActivityViewMode.Lineage, ActivityWords.LineageMode, "arrow.triangle.branch"),
            ],
            new Binding<ActivityViewMode>(() => state.Mode, value => _ = model.SetModeAsync(value)));
        modePicker.Margin = new Thickness(0, 0, Spacing, Spacing);
        modePicker.VerticalAlignment = VerticalAlignment.Top;
        wrap.Children.Add(modePicker);
        clearSlot.Margin = new Thickness(0, 0, 0, Spacing);
        wrap.Children.Add(clearSlot);
        clearSlot.Child = BuildClear();
        Child = wrap;
        Sync();
        AttachedToVisualTree += (_, _) =>
        {
            model.Changed -= OnModelChanged;
            model.Changed += OnModelChanged;
            Sync(); // what changed while the bar was away
        };
        DetachedFromVisualTree += (_, _) =>
        {
            searchTimer.Stop();
            model.Changed -= OnModelChanged;
        };
    }

    /// <summary>The picker for a field, or null before the log is read (tests find the controls through these).</summary>
    public GlassPickerButton<string?>? AssistantPicker => assistantSlot.Child as GlassPickerButton<string?>;

    public GlassPickerButton<string?>? MissionPicker => missionSlot.Child as GlassPickerButton<string?>;

    public GlassPickerButton<string?>? ClassPicker => classSlot.Child as GlassPickerButton<string?>;

    public GlassPickerButton<string?>? DayPicker => daySlot.Child as GlassPickerButton<string?>;

    public Button ClearButton { get; private set; } = null!;

    public ActivityFilterState State => state;

    /// <summary>Filters now by what the box holds, without waiting out the delay.</summary>
    public void FlushSearch()
    {
        searchTimer.Stop();
        var typed = state.Text;
        if (typed.Trim() == model.Filter.Text.Trim()) return;
        _ = model.UpdateFilterAsync(f => f with { Text = typed });
    }

    private Control BuildClear()
    {
        var label = new TextBlock { Text = ActivityWords.ClearFilters, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Chrome.SecondaryText.Brush(label);
        var face = new Border { Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), Height = Chrome.CapsuleContentHeight + Chrome.CapsuleVerticalPadding * 2, Child = label, Background = Brushes.Transparent };
        ClearButton = PlainControls.Button(face, () => _ = model.ClearFilterAsync());
        AutomationProperties.SetName(ClearButton, ActivityWords.ClearFilters);
        return ClearButton;
    }

    private void OnModelChanged(ActivityChange change)
    {
        if ((change & (ActivityChange.Filter | ActivityChange.Mode)) != 0) CopyFilter();
        if ((change & ActivityChange.Snapshot) != 0) Rebuild();
        if ((change & (ActivityChange.Filter | ActivityChange.Snapshot)) != 0) ShowClear();
    }

    private void Sync()
    {
        CopyFilter();
        Rebuild();
        ShowClear();
    }

    private void CopyFilter()
    {
        applying = true;
        try
        {
            state.Apply(model.Filter, model.Mode, model.Today);
        }
        finally
        {
            applying = false;
        }
    }

    private void ShowClear()
    {
        var active = !model.Filter.IsEmpty;
        ClearButton.IsEnabled = active;
        ClearButton.Opacity = active ? 1 : 0;
        if (active) ClearButton.ClearValue(AutomationProperties.AccessibilityViewProperty);
        else AutomationProperties.SetAccessibilityView(ClearButton, AccessibilityView.Raw);
    }

    /// <summary>Builds the four pickers from what the log holds (they are fixed lists, so they are made again when the log is read again).</summary>
    private void Rebuild()
    {
        var facets = model.Snapshot?.Facets ?? ActivityFacets.Empty;
        var labels = model.Labels;
        assistantSlot.Child = Picker("Assistant", ActivityWords.AnyAssistant,
            facets.Assistants.Select(f => ((string?)f.Value, labels.ForAssistant(f.Value))),
            new Binding<string?>(() => state.Assistant, v =>
            {
                state.Assistant = v;
                Push(f => f with { Assistant = v });
            }));
        var missions = facets.Missions.Take(MissionChoices).Select(f => ((string?)f.Value, labels.ForMission(f.Value))).ToList();
        if (model.Filter.Mission is { Length: > 0 } picked && missions.All(m => m.Item1 != picked)) missions.Add((picked, labels.ForMission(picked)));
        if (facets.NoMission > 0) missions.Add((ActivityFilter.None, ActivityWords.NoMission));
        missionSlot.Child = Picker("Mission", ActivityWords.AnyMission, missions,
            new Binding<string?>(() => state.Mission, v =>
            {
                state.Mission = v;
                Push(f => f with { Mission = v });
            }));
        classSlot.Child = Picker("Class", ActivityWords.AnyClass,
            facets.Classes.Select(f => ((string?)f.Value, f.Value)),
            new Binding<string?>(() => state.Class, v =>
            {
                state.Class = v;
                Push(f => f with { Class = v });
            }));
        var days = new List<(string?, string)>
        {
            (ActivityFilterState.Today, "Today"),
            (ActivityFilterState.Last7, "Last 7 days"),
            (ActivityFilterState.Last30, "Last 30 days"),
        };
        foreach (var day in facets.Days.Take(60)) days.Add((day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ActivityWords.DayChoice(day, model.Today)));
        if (state.Day == ActivityFilterState.Range) days.Add((ActivityFilterState.Range, "Custom range"));
        daySlot.Child = Picker("Day", ActivityWords.AnyDay, days,
            new Binding<string?>(() => state.Day, v =>
            {
                state.Day = v;
                var (from, to) = ActivityFilterState.DaysOf(v, model.Today);
                Push(f => f with { From = from, To = to });
            }));
    }

    private void Push(Func<ActivityFilter, ActivityFilter> change)
    {
        if (applying) return;
        _ = model.UpdateFilterAsync(change);
    }

    private static GlassPickerButton<string?> Picker(string name, string any, IEnumerable<(string?, string)> choices, Binding<string?> selection)
    {
        var options = new List<(string? Value, string Title)> { (null, any) };
        options.AddRange(choices.Select(c => (c.Item1, c.Item2)));
        var picker = new GlassPickerButton<string?>(options, selection, maxWidth: 220);
        AutomationProperties.SetName(picker, name);
        return picker;
    }
}
