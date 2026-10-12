// PORT-ONLY: web surfaces W16a (DESIGN.md 7.2 components card, section, stack, row, grid, form, actions; 7.4 streaming;
// 7.5 look; canvas index.html .uic card; ED-NEW-web-w16a-1). No Swift counterpart. The containers: a column of slotted
// children (stack, card, section, a tab's or step's panel, the view itself), a row (a flow from the start edge; a centred,
// end or spread row holds its children until the row is complete, because centring or spreading moves earlier children as
// later ones arrive), a grid (equal columns filled in order), a form (its submit button arrives when the form is complete,
// so it never moves down as fields arrive) and the actions row. Also the factory that maps a kind to its view.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.NthUi;
using Marrow.UI.Port;

namespace Marrow.UI.NthUi;

/// <summary>A column of slotted children: stack, and the base of card, section, form and panels.</summary>
public class NthUiStackView : NthUiNodeView
{
    public NthUiStackView(NthUiContext context, NthUiNode node, double gap) : base(context, node)
    {
        Column = new NthUiColumn { Gap = gap };
    }

    protected NthUiColumn Column { get; }

    public override Control Control => Column;

    public override NthUiSlotMode SlotMode => NthUiSlotMode.Exact;

    public override bool HostsPending => true;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Role == NthUiRole.Declaration) return null;
        var view = Context.Create(child);
        Context.Place(view, this);
        return view;
    }

    protected internal override void Insert(NthUiSlot slot, bool visible)
    {
        Column.Children.Add(slot);
        slot.Arrive(visible, revealSize: true);
    }

    /// <summary>A header row: an icon, a title in <paramref name="font"/>, and a subtitle under it.</summary>
    protected internal static Control Header(string? icon, string title, string? subtitle, SwiftFont font, out TextBlock titleBlock, out TextBlock? subtitleBlock)
    {
        subtitleBlock = null;
        titleBlock = NthUiText.Block(title, font, NthUiTokens.Text1);
        AutomationProperties.SetHeadingLevel(titleBlock, 3);
        Control head = titleBlock;
        if (NthUiText.Icon(icon, 16, NthUiTokens.Text2) is { } glyph)
        {
            glyph.VerticalAlignment = VerticalAlignment.Center;
            titleBlock.VerticalAlignment = VerticalAlignment.Center;
            head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { glyph, titleBlock } };
        }
        if (string.IsNullOrEmpty(subtitle)) return head;
        subtitleBlock = NthUiText.Block(subtitle, NthUiTokens.Small, NthUiTokens.Text2);
        return new StackPanel { Spacing = 2, Children = { head, subtitleBlock } };
    }
}

/// <summary>`card`: a calm tinted card (canvas .uic: radius 16, padding 14, gap 12) with a header.</summary>
public sealed class NthUiCardView : NthUiStackView
{
    private static readonly NthUiSurface CardSurface = new(NthUiTokens.CardFill, NthUiTokens.CardRim, NthUiTokens.CardRadius, true, NthUiTokens.CardPadding);
    private readonly Border border;
    private readonly TextBlock? title;
    private readonly TextBlock? subtitle;

    public NthUiCardView(NthUiContext context, NthUiNode node) : base(context, node, NthUiTokens.CardGap)
    {
        border = new Border { Padding = new Thickness(NthUiTokens.CardPadding), Child = Column };
        var text = Runtime.Text(node);
        if (text.Length > 0 || node.Arg("icon") is not null)
        {
            var header = Header(node.Arg("icon")?.Literal.AsText, text, node.Arg("subtitle") is null ? null : Runtime.Display(node, "subtitle"), NthUiTokens.BodyStrong, out var titleBlock, out subtitle);
            title = titleBlock;
            Column.Children.Add(header);
        }
        AutomationProperties.SetName(border, text.Length > 0 ? text : NthUiStrings.LiveAnswer);
        AutomationProperties.SetControlTypeOverride(border, AutomationControlType.Group);
    }

    public override Control Control => border;

    public override NthUiSurface? Surface => CardSurface;

    public override void Refresh()
    {
        if (title is not null) title.Text = Runtime.Text(Node);
        if (subtitle is not null) subtitle.Text = Runtime.Display(Node, "subtitle");
    }
}

