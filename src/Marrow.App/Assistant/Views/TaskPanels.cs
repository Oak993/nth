// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.6b "Layout, controls and access" (the inline ask, focus), "Heads", "Motion"; the canvas's
// reveal, askHtml, paintAsk and paintHeads, work/design/assistant/index.html lines 1950-2026; ED-NEW-dextra-as8-4). No Swift twin.
// What grows under a task: the inline ask (never a modal) and the heads panel. Both grow from zero height (380 ms in, 300 ms out)
// and their neighbours follow the layout, so nothing slides past a panel's edge; with Reduce motion they are there at once. The ask
// puts focus on Stop now (Keep changes for Revert); Escape and the Keep button close it and give focus back to the control that
// opened it, or, when that one is now disabled, to the first live control of the same row, never to the page. The 5 s "Still
// pausing" note opens without taking focus.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>A box that grows from zero height to its content's and folds back, its neighbours following the layout.</summary>
public sealed class RevealBox : Decorator
{
    public static readonly StyledProperty<double> RevealProperty = AvaloniaProperty.Register<RevealBox, double>(nameof(Reveal));

    public static readonly TimeSpan In = TimeSpan.FromMilliseconds(380);
    public static readonly TimeSpan Out = TimeSpan.FromMilliseconds(300);

    static RevealBox() => AffectsMeasure<RevealBox>(RevealProperty);

    public RevealBox()
    {
        ClipToBounds = true;
        IsVisible = false;
    }

    /// <summary>0 folded, 1 open.</summary>
    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    /// <summary>Open now (after any move finished) or about to be.</summary>
    public bool IsOpen { get; private set; }

    private DispatcherTimer? hide;

    public void Open(bool open, bool still)
    {
        if (open == IsOpen && (open ? Reveal >= 1 : !IsVisible)) return;
        IsOpen = open;
        hide?.Stop();
        hide = null;
        if (still)
        {
            Transitions = null;
            Reveal = open ? 1 : 0;
            IsVisible = open;
            return;
        }
        Transitions = [new DoubleTransition { Property = RevealProperty, Duration = open ? In : Out, Easing = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }];
        if (open) IsVisible = true;
        Reveal = open ? 1 : 0;
        if (!open)
        {
            hide = new DispatcherTimer { Interval = Out };
            hide.Tick += (_, _) =>
            {
                hide?.Stop();
                hide = null;
                if (!IsOpen) IsVisible = false;
            };
            hide.Start();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var full = base.MeasureOverride(availableSize);
        return new Size(full.Width, full.Height * Math.Clamp(Reveal, 0, 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // the content keeps its own height while the box grows, so it is uncovered, never squeezed
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, Child.DesiredSize.Height)));
        return finalSize;
    }
}

/// <summary>Shared look of the task lists: tinted fills on the pane, no glass on glass; every brush made once per control.</summary>
internal static class TaskLook
{
    public static TextBlock Text(string text, double size, SwiftFont.Weight weight, SwiftColor ink, bool wrap = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        SwiftFont.System(size, weight).Apply(block);
        block.Foreground = ink.Brush(block);
        return block;
    }

    public static SwiftColor Ink => Chrome.PrimaryText;

    /// <summary>
    /// The second ink of the detail line, head notes, the footer and every small line: the label colour at 70 % in light; in dark,
    /// white at 72 %, a little above the app's 70 % secondary text, so these lines hold 4.5:1 even on the lightest dark surface
    /// (AccentPalette.DarkCard, 5.0:1 under a card's fill), not only on the dark window (r4 B16, ED-NEW-dextra-as8-4).
    /// </summary>
    public static SwiftColor Ink2 { get; } = SwiftColor.Dynamic(
        light: new SwiftColor(red: 0, green: 0, blue: 0, opacity: 0.85 * 0.7),
        dark: new SwiftColor(red: 1, green: 1, blue: 1, opacity: 0.72));

    /// <summary>A card's tinted fill, its hairline, and the stronger hairline of a card that needs the owner.</summary>
    public static SwiftColor Card => Chrome.Overlay(.035);

    public static SwiftColor Line => Chrome.Overlay(.08);

    public static SwiftColor Line2 => Chrome.Overlay(.16);

