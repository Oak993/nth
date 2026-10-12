// PORT-ONLY: web surfaces W16a (DESIGN.md 7.2 components chips (filter), followups, tabs, steps ("1 of 8", step-through),
// segmented; 7.5 "pill chips", accent only for the current value and focus; canvas index.html .fchips (28 high, radius 14,
// 12 px, faint rim) and .seg (track padding 3, radius 12, fill and inset rim; a 30 high segment, radius 9, the chosen one
// a lighter pill); ED-NEW-web-w16a-1). No Swift counterpart. Choices drawn by their owner: chips and follow-ups arrive
// one by one at the end of their flow, a segment track grows to the right as segments arrive and its chosen pill glides
// to a new choice, and a tab's or step's panel swaps in place while the height springs to the new content.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.NthUi;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi;

/// <summary>A segment track: toggles side by side on a tinted track, with the chosen pill drawn under the chosen one
/// and gliding when the choice moves. Left and Right (and Home and End) move the choice.</summary>
public sealed class NthUiSegments : Control, INthUiAnimated
{
    private readonly List<Control> items = [];
    private const double Pad = 3;
    private const double PillRadius = 9;
    private readonly NthUiMotion motion;
    private NthUiTween x = NthUiTween.At(0);
    private NthUiTween y = NthUiTween.At(0);
    private NthUiTween w = NthUiTween.At(0);
    private NthUiTween h = NthUiTween.At(0);
    private int selected = -1;
    private bool placed;
    private bool glide;

    internal NthUiSegments(NthUiMotion motion)
    {
        this.motion = motion;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>The segments, in order.</summary>
    public IReadOnlyList<Control> Items => items;

    /// <summary>Adds a segment at the end of the track.</summary>
    public void Add(Control segment)
    {
        items.Add(segment);
        LogicalChildren.Add(segment);
        VisualChildren.Add(segment);
        InvalidateMeasure();
    }

    /// <summary>The segment the user moved to with a key (its index).</summary>
    public event Action<int>? Picked;

    /// <summary>The chosen segment's index (-1 when none).</summary>
    public int Selected
    {
        get => selected;
        set
        {
            if (value == selected) return;
            glide = selected >= 0 && placed;
            selected = value;
            InvalidateArrange();
        }
    }

    /// <summary>The chosen pill's rectangle as drawn now.</summary>
    public Rect Pill
    {
        get
        {
            var now = NthUiMotion.Now;
            return new Rect(x.Value(now), y.Value(now), Math.Max(0, w.Value(now)), Math.Max(0, h.Value(now)));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = Math.Max(0, availableSize.Width - 2 * Pad);
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        var first = true;
        foreach (var child in items)
        {
            child.Measure(Size.Infinity);
            if (!child.IsVisible) continue;
            var size = child.DesiredSize;
            if (!first && lineWidth + size.Width > limit + .01)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight;
                lineWidth = 0;
                lineHeight = 0;
            }
            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            first = false;
        }
        width = Math.Max(width, lineWidth);
        height += lineHeight;
        return first ? default : new Size(width + 2 * Pad, height + 2 * Pad);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var limit = Math.Max(0, finalSize.Width - 2 * Pad);
        double left = Pad, top = Pad, lineHeight = 0;
        var first = true;
        Rect? chosen = null;
        for (var i = 0; i < items.Count; i++)
        {
            var child = items[i];
            if (!child.IsVisible) continue;
            var size = child.DesiredSize;
            if (!first && left - Pad + size.Width > limit + .01)
            {
                left = Pad;
                top += lineHeight;
                lineHeight = 0;
            }
            var rect = new Rect(left, top, size.Width, size.Height);
            child.Arrange(rect);
            if (i == selected) chosen = rect;
            left += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            first = false;
        }
        if (chosen is { } target) Place(target);
        return finalSize;
    }

    private void Place(Rect target)
    {
        var now = NthUiMotion.Now;
        if (Math.Abs(x.Target - target.X) < .01 && Math.Abs(y.Target - target.Y) < .01 && Math.Abs(w.Target - target.Width) < .01 && Math.Abs(h.Target - target.Height) < .01)
        {
            placed = true;
            return;
        }
        if (glide && motion.CanAnimate && !NthUiCalm.For(this))
        {
            const double seconds = .26;
            x = x.Retarget(target.X, now, seconds, NthEase.Out);
            y = y.Retarget(target.Y, now, seconds, NthEase.Out);
            w = w.Retarget(target.Width, now, seconds, NthEase.Out);
            h = h.Retarget(target.Height, now, seconds, NthEase.Out);
            motion.Start(this);
        }
        else
        {
            x = NthUiTween.At(target.X);
            y = NthUiTween.At(target.Y);
            w = NthUiTween.At(target.Width);
            h = NthUiTween.At(target.Height);
        }
        glide = false;
        placed = true;
        InvalidateVisual();
    }

    bool INthUiAnimated.Step(double now)
    {
        InvalidateVisual();
        return !(x.Settled(now) && y.Settled(now) && w.Settled(now) && h.Settled(now));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || items.Count == 0) return;
        var visible = items.Select((c, i) => (c, i)).Where(p => p.c.IsVisible).Select(p => p.i).ToList();
        if (visible.Count == 0) return;
        var at = visible.IndexOf(selected);
        var next = e.Key switch
        {
            Key.Left or Key.Up => at <= 0 ? visible[^1] : visible[at - 1],
            Key.Right or Key.Down => at < 0 || at == visible.Count - 1 ? visible[0] : visible[at + 1],
            Key.Home => visible[0],
            Key.End => visible[^1],
            _ => -1,
        };
        if (next < 0) return;
        e.Handled = true;
        Picked?.Invoke(next);
        items[next].Focus(NavigationMethod.Directional);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 1 || Bounds.Height < 1) return;
        var theme = ActualThemeVariant;
        var track = new RoundedRect(new Rect(Bounds.Size), NthUiTokens.ControlRadius);
        context.DrawRectangle(new ImmutableSolidColorBrush(NthUiTokens.ControlFill.Resolve(theme)), null, track);
        context.DrawRectangle(null, new ImmutablePen(new ImmutableSolidColorBrush(NthUiTokens.ControlRim.Resolve(theme)), 1), new RoundedRect(new Rect(Bounds.Size).Deflate(.5), NthUiTokens.ControlRadius - .5));
        if (selected < 0 || !placed) return;
        var pill = Pill;
        if (pill.Width <= 0) return;
        var shape = new RoundedRect(pill, PillRadius);
        if (theme == Avalonia.Styling.ThemeVariant.Light)
            context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(20, 0, 0, 0)), null, new RoundedRect(pill.Translate(new Vector(0, .5)), PillRadius));
        context.DrawRectangle(new ImmutableSolidColorBrush(NthUiTokens.Chosen.Resolve(theme)), new ImmutablePen(new ImmutableSolidColorBrush(NthUiTokens.ChosenEdge.Resolve(theme)), .75), shape);
    }
}

