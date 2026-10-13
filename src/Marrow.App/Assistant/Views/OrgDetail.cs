// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "Clicking a node shows its detail: duties, memory count, grants, reserve, and buttons for Talk, Pause and Settings"; the canvas's renderOdet and .odet,
// work/design/assistant/index.html lines 434-436 and 2878-2886; ED-NEW-dextra-as17-5). No Swift twin.
// The detail of the selected node, under the chart: who it is, what it looks after, its tasks and duties, its notes and grants, today's use against its slice and its reserve; then Talk, Pause (which
// reads Resume once every task it can reach is paused), Settings and, for an Astur, Retire. Fields have fixed places, so a selection of another node changes the words and never the layout;
// when the words change they come in on a short rise (none with Reduce motion). Pause and Resume act on that assistant's own tasks through the board's calls and say what they did. Retire asks
// first, inline (focus on "Keep it", Escape closes and gives the focus back); the controller refuses while the Astur has a task at work, and the answer is told. Buttons are disabled, never removed.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>What the Team tab's controls reach outside the panel; a missing one disables its button.</summary>
public sealed record TeamActions
{
    /// <summary>Talk to this assistant: Dextra's home, or an Astur's own thread.</summary>
    public Action<string>? Talk { get; init; }

    /// <summary>The assistant's settings: an Astur's profile sheet in Edit mode, or Settings > Assistant for Dextra.</summary>
    public Action<string>? Edit { get; init; }

    /// <summary>The profile sheet in Add mode.</summary>
    public Action? Add { get; init; }

    /// <summary>Retires an Astur; null when it was, else why not (the controller's sentence).</summary>
    public Func<string, Task<string?>>? Retire { get; init; }

    /// <summary>Opens a task's lead chat (a chip).</summary>
    public Action<Guid>? OpenChat { get; init; }
}

/// <summary>A small text button (26 px) whose title can change in place, so the focus stays on it when Pause becomes Resume.</summary>
public sealed class DetailButton : Button
{
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
    private readonly Border face = new() { Padding = new Thickness(10, 0), Height = 26 };
    private readonly IBrush rest, hot, inkFill, inkHot;
    private readonly bool primary;
    private bool over;

    protected override Type StyleKeyOverride => typeof(Button);

    public DetailButton(string title, bool primary = false)
    {
        this.primary = primary;
        SwiftFont.System(12, SwiftFont.Weight.Medium).Apply(label);
        face.Child = label;
        face.Capsule(RoundedCornerStyle.Continuous);
        rest = Chrome.Overlay(.06).Brush(face);
        hot = Chrome.Overlay(.1).Brush(face);
        inkFill = Chrome.PrimaryText.Brush(face);
        inkHot = Chrome.PrimaryText.Opacity(.86).Brush(face);
        label.Foreground = primary ? TaskLook.Inverse.Brush(label) : Chrome.PrimaryText.Brush(label);
        Content = face;
        Padding = default;
        Background = Brushes.Transparent;
        BorderThickness = default;
        this.KeyboardFocus(13);
        Title = title;
        Paint();
        PointerEntered += (_, _) => { over = true; Paint(); };
        PointerExited += (_, _) => { over = false; Paint(); };
    }

    public string Title
    {
        get => label.Text ?? "";
        set => label.Text = value;
    }

    private void Paint()
    {
        face.Background = primary ? (over ? inkHot : inkFill) : (over ? hot : rest);
        face.Opacity = IsEnabled ? 1 : .35;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEnabledProperty) Paint();
    }
}

public sealed class OrgDetail : Decorator
{
    private readonly ITeamSource source;
    private readonly TeamActions actions;
    private readonly Action<string> said;
    private readonly Border card = new() { BorderThickness = new Thickness(1), Padding = new Thickness(14, 12) };
    private readonly StackPanel root = new() { Orientation = Orientation.Vertical, Spacing = 10 };
    private readonly WrapPanel fields = new() { Orientation = Orientation.Horizontal, ItemSpacing = 14, LineSpacing = 10 };
    private readonly Dictionary<string, TextBlock> values = [];
    private readonly WrapPanel buttons = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
    private readonly RevealBox askBox = new();
    private readonly TextBlock askText;
    private TeamMember? member;
    private bool busy;