    /// <summary>The disclosure chevron (12 px, the canvas's M9 6l6 6-6 6), turning a quarter on a spring when open.</summary>
    public static Avalonia.Controls.Shapes.Path Chevron()
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M4.5 3 L7.5 6 L4.5 9"),
            Width = 12,
            Height = 12,
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(0),
            RenderTransformOrigin = RelativePoint.Center,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(path, AccessibilityView.Raw);
        path.Stroke = Ink2.Brush(path);
        return path;
    }

    public static void Turn(Avalonia.Controls.Shapes.Path chevron, bool open, bool still)
    {
        if (chevron.RenderTransform is not RotateTransform turn) return;
        turn.Transitions = still ? null : [new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(450), Easing = TaskMetrics.Spring() }];
        turn.Angle = open ? 90 : 0;
    }

    /// <summary>Text on an ink fill: near white in light mode, near black in dark (about 15:1 on the ink either way).</summary>
    public static SwiftColor Inverse => SwiftColor.Dynamic(new SwiftColor(red: .97, green: .97, blue: .98), new SwiftColor(red: .08, green: .08, blue: .09));

    /// <summary>A small text button (26 px): the ask's answers, the footer's Revert changes, the stop strip's actions.</summary>
    public static (Button Button, Control Shown) Small(string title, Action action, bool primary = false, string? name = null)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        SwiftFont.System(12, SwiftFont.Weight.Medium).Apply(label);
        var face = new Border { Padding = new Thickness(10, 0), Height = 26, Child = label };
        face.Capsule(RoundedCornerStyle.Continuous);
        var button = PlainControls.Button(face, action);
        button.KeyboardFocus(13);
        AutomationProperties.SetName(button, name ?? title);
        IBrush rest, hot;
        if (primary)
        {
            // the primary answer: an ink capsule with the card's colour as its text (about 15:1 in both themes)
            rest = Chrome.PrimaryText.Brush(face);
            hot = Chrome.PrimaryText.Opacity(.86).Brush(face);
            label.Foreground = Inverse.Brush(label);
        }
        else
        {
            rest = Chrome.Overlay(.06).Brush(face);
            hot = Chrome.Overlay(.1).Brush(face);
            label.Foreground = Chrome.PrimaryText.Brush(label);
        }
        face.Background = rest;
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = rest;
        return (button, button);
    }

    public static bool Still(Control control) =>
        EnvironmentValues.GetAccessibilityReduceMotion(control) || Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
}