/// <summary>Builds one segment (a tab or a choice): a toggle with the segment face, radius 9.</summary>
internal static class NthUiSegment
{
    public static ToggleButton Create(string label, string? icon, Action pressed, AutomationControlType role)
    {
        var toggle = NthUiPress.Plain(new ToggleButton());
        var face = NthUiPress.Face(toggle, NthUiFaceStyle.Segment, label, icon, 30, out _);
        face.CornerRadius = new CornerRadius(9);
        toggle.Content = face;
        toggle.Click += (_, _) => pressed();
        AutomationProperties.SetName(toggle, label);
        AutomationProperties.SetControlTypeOverride(toggle, role);
        return toggle;
    }

    public static void SetLabel(ToggleButton toggle, string label)
    {
        if (toggle.Content is NthUiFace { Child: StackPanel row } && row.Children.OfType<TextBlock>().FirstOrDefault() is { } text && text.Text != label)
        {
            text.Text = label;
            AutomationProperties.SetName(toggle, label);
        }
    }
}

/// <summary>`chips`: filter chips that toggle values of the chips' state (one at a time with `single`).</summary>
public sealed class NthUiChipsView : NthUiNodeView
{
    private readonly StackPanel column = new() { Spacing = NthUiTokens.LabelGap + 2 };
    private readonly NthUiFlow flow = new() { Gap = 6, LineGap = 6 };
    private readonly List<(NthUiNode Node, ToggleButton Toggle, TextBlock Text, NthUiSlot Slot)> chips = [];
    private readonly TextBlock? title;

    public NthUiChipsView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var caption = Runtime.Text(node);
        if (caption.Length > 0)
        {
            title = NthUiText.Block(caption, NthUiTokens.Caption, NthUiTokens.Text2);
            column.Children.Add(title);
        }
        column.Children.Add(flow);
        AutomationProperties.SetControlTypeOverride(column, AutomationControlType.Group);
        AutomationProperties.SetName(column, caption);
    }

    public override Control Control => column;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "chip" || child.Degraded) return null;
        Context.Register(child, this);
        var toggle = NthUiPress.Toggle(Runtime.Text(child), NthUiFaceStyle.Filter, NthUiTokens.ChipHeight, () => Pick(child), out _, out var text);
        toggle.IsChecked = Runtime.IsSelected(child);
        var slot = new NthUiSlot(Context.Motion) { Key = child.Key, Reveal = NthUiReveal.Horizontal, Child = toggle };
        chips.Add((child, toggle, text, slot));
        flow.Children.Add(slot);
        slot.Arrive(Runtime.IsVisible(child), revealSize: true);
        return null;
    }

    private void Pick(NthUiNode chip)
    {
        Context.Run(chip);
        Refresh();
    }

    public override void Refresh()
    {
        if (title is not null) title.Text = Runtime.Text(Node);
        foreach (var (node, toggle, text, slot) in chips)
        {
            toggle.IsChecked = Runtime.IsSelected(node);
            var label = Runtime.Text(node);
            if (text.Text != label)
            {
                text.Text = label;
                AutomationProperties.SetName(toggle, label);
            }
            slot.SetShown(Runtime.IsVisible(node));
        }
    }
}

