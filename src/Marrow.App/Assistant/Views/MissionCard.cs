// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 3.3 "Mission card", 12.6b "Layout, controls and access"; the canvas's mCard and updCard,
// work/design/assistant/index.html lines 2030-2062 and its .mc CSS, lines 239-325; ED-NEW-dextra-as8-4, -5). No Swift twin.
// A mission card on the Board: a tinted fill on the pane, never glass on glass. Its top line holds the mark (20 px), the title and
// owner, the 86 px state label and the two control slots, as a task row does. Under it: the progress track (drawn, scaled from the
// left), the activity line (which shimmers while the task works and its list's budget allows), the heads row (one chip per head;
// it is the disclosure for the heads panel), the inline ask, the heads panel, and the meta line (elapsed, agents, spend), whose
// values are drawn in fixed slots so a tick never re-lays out the card. Open, Redirect and Review sit at the bottom right on hover
// or keyboard focus, or as a row under the card with touch. A Done card drops its two empty slots. Paused and stopped cards dim.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>What a card's actions reach outside the board; a missing one hides its button.</summary>
public sealed record TaskActions(Action<MissionJob>? Open = null, Func<MissionJob, string, string?>? Redirect = null, Action<MissionJob>? Review = null,
    Action<MissionJob>? Lineage = null)
{
    public static TaskActions None { get; } = new();
}

/// <summary>The progress track: drawn, its fill scaled from the left (0.9 s), never laid out again.</summary>
public sealed class ProgressTrack : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ProgressTrack, double>(nameof(Value));

    static ProgressTrack() => AffectsRender<ProgressTrack>(ValueProperty);

    private readonly IBrush track;
    private readonly IBrush fill;

    public ProgressTrack()
    {
        Height = 3;
        track = Chrome.Overlay(.1).Brush(this);
        fill = Chrome.PrimaryText.Brush(this);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public void Show(double value, bool still)
    {
        value = Math.Clamp(value, 0, 1);
        Transitions = still ? null : [new DoubleTransition { Property = ValueProperty, Duration = TimeSpan.FromMilliseconds(900), Easing = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }];
        Value = value;
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        context.DrawRectangle(track, null, new Rect(0, 0, w, h), 2, 2);
        using (context.PushClip(new RoundedRect(new Rect(0, 0, w, h), 2)))
        {
            var x = FlowDirection == FlowDirection.RightToLeft ? w * (1 - Value) : 0;
            context.FillRectangle(fill, new Rect(x, 0, w * Math.Clamp(Value, 0, 1), h));
        }
    }
}

public sealed class MissionCard : Decorator
{
    private readonly Border card = new() { BorderThickness = new Thickness(1), Padding = new Thickness(12, 10) };
    private readonly TextBlock title = TaskLook.Text("", 13, SwiftFont.Weight.Semibold, TaskLook.Ink);
    private readonly TextBlock owner = TaskLook.Text("", 11, SwiftFont.Weight.Regular, TaskLook.Ink2);
    private readonly StackPanel chips = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
    private readonly Path chevron = TaskLook.Chevron();
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Border actionsBox = new() { Padding = new Thickness(2), BorderThickness = new Thickness(1) };
    private readonly RevealBox redirectBox = new();
    private readonly TextBox redirectText = new() { PlaceholderText = "A short instruction for the lead chat", AcceptsReturn = false, MinWidth = 120 };
    private readonly IBrush line1, line2, titleInk, titleDim;
    private readonly TaskActions doing;
    private Button? open, redirect, review, lineage;
    private bool calm;
    private bool coarse;