/// <summary>The inline ask under a task or a head: a sentence and its buttons, in a group named for what it confirms.</summary>
public sealed class TaskAskPanel : Decorator
{
    private readonly RevealBox box = new();
    private readonly WrapPanel content = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6 };
    private readonly TextBlock sentence = new() { TextWrapping = TextWrapping.Wrap, MinWidth = 160 };
    private readonly Dictionary<AskAnswer, Button> buttons = [];
    private string? shownKey;
    private readonly IBrush leadInk;

    public TaskAskPanel()
    {
        sentence.Inlines = [];
        leadInk = TaskLook.Ink.Brush(sentence);
        SwiftFont.System(12).Apply(sentence);
        sentence.Foreground = TaskLook.Ink2.Brush(sentence);
        box.Child = new Border { Padding = new Thickness(0, 2, 0, 8), Child = content };
        Child = box;
        // Escape closes the ask, wherever focus is inside it
        AddHandler(KeyDownEvent, (object? _, KeyEventArgs e) =>
        {
            if (e.Key != Key.Escape || Kind == AskKind.None) return;
            e.Handled = true;
            Answered?.Invoke(this, Kind == AskKind.Revert ? AskAnswer.KeepChanges : AskAnswer.KeepRunning);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public AskKind Kind { get; private set; }

    public string Sentence => string.Concat(sentence.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []);

    public IReadOnlyDictionary<AskAnswer, Button> Buttons => buttons;

    public bool IsOpen => box.IsOpen;

    /// <summary>Raised when the owner picks an answer, or presses Escape (Keep).</summary>
    public event Action<TaskAskPanel, AskAnswer>? Answered;

    /// <summary>Shows <paramref name="text"/> (or folds when <paramref name="kind"/> is None); focus goes to its first answer when <paramref name="focus"/>.</summary>
    public void Show(AskKind kind, TaskWords.AskText? text, bool focus)
    {
        var still = TaskLook.Still(this);
        if (kind == AskKind.None || text is null)
        {
            Kind = AskKind.None;
            shownKey = null;
            box.Open(false, still);
            AutomationProperties.SetName(this, null);
            return;
        }
        var key = kind + "|" + text.Lead + text.Body + string.Join(",", text.Buttons.Select(b => b.Answer));
        var focusedAnswer = buttons.FirstOrDefault(b => b.Value.IsFocused).Key;
        var hadFocus = buttons.Values.Any(b => b.IsFocused);
        if (key != shownKey)
        {
            shownKey = key;
            Kind = kind;
            AutomationProperties.SetName(this, text.Group);
            sentence.Inlines!.Clear();
            sentence.Inlines.Add(new Avalonia.Controls.Documents.Run(text.Lead) { FontWeight = FontWeight.SemiBold, Foreground = leadInk });
            if (text.Body.Length > 0) sentence.Inlines.Add(new Avalonia.Controls.Documents.Run(" " + text.Body.Trim()));
            content.Children.Clear();
            buttons.Clear();
            content.Children.Add(sentence);
            foreach (var (answer, label, primary) in text.Buttons)
            {
                var pick = answer;
                var (button, shown) = TaskLook.Small(label, () => Answered?.Invoke(this, pick), primary);
                buttons[answer] = button;
                content.Children.Add(shown);
            }
            if (hadFocus) (buttons.GetValueOrDefault(focusedAnswer) ?? buttons.Values.FirstOrDefault())?.Focus(NavigationMethod.Tab);
        }
        box.Open(true, still);
        if (focus)
        {
            // Stop now for a stop, Keep changes for a revert; the still-pausing note never takes focus
            var target = kind == AskKind.Revert ? buttons.GetValueOrDefault(AskAnswer.KeepChanges) : kind == AskKind.Stop ? buttons.GetValueOrDefault(AskAnswer.StopNow) : null;
            target?.Focus(NavigationMethod.Tab);
        }
    }
}

/// <summary>One head's line in the heads panel: glyph, name and note, the same 86 px label and the same two slots as its task.</summary>
public sealed class HeadLine : Decorator
{
    private readonly StateGlyph glyph = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock name = TaskLook.Text("", 12, SwiftFont.Weight.Regular, TaskLook.Ink);
    private readonly TextBlock note = TaskLook.Text("", 11, SwiftFont.Weight.Regular, TaskLook.Ink2);
    private readonly Grid line = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8, MinHeight = 30 };

    public HeadLine(Guid head)
    {
        Head = head;
        Ask = new TaskAskPanel { Margin = new Thickness(18, 0, 0, 0) };
        var names = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { name, note } };
        Grid.SetColumn(names, 1);
        Grid.SetColumn(Label, 2);
        Grid.SetColumn(Slots, 3);
        line.Children.Add(glyph);
        line.Children.Add(names);
        line.Children.Add(Label);
        line.Children.Add(Slots);
        Child = new StackPanel { Orientation = Orientation.Vertical, Children = { line, Ask } };
    }

    public Guid Head { get; }

    public StateLabel Label { get; } = new();

    public TaskSlots Slots { get; } = new();

    public TaskAskPanel Ask { get; }

    public string NameText => name.Text ?? "";

    public string NoteText => note.Text ?? "";

    public void Show(HeadJob head, string task, bool narrow)
    {
        glyph.Key = StateGlyph.KeyOf(head.State);
        name.Text = head.Name;
        note.Text = head.Note;
        note.IsVisible = head.Note.Length > 0;
        Label.Narrow = narrow;
        line.ColumnSpacing = narrow ? 4 : 8; // the task line's gap, so the label and slots stay in its columns
        Label.Show(head.State);
        Slots.Set(head.Slots, TaskWords.HeadSubject(head.Name, task), TaskLook.Still(this));
    }
}