/// <summary>`section`: a titled group inside a card, no surface of its own.</summary>
public sealed class NthUiSectionView : NthUiStackView
{
    private readonly TextBlock? title;

    public NthUiSectionView(NthUiContext context, NthUiNode node) : base(context, node, 10)
    {
        var text = Runtime.Text(node);
        if (text.Length > 0)
        {
            var header = Header(node.Arg("icon")?.Literal.AsText, text, null, NthUiTokens.BodyStrong, out var titleBlock, out _);
            title = titleBlock;
            Column.Children.Add(header);
        }
        AutomationProperties.SetName(Column, text);
    }

    public override void Refresh()
    {
        if (title is not null) title.Text = Runtime.Text(Node);
    }
}

/// <summary>A tab's or a step's panel: a column its owner shows when it is chosen.</summary>
public sealed class NthUiPanelView(NthUiContext context, NthUiNode node, NthUiNodeView? owner = null) : NthUiStackView(context, node, NthUiTokens.CardGap)
{
    /// <summary>The tabs or steps view that shows this panel and draws its label.</summary>
    public NthUiNodeView? Owner => owner;

    /// <summary>The panel's label (a tab's or step's title) reads state: its owner redraws it.</summary>
    public override void Refresh() => owner?.Refresh();
}

/// <summary>`row`: children side by side (canvas .ctr: gap 12 by 18), wrapping.</summary>
public sealed class NthUiRowView : NthUiNodeView
{
    private readonly NthUiFlow flow;
    private bool closed;

    public NthUiRowView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var gap = Runtime.Value(node, "gap") is { IsNumber: true } g ? Math.Clamp(g.AsNumber, 0, 64) : 12;
        flow = new NthUiFlow
        {
            Gap = gap,
            LineGap = Math.Min(gap, 12),
            Align = node.Arg("align")?.Literal.AsText switch
            {
                "center" => NthUiAlign.Center,
                "end" => NthUiAlign.End,
                "between" => NthUiAlign.Between,
                _ => NthUiAlign.Start,
            },
        };
    }

    public override Control Control => flow;

    public override bool HostsPending => true;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Role == NthUiRole.Declaration) return null;
        var view = Context.Create(child);
        Context.Place(view, this, NthUiReveal.Horizontal);
        return view;
    }

    protected internal override void Insert(NthUiSlot slot, bool visible)
    {
        flow.Children.Add(slot);
        slot.Arrive(visible, revealSize: false, hold: flow.Align != NthUiAlign.Start && !closed);
    }

    public override void Close()
    {
        if (closed) return;
        closed = true;
        foreach (var slot in flow.Children.OfType<NthUiSlot>()) slot.Release();
    }
}

/// <summary>`grid`: equal columns (1 to 6, two when not given), filled in order.</summary>
public sealed class NthUiGridView : NthUiNodeView
{
    private readonly NthUiGrid grid;

    public NthUiGridView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        var cols = Runtime.Value(node, "cols") is { IsNumber: true } c ? (int)Math.Clamp(Math.Round(c.AsNumber), 1, 6) : 2;
        var gap = Runtime.Value(node, "gap") is { IsNumber: true } g ? Math.Clamp(g.AsNumber, 0, 64) : NthUiTokens.CardGap;
        grid = new NthUiGrid { Columns = cols, Gap = gap };
    }

    public override Control Control => grid;

    public override bool HostsPending => true;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Role == NthUiRole.Declaration) return null;
        var view = Context.Create(child);
        Context.Place(view, this);
        return view;
    }

    protected internal override void Insert(NthUiSlot slot, bool visible)
    {
        grid.Children.Add(slot);
        slot.Arrive(visible, revealSize: false);
    }
}

/// <summary>`form`: its fields in a column and, once the form is complete, its submit button (submitting sends).</summary>
public sealed class NthUiFormView : NthUiStackView
{
    private readonly Border border;
    private readonly TextBlock? title;
    private bool closed;

