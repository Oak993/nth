// PORT-ONLY: Dextra AS-17 (DESIGN.md rev 5, 9.4 "Each node shows: its mark (Armilla at 64 px with a node per Astur for Dextra; the node shape at 30 px for an Astur), the name, the current work
// line, a status chip and a small usage meter. Mission chips hang under each node, one per active mission, marked with a Hydra glyph when a Hydra team is on it"; the canvas's onode and
// .onode CSS, work/design/assistant/index.html lines 426-441 and 2866-2870; ED-NEW-dextra-as17-4). No Swift twin.
// One node of the org chart. A tinted card (never glass on glass), with the mark, the name, the work line under it (two lines, always), the status chip, the usage meter and the mission chips
// (one line, always), so a change of state never changes a node's size. The whole card is one button that selects the node (the detail panel shows it); the chips are their own buttons laid
// over it. In the narrow list the mark moves to the left and the words stand beside it. The card lifts 2 px under the pointer (a transform; none with Reduce motion), and its selected ring
// comes in and goes out on a short ease. The mark is a picture out of the accessibility tree; the node is read by its name, its state in a word and its work line.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>The card's button: selects the node, and is read as a selectable list item that says whether it is selected.</summary>
public sealed class OrgNodeButton : Button
{
    public static readonly StyledProperty<bool> IsSelectedProperty = AvaloniaProperty.Register<OrgNodeButton, bool>(nameof(IsSelected));

    protected override Type StyleKeyOverride => typeof(Button);

    public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    internal Action? Choose { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSelectedProperty && ControlAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, change.GetOldValue<bool>(), change.GetNewValue<bool>());
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(OrgNodeButton owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        public bool IsSelected => owner.IsSelected;

        public ISelectionProvider? SelectionContainer => null;

        public void Select() => owner.Choose?.Invoke();

        public void AddToSelection() => owner.Choose?.Invoke();

        public void RemoveFromSelection()
        {
        }

        public void Invoke() => owner.Choose?.Invoke();
    }
}

public sealed class OrgNode : Decorator
{
    public const double Radius = 16;
    public const double DextraMarkSize = 64;
    public const double AsturMarkSize = 30;

    private static readonly TimeSpan Ring = TimeSpan.FromMilliseconds(220);