/// <summary>The heads panel of an expanded task: one line per head and the footer (Revert changes on a stopped task only).</summary>
public sealed class HeadsPanel : Decorator
{
    private readonly RevealBox box = new();
    private readonly StackPanel lines = new() { Orientation = Orientation.Vertical, Spacing = 1 };
    private readonly TextBlock footer = TaskLook.Text("", 11.5, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
    private readonly Dictionary<Guid, HeadLine> byHead = [];
    private readonly HeadLine lead = new(Guid.Empty);

    public HeadsPanel()
    {
        var (revert, shown) = TaskLook.Small("Revert changes", () => RevertAsked?.Invoke(), name: "Revert changes");
        RevertButton = revert;
        RevertShown = shown;
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(shown, 1);
        foot.Children.Add(footer);
        foot.Children.Add(shown);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 2) };
        rule.Background = Chrome.Overlay(.08).Brush(rule);
        box.Child = new StackPanel { Orientation = Orientation.Vertical, Children = { lines, rule, foot } };
        Child = box;
    }

    public bool IsOpen => box.IsOpen;

    public Button RevertButton { get; }

    internal Control RevertShown { get; }

    public IReadOnlyList<HeadLine> Lines => [.. lines.Children.OfType<HeadLine>()];

    public string FooterText => footer.Text ?? "";

    /// <summary>The footer's Revert changes was pressed.</summary>
    public event Action? RevertAsked;

    /// <summary>A head's slot was pressed: the head's thread and the kind.</summary>
    public event Action<HeadLine, SlotKind>? HeadSlot;

    /// <summary>A head's ask was answered.</summary>
    public event Action<HeadLine, AskAnswer>? HeadAnswered;

    public void Open(bool open) => box.Open(open, TaskLook.Still(this));

    public HeadLine? Line(Guid head) => byHead.GetValueOrDefault(head);

    public void Show(MissionJob job, bool narrow)
    {
        if (job.Heads.Count == 0)
        {
            byHead.Clear();
            if (!lines.Children.Contains(lead)) { lines.Children.Clear(); lines.Children.Add(lead); }
            lead.Show(new HeadJob(Guid.Empty, "Lead chat", job.State switch
            {
                MissionState.Done => HeadState.Done,
                MissionState.Paused => HeadState.Paused,
                MissionState.Pausing => HeadState.Pausing,
                MissionState.Stopping => HeadState.Stopping,
                MissionState.Stopped => HeadState.Stopped,
                MissionState.Blocked or MissionState.Failed or MissionState.NeedsYou => HeadState.Blocked,
                _ => HeadState.Working,
            })
            { Note = "one agent, no Hydra heads", Slots = new ControlSlots(SlotA.None, false, SlotB.None, false) }, job.Title, narrow);
        }
        else
        {
            if (lines.Children.Contains(lead)) lines.Children.Remove(lead);
            var keep = job.Heads.Select(h => h.Thread).ToHashSet();
            foreach (var gone in byHead.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                lines.Children.Remove(byHead[gone]);
                byHead.Remove(gone);
            }
            var index = 0;
            foreach (var head in job.Heads)
            {
                if (!byHead.TryGetValue(head.Thread, out var line))
                {
                    line = new HeadLine(head.Thread);
                    var made = line;
                    line.Slots.A.Clicked += (_, kind) => HeadSlot?.Invoke(made, kind);
                    line.Slots.B.Clicked += (_, kind) => HeadSlot?.Invoke(made, kind);
                    line.Ask.Answered += (_, answer) => HeadAnswered?.Invoke(made, answer);
                    byHead[head.Thread] = line;
                    lines.Children.Insert(Math.Min(index, lines.Children.Count), line);
                }
                else if (lines.Children.IndexOf(line) != index)
                {
                    lines.Children.Remove(line);
                    lines.Children.Insert(Math.Min(index, lines.Children.Count), line);
                }
                line.Show(head, job.Title, narrow);
                index++;
            }
        }
        footer.Text = TaskWords.Footer(job);
        // Revert changes sits only on a stopped task with kept changes ([hidden] always wins, r4 B1)
        RevertShown.IsVisible = job.State == MissionState.Stopped && job.ChangedFiles > 0 && !job.Reverted;
    }
}

/// <summary>The mark of a task: Armilla's small form at 20 px, out of the accessibility tree, with the action as its hover.</summary>
internal static class TaskMark
{
    public static ArmillaMark Make() => new() { MarkSize = TaskMetrics.MarkSize, Kind = ArmillaKind.Row, VerticalAlignment = VerticalAlignment.Center };

    public static void Show(ArmillaMark mark, MissionJob job)
    {
        mark.State = job.Mark;
        mark.Glyph = job.Glyph;
        mark.Activity = $"{job.Owner}: {(job.State == MissionState.Done ? job.Detail : job.Activity ?? job.Detail)}";
    }
}