    public static readonly string[] Labels = ["Assistant", "Area", "Missions \U000000B7 duties", "Memory notes \U000000B7 grants", "Today's usage", "Reserve"];

    public OrgDetail(ITeamSource source, TeamActions? actions = null, Action<string>? said = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.actions = actions ?? new TeamActions();
        this.said = said ?? (_ => { });

        card.RoundedRectangle(OrgNode.Radius, RoundedCornerStyle.Continuous);
        card.Background = TaskLook.Card.Brush(card);
        card.BorderBrush = TaskLook.Line.Brush(card);
        foreach (var label in Labels)
        {
            var value = TaskLook.Text("", 12.5, SwiftFont.Weight.Regular, TaskLook.Ink, wrap: true);
            values[label] = value;
            var head = TaskLook.Text(label, 11, SwiftFont.Weight.Regular, TaskLook.Ink2);
            var cell = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, Width = label == "Area" ? double.NaN : 168, Children = { head, value } };
            // the Area is the longest sentence: it takes the whole first line
            if (label == "Area") cell.MinWidth = 168;
            fields.Children.Add(cell);
        }
        Talk = new DetailButton("Talk", primary: true);
        Pause = new DetailButton("Pause");
        Settings = new DetailButton("Settings");
        Retire = new DetailButton("Retire");
        Talk.Click += (_, _) => { if (member is { } m) this.actions.Talk?.Invoke(m.Id); };
        Pause.Click += (_, _) => PauseOrResume();
        Settings.Click += (_, _) => { if (member is { } m) this.actions.Edit?.Invoke(m.Id); };
        Retire.Click += (_, _) => OpenAsk();
        foreach (var b in new[] { Talk, Pause, Settings, Retire }) buttons.Children.Add(b);