/// <summary>`followups`: suggested next questions; pressing one sends it as the user's turn.</summary>
public sealed class NthUiFollowupsView : NthUiNodeView
{
    private readonly NthUiFlow flow = new() { Gap = 6, LineGap = 6 };
    private readonly List<(NthUiNode Node, Button Button, TextBlock Text, NthUiSlot Slot)> chips = [];

    public NthUiFollowupsView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        AutomationProperties.SetControlTypeOverride(flow, AutomationControlType.Group);
        AutomationProperties.SetName(flow, NthUiStrings.FollowUps);
    }

    public override Control Control => flow;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "chip" || child.Degraded) return null;
        Context.Register(child, this);
        var button = NthUiPress.Plain(new Button());
        button.Content = NthUiPress.Face(button, NthUiFaceStyle.Chip, Runtime.Text(child), null, NthUiTokens.ChipHeight, out var text);
        button.Click += (_, _) => Context.Run(child);
        AutomationProperties.SetName(button, text.Text ?? "");
        var slot = new NthUiSlot(Context.Motion) { Key = child.Key, Reveal = NthUiReveal.Horizontal, Child = button };
        chips.Add((child, button, text, slot));
        flow.Children.Add(slot);
        slot.Arrive(Runtime.IsVisible(child), revealSize: true);
        return null;
    }

    public override void Refresh()
    {
        foreach (var (node, button, text, slot) in chips)
        {
            var label = Runtime.Text(node);
            if (text.Text != label)
            {
                text.Text = label;
                AutomationProperties.SetName(button, label);
            }
            slot.SetShown(Runtime.IsVisible(node));
        }
    }
}

/// <summary>`tabs`: a segment track of tab labels over the chosen tab's panel.</summary>
public sealed class NthUiTabsView : NthUiNodeView
{
    private readonly NthUiColumn column = new() { Gap = NthUiTokens.CardGap };
    private readonly NthUiSegments track;
    private readonly NthUiSlot trackSlot;
    private readonly NthUiSlot host;
    private readonly List<(NthUiNode Node, ToggleButton Toggle, NthUiPanelView Panel)> tabs = [];

    public NthUiTabsView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        track = new NthUiSegments(context.Motion);
        track.Picked += i => Pick(tabs[i].Node);
        AutomationProperties.SetControlTypeOverride(track, AutomationControlType.Tab);
        host = new NthUiSlot(context.Motion) { Key = node.Key + "/panel", Mode = NthUiSlotMode.Exact };
        AutomationProperties.SetControlTypeOverride(host, AutomationControlType.Pane);
        // The track opens with its first tab, so an empty tabs line draws nothing and the first label fades in.
        trackSlot = new NthUiSlot(context.Motion) { Key = node.Key + "/tabs", Child = track };
        column.Children.Add(trackSlot);
        column.Children.Add(host);
        trackSlot.Arrive(false, revealSize: true);
    }

    public override Control Control => column;

    public override NthUiSlotMode SlotMode => NthUiSlotMode.Exact;

    /// <summary>The segment track (for tests and the keyboard).</summary>
    public NthUiSegments Track => track;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "tab" || child.Degraded) return null;
        var panel = new NthUiPanelView(Context, child, this);
        var toggle = NthUiSegment.Create(Runtime.Text(child), child.Arg("icon")?.Literal.AsText, () => Pick(child), AutomationControlType.TabItem);
        tabs.Add((child, toggle, panel));
        track.Add(toggle);
        trackSlot.SetShown(true);
        Sync(animate: false);
        return panel;
    }

    private void Pick(NthUiNode tab)
    {
        Context.Run(tab);
        Sync(animate: true);
    }

    public override void Refresh() => Sync(animate: true);

    private void Sync(bool animate)
    {
        var chosen = Runtime.SelectedTab(Node);
        for (var i = 0; i < tabs.Count; i++)
        {
            var (node, toggle, panel) = tabs[i];
            var on = ReferenceEquals(node, chosen);
            toggle.IsChecked = on;
            toggle.IsVisible = Runtime.IsVisible(node);
            NthUiSegment.SetLabel(toggle, Runtime.Text(node));
            AutomationProperties.SetPositionInSet(toggle, i + 1);
            AutomationProperties.SetSizeOfSet(toggle, tabs.Count);
            if (!on) continue;
            track.Selected = i;
            if (ReferenceEquals(host.Child, panel.Control)) continue;
            if (animate && host.Child is not null) host.Swap(panel.Control);
            else host.Child = panel.Control;
            AutomationProperties.SetName(host, Runtime.Text(node));
        }
    }
}