    public NthUiFormView(NthUiContext context, NthUiNode node) : base(context, node, NthUiTokens.CardGap)
    {
        border = new Border { Child = Column };
        var text = Runtime.Text(node);
        if (text.Length > 0)
        {
            var header = Header(null, text, null, NthUiTokens.BodyStrong, out var titleBlock, out _);
            title = titleBlock;
            Column.Children.Add(header);
        }
        AutomationProperties.SetName(border, text);
        AutomationProperties.SetControlTypeOverride(border, AutomationControlType.Group);
    }

    public override Control Control => border;

    public override void Refresh()
    {
        if (title is not null) title.Text = Runtime.Text(Node);
    }

    public override void Close()
    {
        if (closed) return;
        closed = true;
        var label = Node.Arg("submit") is null ? NthUiStrings.Send : Runtime.Display(Node, "submit");
        var button = NthUiPress.Button(label, NthUiFaceStyle.Primary, "send", () => Context.Run(Node));
        button.HorizontalAlignment = HorizontalAlignment.Left;
        var slot = new NthUiSlot(Context.Motion) { Key = Node.Key + "/submit", Child = button };
        Insert(slot, true);
    }
}

/// <summary>`actions`: a row of buttons, one per action.</summary>
public sealed class NthUiActionsView : NthUiNodeView
{
    private readonly NthUiFlow flow = new() { Gap = NthUiTokens.RowGap };

    public NthUiActionsView(NthUiContext context, NthUiNode node) : base(context, node)
    {
    }

    public override Control Control => flow;

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Role != NthUiRole.Action) return null;
        var view = Context.Create(child);
        Context.Place(view, this, NthUiReveal.Horizontal);
        return view;
    }

    protected internal override void Insert(NthUiSlot slot, bool visible)
    {
        flow.Children.Add(slot);
        slot.Arrive(visible, revealSize: false);
    }
}

/// <summary>Maps each kind to its core view.</summary>
public static class NthUiComponents
{
    public static NthUiNodeView Create(NthUiContext context, NthUiNode node)
    {
        if (node.Degraded) return new NthUiTextView(context, node);
        return node.Kind switch
        {
            "text" => new NthUiTextView(context, node),
            "heading" => new NthUiHeadingView(context, node),
            "card" => new NthUiCardView(context, node),
            "section" => new NthUiSectionView(context, node),
            "stack" => new NthUiStackView(context, node, Gap(context, node, 10)),
            "row" => new NthUiRowView(context, node),
            "grid" => new NthUiGridView(context, node),
            "divider" => new NthUiDividerView(context, node),
            "badge" => new NthUiBadgeView(context, node),
            "stat" => new NthUiStatView(context, node),
            "callout" => new NthUiCalloutView(context, node),
            "image" => new NthUiImageView(context, node),
            "svg" => new Diagram.NthUiSvgView(context, node),
            "diagram" => new Diagram.NthUiDiagramView(context, node),
            "code" => new NthUiCodeView(context, node),
            "chart" => new Charts.NthUiChartView(context, node),
            "table" => new NthUiTableView(context, node),
            "list" => new NthUiListView(context, node),
            "checklist" => new NthUiChecklistView(context, node),
            "timeline" => new NthUiTimelineView(context, node),
            "steps" => new NthUiStepsView(context, node),
            "tabs" => new NthUiTabsView(context, node),
            "chips" => new NthUiChipsView(context, node),
            "followups" => new NthUiFollowupsView(context, node),
            "map" => new Map.NthUiMapView(context, node),
            "segmented" => new NthUiSegmentedView(context, node),
            "stepper" => new NthUiStepperView(context, node),
            "slider" => new NthUiSliderView(context, node),
            "toggle" when node.Role == NthUiRole.Input => new NthUiToggleView(context, node),
            "textfield" => new NthUiTextFieldView(context, node),
            "select" => new NthUiSelectView(context, node),
            "date" => new NthUiDateView(context, node),
            "form" => new NthUiFormView(context, node),
            "actions" => new NthUiActionsView(context, node),
            _ when node.Role == NthUiRole.Action => new NthUiActionView(context, node),
            _ => new NthUiTextView(context, node),
        };
    }

    private static double Gap(NthUiContext context, NthUiNode node, double fallback) =>
        context.Runtime.Value(node, "gap") is { IsNumber: true } g ? Math.Clamp(g.AsNumber, 0, 64) : fallback;
}