        askText = TaskLook.Text("", 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        askText.MinWidth = 160;
        Keep = new DetailButton("Keep it");
        Confirm = new DetailButton("Retire", primary: true);
        Keep.Click += (_, _) => CloseAsk(refocus: true);
        Confirm.Click += async (_, _) => await RetireNow();
        var askRow = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6, Children = { askText, Keep, Confirm } };
        askBox.Child = askRow;
        askRow.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseAsk(refocus: true);
        };
        AutomationProperties.SetControlTypeOverride(askRow, AutomationControlType.Group);

        root.Children.Add(fields);
        root.Children.Add(buttons);
        root.Children.Add(askBox);
        card.Child = root;
        Child = card;
        AutomationProperties.SetControlTypeOverride(card, AutomationControlType.Group);
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public DetailButton Talk { get; }

    public DetailButton Pause { get; }

    public DetailButton Settings { get; }

    public DetailButton Retire { get; }

    public DetailButton Keep { get; }

    public DetailButton Confirm { get; }

    public TeamMember? Member => member;

    public bool AskOpen => askBox.IsOpen;

    /// <summary>The inline ask (tests read its name).</summary>
    public Control AskPanel => askBox;

    /// <summary>The fields (tests read the rise).</summary>
    public Control FieldsPanel => fields;

    /// <summary>The words of one field, by its label.</summary>
    public string Field(string label) => values[label].Text ?? "";

    public IReadOnlyList<string> FieldLabels => Labels;

    /// <summary>Shows this assistant. The same facts again change nothing; another assistant's words come in on a short rise.</summary>
    public void Show(TeamMember next, bool still)
    {
        var other = member is null || member.Id != next.Id;
        if (member == next) return;
        member = next;
        var area = next.IsDextra ? "Everything that spans areas: it delegates to the Asturs and merges what they bring back." : next.Profile.Area;
        values["Assistant"].Text = next.Name;
        values["Area"].Text = string.IsNullOrWhiteSpace(area) ? "Not described yet" : area;
        values["Missions \U000000B7 duties"].Text = OrgChartWords.Pair(OrgChartWords.Count(next.Missions.Count, "mission", "missions"), OrgChartWords.Count(next.Facts.Duties, "duty", "duties"));
        values["Memory notes \U000000B7 grants"].Text = OrgChartWords.Pair(OrgChartWords.Count(next.Facts.Notes, "note", "notes"), OrgChartWords.Count(next.Facts.Grants, "grant", "grants"));
        values["Today's usage"].Text = next.MeterWords.Length > 0 ? next.MeterWords : "Not measured";
        var slice = OrgChartWords.SliceLine(next.Profile.Budget);
        values["Reserve"].Text = next.Facts.Reserve.Length > 0 ? slice + ". " + next.Facts.Reserve : slice;

        AutomationProperties.SetName(card, "Details of " + next.Name);
        AutomationProperties.SetName(Talk, "Talk to " + next.Name);
        AutomationProperties.SetName(Settings, "Open " + next.Name + "'s settings");
        AutomationProperties.SetName(Retire, "Retire " + next.Name);
        Retire.IsVisible = !next.IsDextra;
        Talk.IsEnabled = actions.Talk is not null;
        Settings.IsEnabled = actions.Edit is not null;
        Retire.IsEnabled = actions.Retire is not null && !busy;
        ShowPause(next);
        if (other && askBox.IsOpen) CloseAsk(refocus: false);
        if (other && !still) Rise();
    }

    private void ShowPause(TeamMember next)
    {
        var resume = !next.CanPause && next.CanResume;
        Pause.Title = resume ? "Resume" : "Pause";
        Pause.IsEnabled = next.CanPause || next.CanResume;
        AutomationProperties.SetName(Pause, (resume ? "Resume " : "Pause ") + next.Name + "'s tasks");
    }

    private void PauseOrResume()
    {
        if (member is not { } m) return;
        var line = !m.CanPause && m.CanResume ? source.ResumeTasks(m.Id) : source.PauseTasks(m.Id);
        said(line);
    }

    // ---- Retire asks first, inline ----

    private void OpenAsk()
    {
        if (member is not { IsDextra: false } m || actions.Retire is null) return;
        askText.Text = $"Retire {m.Name}? It is no longer listed or called, and its budget slice is released. Its folder and notes stay until you delete them.";
        AutomationProperties.SetName(askBox, "Confirm retiring " + m.Name);
        askBox.Open(true, TaskLook.Still(this));
        Keep.Focus(NavigationMethod.Tab);
    }

    private void CloseAsk(bool refocus)
    {
        askBox.Open(false, TaskLook.Still(this));
        if (refocus && Retire.IsEffectivelyVisible && Retire.IsEnabled) Retire.Focus(NavigationMethod.Tab);
    }

    private async Task RetireNow()
    {
        if (member is not { IsDextra: false } m || actions.Retire is not { } retire || busy) return;
        busy = true;
        Confirm.IsEnabled = false;
        Keep.IsEnabled = false;
        string? problem;
        try { problem = await retire(m.Id); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            problem = $"nth could not retire {m.Name}: {error.Message}";
        }
        busy = false;
        Confirm.IsEnabled = true;
        Keep.IsEnabled = true;
        CloseAsk(refocus: problem is not null);
        said(problem ?? $"{m.Name} is retired. Its folder and notes stay.");
    }

    // the words come in on a short rise: a transform and an opacity, over 280 ms
    private void Rise()
    {
        var shift = fields.RenderTransform as TranslateTransform ?? new TranslateTransform();
        shift.Transitions = null;
        shift.Y = 6;
        fields.RenderTransform = shift;
        fields.Transitions = null;
        fields.Opacity = 0;
        var ease = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1);
        shift.Transitions = [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = ease }];
        fields.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = ease }];
        shift.Y = 0;
        fields.Opacity = 1;
    }
}