    public MissionCard(IMissionJobs jobs, Action refresh, TaskActions? actionsGiven = null)
    {
        doing = actionsGiven ?? TaskActions.None;
        Mark = TaskMark.Make();
        AutomationProperties.SetAccessibilityView(Mark, AccessibilityView.Raw);

        // the top line: mark · title and owner · label · slots
        var names = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Children = { title, owner } };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 9 };
        Grid.SetColumn(names, 1);
        Grid.SetColumn(Label, 2);
        Grid.SetColumn(Slots, 3);
        Label.VerticalAlignment = VerticalAlignment.Center;
        Slots.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(Mark);
        top.Children.Add(names);
        top.Children.Add(Label);
        top.Children.Add(Slots);

        Activity.FontSize = 12;
        Activity.Ink = TaskLook.Ink2.Brush(Activity);

        // the heads row is the disclosure of the heads panel
        var heads = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { chips, chevron } };
        HeadsButton = new DisclosureButton { Content = heads, Padding = new Thickness(2), Margin = new Thickness(-2), HorizontalAlignment = HorizontalAlignment.Left };
        HeadsButton.KeyboardFocus(10);
        HeadsButton.Click += (_, _) =>
        {
            Surface!.Expand(!Surface.Expanded);
            HeadsButton.IsExpanded = Surface.Expanded;
            TaskLook.Turn(chevron, Surface.Expanded, TaskLook.Still(this));
            if (Surface.Job is { } job) Show(job, lastNow);
        };

        // the meta line: values in fixed slots
        Elapsed.Width = 56;
        Agents.Width = 112;
        Spend.HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var t in new[] { Elapsed, Agents, Spend }) { t.FontSize = 11; t.Ink = TaskLook.Ink2.Brush(t); }
        var meta = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10 };
        Grid.SetColumn(Agents, 1);
        Grid.SetColumn(Spend, 2);
        meta.Children.Add(Elapsed);
        meta.Children.Add(Agents);
        meta.Children.Add(Spend);

        // Redirect: a short line to the lead chat, inline under the card
        var (send, sendShown) = TaskLook.Small("Send", SendRedirect, primary: true, name: "Send the redirect");
        var (cancel, cancelShown) = TaskLook.Small("Cancel", () => CloseRedirect(), name: "Cancel the redirect");
        _ = send; _ = cancel;
        SwiftFont.System(12).Apply(redirectText);
        AutomationProperties.SetName(redirectText, "Redirect: a short instruction for the lead chat");
        redirectText.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; SendRedirect(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CloseRedirect(); }
        };
        var redirectRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetColumn(sendShown, 1);
        Grid.SetColumn(cancelShown, 2);
        redirectRow.Children.Add(redirectText);
        redirectRow.Children.Add(sendShown);
        redirectRow.Children.Add(cancelShown);
        redirectBox.Child = redirectRow;
        AutomationProperties.SetName(redirectBox, "Redirect");

        BuildActions();
        actionsBox.RoundedRectangle(15, RoundedCornerStyle.Continuous);
        actionsBox.Child = actions;
        actionsBox.Background = Chrome.GlassTint.Brush(actionsBox);
        actionsBox.BorderBrush = TaskLook.Line.Brush(actionsBox);
        actionsBox.HorizontalAlignment = HorizontalAlignment.Right;
        actionsBox.VerticalAlignment = VerticalAlignment.Bottom;
        actionsBox.Margin = new Thickness(0, 0, -4, -4);
        actionsBox.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }];

        Body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 7, Children = { top, Track, Activity, HeadsButton, Ask, Heads, meta, redirectBox } };
        var layers = new Panel { Children = { Body, actionsBox } };
        card.Child = layers;
        card.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        card.Background = TaskLook.Card.Brush(card);
        line1 = TaskLook.Line.Brush(card);
        line2 = TaskLook.Line2.Brush(card);
        card.BorderBrush = line1;
        Child = card;
        titleInk = title.Foreground!;
        titleDim = TaskLook.Ink2.Brush(title);
        foreach (var faded in new Control[] { Mark, Track, chips })
            faded.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) }];
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Group);

        Surface = new TaskSurface(jobs, Slots, Ask, Heads, () => HeadsButton.IsVisible ? HeadsButton : open ?? (Control?)null, refresh);
        Surface.HoldChanged += PaintShown;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsPointerOverProperty || e.Property == IsKeyboardFocusWithinProperty) PaintShown();
        };
        ActualThemeVariantChanged += (_, _) => PaintRing();
        Coarse = TaskTouch.IsCoarse;
    }

    public Guid Id => Surface.Job?.Id ?? Guid.Empty;

    public ArmillaMark Mark { get; }

    public StateLabel Label { get; } = new();

    public TaskSlots Slots { get; } = new();

    public ProgressTrack Track { get; } = new();

    public TickText Activity { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    public TickText Elapsed { get; } = new();

    public TickText Agents { get; } = new();

    public TickText Spend { get; } = new();

    public DisclosureButton HeadsButton { get; }

    public TaskAskPanel Ask { get; } = new();

    public HeadsPanel Heads { get; } = new();

    public TaskSurface Surface { get; }

    internal StackPanel Body { get; }

    internal Border ActionsBox => actionsBox;

    public Button? OpenButton => open;

    public Button? RedirectButton => redirect;

    public Button? ReviewButton => review;

    public Button? LineageButton => lineage;

    public string TitleText => title.Text ?? "";

    public string OwnerText => owner.Text ?? "";

    internal TextBlock Title => title;

    private DateTimeOffset lastNow;

    /// <summary>Set by the motion budget when the Board goes still: the activity line stops shimmering.</summary>
    public bool Calm
    {
        get => calm;
        set { calm = value; Activity.Calm = value; }
    }

    /// <summary>Whether this card's shimmer is wanted now (a working card; what the budget counts).</summary>
    public bool WantsSheen => Surface.Job is { State: MissionState.Working };

    /// <summary>With touch the slots are always shown, 40 px apart by 8, and the actions sit in a row under the card.</summary>
    public bool Coarse
    {
        get => coarse;
        set
        {
            coarse = value;
            Slots.Coarse = value;
            actionsBox.HorizontalAlignment = HorizontalAlignment.Right;
            if (value)
            {
                ((Panel)card.Child!).Children.Remove(actionsBox);
                if (!Body.Children.Contains(actionsBox)) Body.Children.Add(actionsBox);
                actionsBox.Margin = default;
            }
            else
            {
                Body.Children.Remove(actionsBox);
                var layers = (Panel)card.Child!;
                if (!layers.Children.Contains(actionsBox)) layers.Children.Add(actionsBox);
                actionsBox.Margin = new Thickness(0, 0, -4, -4);
            }
            PaintShown();
        }
    }

    /// <summary>Below 520 px titles take up to two lines and the label becomes its glyph (r4 B15).</summary>
    public bool Narrow
    {
        get => Label.Narrow;
        set
        {
            Label.Narrow = value;
            title.TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            title.MaxLines = value ? 2 : 0;
            if (Surface.Job is { } job) Show(job, lastNow);
        }
    }

    public void Show(MissionJob job, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        lastNow = now;
        var still = TaskLook.Still(this);
        title.Text = job.Title;
        owner.Text = job.State == MissionState.Done && job.ReviewedBy is { Length: > 0 } by ? $"{job.Owner} · Reviewed by {by}" : job.Owner;
        AutomationProperties.SetName(this, job.Title);
        TaskMark.Show(Mark, job);
        Label.Show(job.State);
        Track.Show(job.Progress, still);
        Activity.Text = job.Detail;
        Activity.Sheen = job.State == MissionState.Working;
        Elapsed.Text = TaskWords.Elapsed((job.Ended ?? now) - job.Created);
        Agents.Text = TaskWords.AgentsWord(job);
        Spend.Text = TaskWords.SpendWord(job.Spend);
        // a Done card has nothing to control: its two empty slots fold away as it moves to Done today (the move hides it)
        Slots.IsVisible = job.State != MissionState.Done;
        Surface.Show(job, Label.Narrow);
        ShowChips(job);
        // dimmed (paused, stopped, stopping) without taking any text under 4.5:1: the mark, the track and the chips dim, and the
        // title moves to the 70 % ink (the canvas dims the whole card to .62, which put its small lines near 3:1)
        var dim = job.Dimmed ? .5 : 1;
        Mark.Opacity = dim;
        Track.Opacity = dim;
        chips.Opacity = dim;
        title.Foreground = job.Dimmed ? titleDim : titleInk;
        if (open is not null) open.IsEnabled = job.LeadThread is not null;
        if (redirect is not null) redirect.IsEnabled = job.LeadThread is not null && job.State is not (MissionState.Done or MissionState.Stopped or MissionState.Stopping);
        var needs = job.State == MissionState.NeedsYou;
        card.BorderBrush = needs ? line2 : line1;
        if (needs != Classes.Contains("needs")) { Classes.Set("needs", needs); PaintRing(); }
        PaintShown();
    }

    /// <summary>Only the meta line's values and the activity: what a 4 Hz tick changes (never a layout pass).</summary>
    public void Tick(MissionJob job, DateTimeOffset now)
    {
        lastNow = now;
        Elapsed.Text = TaskWords.Elapsed((job.Ended ?? now) - job.Created);
        Agents.Text = TaskWords.AgentsWord(job);
        Spend.Text = TaskWords.SpendWord(job.Spend);
        Activity.Text = job.Detail;
    }

    private void ShowChips(MissionJob job)
    {
        HeadsButton.IsVisible = job.Heads.Count > 0;
        if (job.Heads.Count == 0) return;
        AutomationProperties.SetName(HeadsButton, TaskWords.CardHeads(job.Heads.Count, job.Title));
        HeadsButton.IsExpanded = Surface.Expanded;
        while (chips.Children.Count > job.Heads.Count) chips.Children.RemoveAt(chips.Children.Count - 1);
        for (var i = 0; i < job.Heads.Count; i++)
        {
            var head = job.Heads[i];
            if (i >= chips.Children.Count) chips.Children.Add(Chip());
            var chip = (Border)chips.Children[i];
            var row = (StackPanel)chip.Child!;
            ((StateGlyph)row.Children[0]).Key = StateGlyph.KeyOf(head.State);
            ((TextBlock)row.Children[1]).Text = head.Name;
        }
    }

    private static Border Chip()
    {
        var name = TaskLook.Text("", 11, SwiftFont.Weight.Regular, TaskLook.Ink2);
        name.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Border
        {
            Height = 20,
            Padding = new Thickness(6, 0, 7, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new StateGlyph { VerticalAlignment = VerticalAlignment.Center }, name } },
        };
        chip.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        chip.Background = Chrome.Overlay(.06).Brush(chip);
        return chip;
    }

    private void BuildActions()
    {
        if (doing.Open is { } go)
        {
            open = Icon("M14 5h5v5 M19 5l-8 8 M17 14v4a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1h4", "Open the lead chat", () => { if (Surface.Job is { } j) go(j); });
            actions.Children.Add(open);
        }
        if (doing.Redirect is not null)
        {
            redirect = Icon("M4 17c0-5 3.5-8 9-8h6 M15 5l4 4-4 4", "Redirect", OpenRedirect);
            actions.Children.Add(redirect);
        }
        if (doing.Review is { } see)
        {
            review = Icon("M2.5 12s3.5-6.5 9.5-6.5S21.5 12 21.5 12s-3.5 6.5-9.5 6.5S2.5 12 2.5 12z M12 9.2a2.8 2.8 0 1 0 0 5.6a2.8 2.8 0 1 0 0-5.6z", "Review and merge",
                () => { if (Surface.Job is { } j) see(j); });
            ToolTip.SetTip(review, "Review the mission's branch and merge");
            actions.Children.Add(review);
        }
        if (doing.Lineage is { } trace)
        {
            // AS-21's seam: the activity log, opened on this mission (every step it took and who allowed it)
            lineage = Icon("M3 12a9 9 0 1 0 3-6.7 M3 4v5h5 M12 7v5l3 2", "Show its activity", () => { if (Surface.Job is { } j) trace(j); });
            ToolTip.SetTip(lineage, "Every step it took, and who allowed it");
            actions.Children.Add(lineage);
        }
        actionsBox.IsVisible = actions.Children.Count > 0;
    }

    private static Button Icon(string data, string name, Action action)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.Stroke = Chrome.SecondaryText.Brush(path);
        var face = new Border { Width = 26, Height = 26, Child = path, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        var hot = Chrome.Overlay(.07).Brush(face);
        var button = PlainControls.Button(face, action);
        button.KeyboardFocus(13);
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = Brushes.Transparent;
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    private void OpenRedirect()
    {
        redirectBox.Open(true, TaskLook.Still(this));
        redirectText.Focus(NavigationMethod.Tab);
        PaintShown();
    }

    private void CloseRedirect(bool focusBack = true)
    {
        redirectText.Text = "";
        redirectBox.Open(false, TaskLook.Still(this));
        if (focusBack) redirect?.Focus(NavigationMethod.Tab);
        PaintShown();
    }

    private void SendRedirect()
    {
        var text = redirectText.Text?.Trim() ?? "";
        if (text.Length == 0 || Surface.Job is not { } job || doing.Redirect is not { } send) return;
        var note = send(job, text);
        if (note is not null) Said?.Invoke(note);
        CloseRedirect();
    }

    /// <summary>A refusal or note from the redirect, for the polite live region.</summary>
    public event Action<string>? Said;

    private void PaintShown()
    {
        var shown = coarse || IsPointerOver || IsKeyboardFocusWithin || Surface.Hold;
        Slots.Opacity = shown ? 1 : 0;
        actionsBox.Opacity = coarse || IsPointerOver || IsKeyboardFocusWithin || redirectBox.IsOpen ? 1 : 0;
        actionsBox.IsHitTestVisible = actionsBox.Opacity > 0;
    }

    private void PaintRing()
    {
        // the card that needs the owner wears a 3 px ring outside its hairline
        card.BoxShadow = Classes.Contains("needs")
            ? new BoxShadows(new BoxShadow { Spread = 3, Color = Chrome.Overlay(.09).Resolve(ActualThemeVariant) })
            : default;
    }
}
