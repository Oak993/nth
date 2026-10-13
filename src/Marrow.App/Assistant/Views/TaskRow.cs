// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.6b "Layout, controls and access", "Motion"; 6 the Result list; the canvas's rowEl,
// updRow, fold and grow, work/design/assistant/index.html lines 2064-2096 and its .tk CSS, lines 518-534; ED-NEW-dextra-as8-4).
// No Swift twin.
// A compact task row: the mark (a 20 px slot) · the title and its one-line detail · the heads chevron, all one expander named
// "Show heads of ⟨task⟩" · the state label · two control slots. The ask and the heads panel open under it, indented to the title,
// with the row's own 6 px trailing padding so a head's label and buttons line up with the task's. A state change never changes
// the row's size: the label and the slots have fixed widths, the detail is drawn in a fixed line, and a title's two lines (below
// 520 px) belong to the title, not the state.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

public sealed class TaskRow : Decorator
{
    private readonly Border card = new() { BorderThickness = new Thickness(1) };
    private readonly Grid line = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
    private readonly TextBlock title = TaskLook.Text("", 12.5, SwiftFont.Weight.Semibold, TaskLook.Ink);
    private readonly TickText detail = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Path chevron = TaskLook.Chevron();
    private readonly Grid inside = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
    private readonly StackPanel texts = new() { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border hover = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Border facePad = new() { Padding = new Thickness(6, 5) };
    private readonly Border lineBox = new() { Padding = new Thickness(4, 4, TaskMetrics.TrailingPadding, 4) };
    private readonly IBrush line1, line2;
    private bool narrow;

    public TaskRow(IMissionJobs jobs, Action refresh)
    {
        Mark = TaskMark.Make();
        AutomationProperties.SetAccessibilityView(Mark, AccessibilityView.Raw);
        texts.Children.Add(title);
        texts.Children.Add(detail);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(chevron, 2);
        inside.Children.Add(Mark);
        inside.Children.Add(texts);
        inside.Children.Add(chevron);
        hover.RoundedRectangle(9, RoundedCornerStyle.Continuous);
        facePad.Child = inside;
        var face = new Panel { Children = { hover, facePad } };
        Expander = new DisclosureButton { Content = face, Padding = default, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Expander.KeyboardFocus(9);
        Expander.Click += (_, _) => Toggle();
        Expander.PointerEntered += (_, _) => hover.Opacity = 1;
        Expander.PointerExited += (_, _) => hover.Opacity = 0;
        Grid.SetColumn(Label, 1);
        Grid.SetColumn(Slots, 2);
        Label.VerticalAlignment = VerticalAlignment.Center;
        Slots.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(Expander);
        line.Children.Add(Label);
        line.Children.Add(Slots);
        lineBox.Child = line;
        var stack = new StackPanel { Orientation = Orientation.Vertical, Children = { lineBox, Ask, Heads } };
        card.Child = stack;
        card.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        Child = card;
        Surface = new TaskSurface(jobs, Slots, Ask, Heads, () => Expander, refresh);
        Surface.HoldChanged += PaintSlots;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsPointerOverProperty || e.Property == IsKeyboardFocusWithinProperty) PaintSlots();
        };
        card.Background = TaskLook.Card.Brush(card);
        line1 = TaskLook.Line.Brush(card);
        line2 = TaskLook.Line2.Brush(card);
        card.BorderBrush = line1;
        hover.Background = Chrome.Overlay(.04).Brush(hover);
        detail.Ink = TaskLook.Ink2.Brush(detail);
        Indent();
    }

    public Guid Id => Surface.Job?.Id ?? Guid.Empty;

    public ArmillaMark Mark { get; }

    public DisclosureButton Expander { get; }

    public StateLabel Label { get; } = new();

    public TaskSlots Slots { get; } = new();

    public TaskAskPanel Ask { get; } = new();

    public HeadsPanel Heads { get; } = new();

    public TaskSurface Surface { get; }

    public string TitleText => title.Text ?? "";

    public string DetailText => detail.Text;

    internal TextBlock Title => title;

    internal TickText Detail => detail;

    /// <summary>Below 520 px: the label becomes its glyph, titles take two lines and the gaps tighten (r4 B15).</summary>
    public bool Narrow
    {
        get => narrow;
        set
        {
            if (narrow == value) return;
            narrow = value;
            Label.Narrow = value;
            title.TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            title.MaxLines = value ? 2 : 0;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            line.ColumnSpacing = value ? 4 : 8;
            inside.ColumnSpacing = value ? 8 : 10;
            Indent();
            if (Surface.Job is { } job) Show(job);
        }
    }

    public void Show(MissionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        title.Text = job.Title;
        detail.Text = job.Detail;
        TaskMark.Show(Mark, job);
        Label.Show(job.State);
        Surface.Show(job, narrow);
        Expander.IsExpanded = Surface.Expanded;
        AutomationProperties.SetName(Expander, (job.Heads.Count > 0 ? "Show heads of " : "Show the agent of ") + job.Title);
        AutomationProperties.SetHelpText(Expander, job.Title + ". " + job.Detail);
        var needs = job.State == MissionState.NeedsYou;
        card.BorderBrush = needs ? line2 : line1;
        PaintSlots();
    }

    private void Toggle()
    {
        Surface.Expand(!Surface.Expanded);
        Expander.IsExpanded = Surface.Expanded;
        TaskLook.Turn(chevron, Surface.Expanded, TaskLook.Still(this));
        if (Surface.Job is { } job) Show(job);
    }

    private void Indent()
    {
        // the ask and the heads line up under the title: 40 px in (12 narrow), the row's own trailing padding (6 px, 4 narrow)
        var trailing = narrow ? TaskMetrics.NarrowTrailingPadding : TaskMetrics.TrailingPadding;
        lineBox.Padding = narrow ? new Thickness(2, 4, trailing, 4) : new Thickness(4, 4, trailing, 4);
        facePad.Padding = narrow ? new Thickness(4, 5) : new Thickness(6, 5);
        var pad = narrow ? new Thickness(12, 0, trailing, 10) : new Thickness(40, 0, trailing, 10);
        Ask.Margin = new Thickness(pad.Left, 0, pad.Right, 0);
        Heads.Margin = pad;
    }

    private void PaintSlots()
    {
        var coarse = TaskTouch.IsCoarse;
        Slots.Coarse = coarse;
        Slots.Opacity = coarse || IsPointerOver || IsKeyboardFocusWithin || Surface.Hold ? 1 : 0;
    }
}