    private readonly bool dextra;
    private readonly Panel face = new();
    private readonly Border card = new() { BorderThickness = new Thickness(1) };
    private readonly Border ring = new() { BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Opacity = 0 };
    private readonly Grid content = new() { Margin = new Thickness(12), RowSpacing = 6 };
    private readonly Panel markHost = new();
    private readonly TextBlock name = TaskLook.Text("", 13, SwiftFont.Weight.Semibold, TaskLook.Ink);
    private readonly TextBlock work = TaskLook.Text("", 11.5, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
    private readonly Border chip = new() { Height = 20, Padding = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock chipText = TaskLook.Text("", 10.5, SwiftFont.Weight.Medium, TaskLook.Ink);
    private readonly ProgressTrack meter = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TeamChipRow chips = new();
    private readonly IBrush chipFill, chipWaitingFill, chipWaitingInk, chipInk;
    private TeamMember? member;
    private ArmillaState shownMark;
    private ArmillaNode[] shownNodes = [];
    private bool compact;
    private bool calm;
    private bool still;
    private Action<Guid>? openChat;

    public OrgNode(bool dextra, Action<OrgNode>? choose = null)
    {
        this.dextra = dextra;
        Mark = dextra
            ? new ArmillaMark { MarkSize = DextraMarkSize, Kind = ArmillaKind.Live, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new AsturMark { MarkSize = AsturMarkSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(Mark, AccessibilityView.Raw);
        markHost.Children.Add(Mark);

        card.RoundedRectangle(Radius, RoundedCornerStyle.Continuous);
        card.Background = TaskLook.Card.Brush(card);
        card.BorderBrush = TaskLook.Line2.Brush(card);
        ring.RoundedRectangle(Radius, RoundedCornerStyle.Continuous);
        ring.BorderBrush = TaskLook.Ink.Brush(ring);

        Face = new OrgNodeButton { Content = new Panel { Children = { card, ring } }, Padding = default, Background = Brushes.Transparent, BorderThickness = default, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Face.KeyboardFocus(Radius);
        Face.Choose = () => choose?.Invoke(this);
        Face.Click += (_, _) => choose?.Invoke(this);
        // The hit surface stays put while only the card lifts: the bottom strip must not exit its own hover.
        var hitSurface = new Border { Background = Brushes.Transparent, Child = face };
        hitSurface.PointerEntered += (_, _) => Lift(true);
        hitSurface.PointerExited += (_, _) => Lift(false);

        name.TextAlignment = TextAlignment.Center;
        work.TextAlignment = TextAlignment.Center;
        work.MaxLines = 2;
        work.TextTrimming = TextTrimming.CharacterEllipsis;
        work.MinHeight = 31;
        work.LineHeight = 15.5;
        chip.Capsule(RoundedCornerStyle.Continuous);
        chip.Child = chipText;
        chipText.VerticalAlignment = VerticalAlignment.Center;
        chipFill = Chrome.Overlay(.07).Brush(chip);
        chipWaitingFill = TaskLook.Ink.Brush(chip);
        chipWaitingInk = TaskLook.Inverse.Brush(chipText);
        chipInk = TaskLook.Ink.Brush(chipText);
        chip.Background = chipFill;
        meter.Height = 3;
        chips.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var quiet in new Control[] { markHost, name, work, chip, meter }) quiet.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(chip, AccessibilityView.Raw);

        for (var i = 0; i < 6; i++) content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Place(false);
        content.Children.Add(markHost);
        content.Children.Add(name);
        content.Children.Add(work);
        content.Children.Add(chip);
        content.Children.Add(meter);
        content.Children.Add(chips);

        face.Children.Add(Face);
        face.Children.Add(content);
        face.RenderTransform = new TranslateTransform();
        Child = hitSurface;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(content, AccessibilityView.Raw);
    }

    public bool IsDextra => dextra;

    /// <summary>The node's id: the assistant's.</summary>
    public string Id { get; private set; } = "";

    public OrgNodeButton Face { get; }

    /// <summary>The mark: Armilla at 64 px for Dextra, the node shape at 30 px for an Astur.</summary>
    public Control Mark { get; }

    public ArmillaMark? DextraMark => Mark as ArmillaMark;

    public AsturMark? AsturMark => Mark as AsturMark;

    public TextBlock NameText => name;

    public TextBlock WorkText => work;

    public Border StatusChip => chip;

    public TextBlock StatusText => chipText;

    public ProgressTrack Meter => meter;

    public IReadOnlyList<Button> ChipButtons => chips.Shown;

    /// <summary>The "+N" chip's words when some chips did not fit, else null.</summary>
    public string? MoreText => chips.MoreText;

    public TeamChipRow ChipRow => chips;

    public TeamMember? Member => member;

    public bool IsSelected
    {
        get => Face.IsSelected;
        set
        {
            if (Face.IsSelected == value) return;
            Face.IsSelected = value;
            ring.Transitions = still || TaskLook.Still(this) ? null : [new DoubleTransition { Property = OpacityProperty, Duration = Ring, Easing = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }];
            ring.Opacity = value ? 1 : 0;
        }
    }

    /// <summary>Whether the words stand beside the mark (the narrow list) rather than under it.</summary>
    public bool Compact
    {
        get => compact;
        set
        {
            if (compact == value) return;
            compact = value;
            Place(value);
            InvalidateMeasure();
        }
    }

    /// <summary>The motion budget stilled the Board's list: Dextra's mark shows its still pose (an Astur's never moves).</summary>
    public bool Calm
    {
        get => calm;
        set
        {
            calm = value;
            if (Mark is ArmillaMark a) a.Calm = value;
            else if (Mark is AsturMark s) s.Calm = value;
        }
    }

    /// <summary>What a chip press opens: that task's lead chat ("watching its computer").</summary>
    public Action<Guid>? OpenChat
    {
        get => openChat;
        set => openChat = value;
    }

    /// <summary>Shows this assistant as the snapshot has it. Nothing is rebuilt when nothing it shows changed.</summary>
    public void Show(TeamMember next, TeamSnapshot all, bool still)
    {
        this.still = still;
        var nodes = dextra ? [.. all.Asturs.Select(a => new ArmillaNode(AsturMarkStates.ShapeOf(a.Shape), AsturPresences.Busy(a.Presence)))] : Array.Empty<ArmillaNode>();
        if (member == next && (!dextra || (shownMark == all.DextraMark && shownNodes.SequenceEqual(nodes)))) return;
        var first = member is null;
        var before = member;
        member = next;
        shownMark = all.DextraMark;
        shownNodes = nodes;
        Id = next.Id;
        if (before is null || before.Name != next.Name) name.Text = next.Name;
        if (before is null || before.Work != next.Work) work.Text = next.Work;
        var hover = $"{next.Name}: {next.Activity ?? next.Work}";
        if (Mark is ArmillaMark live)
        {
            live.State = all.DextraMark;
            live.Nodes = nodes;
            live.Activity = hover;
        }
        else if (Mark is AsturMark astur)
        {
            astur.Shape = AsturMarkStates.ShapeOf(next.Shape);
            astur.State = next.MarkState;
            astur.Activity = hover;
        }
        ShowStatus(next);
        meter.IsVisible = next.Meter is not null;
        if (next.Meter is { } fraction) meter.Show(fraction, still || first);
        ToolTip.SetTip(meter, next.MeterWords.Length == 0 ? null : next.MeterWords);
        if (before is null || before.Missions != next.Missions) ShowChips(next);
        AutomationProperties.SetName(Face, next.Reading);
        AutomationProperties.SetHelpText(Face, next.MeterWords);
    }

    private void ShowStatus(TeamMember next)
    {
        chipText.Text = AsturPresences.Label(next.Presence);
        var waiting = next.Presence == AsturPresence.Waiting;
        chip.Background = waiting ? chipWaitingFill : chipFill;
        chipText.Foreground = waiting ? chipWaitingInk : chipInk;
    }

    private void ShowChips(TeamMember next) => chips.Set([.. next.Missions.Select(m => TeamChips.Make(m, openChat))]);

    // the grid, for the chart (words under the mark, centred) and for the list (the mark at the left, the words beside it)
    private void Place(bool list)
    {
        var words = new Control[] { name, work, chip, meter, chips };
        content.ColumnDefinitions.Clear();
        if (list)
        {
            content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            content.ColumnSpacing = 12;
            Grid.SetColumn(markHost, 0);
            Grid.SetRow(markHost, 0);
            Grid.SetRowSpan(markHost, 6);
            markHost.VerticalAlignment = VerticalAlignment.Center;
            for (var i = 0; i < words.Length; i++)
            {
                Grid.SetColumn(words[i], 1);
                Grid.SetRow(words[i], i + 1);
                words[i].HorizontalAlignment = HorizontalAlignment.Left;
            }
            name.TextAlignment = TextAlignment.Left;
            work.TextAlignment = TextAlignment.Left;
            work.MinHeight = 0;
            chips.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            content.ColumnSpacing = 0;
            Grid.SetColumn(markHost, 0);
            Grid.SetRow(markHost, 0);
            Grid.SetRowSpan(markHost, 1);
            markHost.VerticalAlignment = VerticalAlignment.Center;
            for (var i = 0; i < words.Length; i++)
            {
                Grid.SetColumn(words[i], 0);
                Grid.SetRow(words[i], i + 1);
                words[i].HorizontalAlignment = HorizontalAlignment.Center;
            }
            name.TextAlignment = TextAlignment.Center;
            work.TextAlignment = TextAlignment.Center;
            work.MinHeight = 31;
            chips.HorizontalAlignment = HorizontalAlignment.Center;
        }
        meter.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    // the card lifts 2 px under the pointer: a transform, on a short ease; not with Reduce motion
    private void Lift(bool up)
    {
        if (face.RenderTransform is not TranslateTransform lift) return;
        lift.Transitions = still || TaskLook.Still(this) ? null : [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }];
        lift.Y = up ? -2 : 0;
    }

    /// <summary>The card's lift now, in px (0 at rest, -2 under the pointer); tests read it.</summary>
    public double LiftNow => face.RenderTransform is TranslateTransform t ? t.Y : 0;

    /// <summary>Whether the card's lift has a transition on it (tests): not with Reduce motion.</summary>
    public bool LiftEases => face.RenderTransform is TranslateTransform { Transitions.Count: > 0 };

    /// <summary>The ring's strength now (0 not selected, 1 selected, between while it eases).</summary>
    public double RingNow => ring.Opacity;

    /// <summary>Whether the card has a transition on its ring (tests).</summary>
    public bool RingEases => ring.Transitions is { Count: > 0 };

    public void Focus() => Face.Focus(NavigationMethod.Directional);
}