/// <summary>`steps`: one step at a time with its place ("1 of 8") and previous and next.</summary>
public sealed class NthUiStepsView : NthUiNodeView
{
    private readonly NthUiColumn column = new() { Gap = 10 };
    private readonly TextBlock title;
    private readonly TextBlock place;
    private readonly TextBlock stepTitle;
    private readonly Button previous;
    private readonly Button next;
    private readonly NthUiSlot host;
    private readonly List<(NthUiNode Node, NthUiPanelView Panel)> steps = [];

    public NthUiStepsView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        title = NthUiText.Block(Runtime.Text(node), NthUiTokens.BodyStrong, NthUiTokens.Text1);
        title.VerticalAlignment = VerticalAlignment.Center;
        place = NthUiText.Block("", NthUiTokens.Small.MonospacedDigit(), NthUiTokens.Text2, wrap: false);
        place.VerticalAlignment = VerticalAlignment.Center;
        place.Margin = new Thickness(8, 0, 6, 0);
        previous = Arrow("chevron-left", NthUiStrings.PreviousStep, -1);
        next = Arrow("chevron-right", NthUiStrings.NextStep, 1);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        head.Children.Add(title);
        Grid.SetColumn(place, 1);
        head.Children.Add(place);
        Grid.SetColumn(previous, 2);
        head.Children.Add(previous);
        Grid.SetColumn(next, 3);
        head.Children.Add(next);
        stepTitle = NthUiText.Block("", NthUiTokens.Caption, NthUiTokens.Text2);
        AutomationProperties.SetLiveSetting(stepTitle, AutomationLiveSetting.Polite);
        host = new NthUiSlot(context.Motion) { Key = node.Key + "/panel", Mode = NthUiSlotMode.Exact };
        column.Children.Add(head);
        column.Children.Add(stepTitle);
        column.Children.Add(host);
        AutomationProperties.SetControlTypeOverride(column, AutomationControlType.Group);
        AutomationProperties.SetName(column, Runtime.Text(node));
        Sync(animate: false);
    }

    private Button Arrow(string icon, string name, int direction)
    {
        var button = NthUiPress.Plain(new Button());
        var glyph = NthUiText.Icon(icon, 14);
        var face = new NthUiFace(button, NthUiFaceStyle.Ghost, 10)
        {
            Width = 28,
            Height = 28,
            Child = glyph is null ? NthUiText.Block(direction < 0 ? "‹" : "›", NthUiTokens.BodyStrong, null, wrap: false) : glyph,
        };
        if (face.Child is Control c)
        {
            c.HorizontalAlignment = HorizontalAlignment.Center;
            c.VerticalAlignment = VerticalAlignment.Center;
        }
        button.Content = face;
        button.Click += (_, _) => Move(direction);
        AutomationProperties.SetName(button, name);
        return button;
    }

    public override Control Control => column;

    public override NthUiSlotMode SlotMode => NthUiSlotMode.Exact;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Kind != "step" || child.Degraded) return null;
        var panel = new NthUiPanelView(Context, child, this);
        steps.Add((child, panel));
        Sync(animate: false);
        return panel;
    }

    private void Move(int direction)
    {
        Context.Changed(Runtime.MoveStep(Node, direction));
        Sync(animate: true);
    }

    public override void Refresh() => Sync(animate: true);

    private void Sync(bool animate)
    {
        title.Text = Runtime.Text(Node);
        title.IsVisible = title.Text.Length > 0;
        var count = steps.Count;
        if (count == 0)
        {
            place.Text = "";
            previous.IsEnabled = next.IsEnabled = false;
            return;
        }
        var at = Runtime.CurrentStep(Node);
        var (node, panel) = steps[Math.Clamp(at, 0, count - 1)];
        place.Text = NthUiStrings.StepOf(at + 1, count);
        previous.IsEnabled = at > 0;
        next.IsEnabled = at < count - 1;
        var name = Runtime.Text(node);
        stepTitle.Text = name;
        AutomationProperties.SetName(stepTitle, NthUiStrings.StepName(at + 1, count, name));
        if (ReferenceEquals(host.Child, panel.Control)) return;
        if (animate && host.Child is not null) host.Swap(panel.Control);
        else host.Child = panel.Control;
    }
}
