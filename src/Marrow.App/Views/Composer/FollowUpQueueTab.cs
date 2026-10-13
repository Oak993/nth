using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/FollowUpQueueTab.swift (B3 52bc7926), :1-532.

namespace Marrow.App.Views.Composer;

/// <summary>:8-297 <c>struct FollowUpQueueTab: View</c>.</summary>
public sealed class FollowUpQueueTab : Decorator
{
    // :9
    public const double Overlap = ThreadChangesTab.Overlap;

    // :13
    public const int MaxRows = 7;

    private readonly ThreadRuntime runtime;
    private readonly StackPanel rowsPanel;
    private readonly ScrollViewer scrollView;
    // :88-122 the scroll content: the rule, a gap of 4, then the rows in a stack of their own with no spacing.
    private readonly StackPanel listContent;
    private readonly StackPanel rowsStack;
    // :124 `.padding(.top, 6)` on the list; the list's natural height (`listHeight`, :128-130) is measured with it.
    private readonly Border rowsClip;
    private readonly Dictionary<Guid, double> measuredHeights = [];
    private readonly Dictionary<Guid, FollowUpRow> rowControls = [];
    private double listHeight;
    private Guid? pairTarget;
    private RowDrag<Guid> drag = new();

    // :20-25
    public static double CappedHeight(double listHeight, ValueMap<Guid, double> rowHeights, int count)
    {
        if (count <= MaxRows || rowHeights.Count == 0) return listHeight;
        var rows = rowHeights.Values.Sum();
        var top = Math.Max(0, listHeight - rows);
        return top + MaxRows * rows / rowHeights.Count;
    }

    // :31
    private bool IsCollapsed => runtime.FollowUpsCollapsed;

    /// <param name="runtime">:27</param>
    // :46-185
    public FollowUpQueueTab(ThreadRuntime runtime)
    {
        this.runtime = runtime;

        var title = MakeTitle();
        rowsStack = new StackPanel { Spacing = 0 }; // :94 VStack(alignment: .leading, spacing: 0)
        listContent = new StackPanel { Spacing = 4 }; // :88 VStack(alignment: .leading, spacing: 4)
        listContent.Children.Add(new Border
        {
            Height = 1,
            Background = Chrome.SecondaryText.Brush(this),
            Opacity = 0.5
        }); // :89 Divider().opacity(0.5)
        listContent.Children.Add(rowsStack);
        rowsPanel = new StackPanel { Spacing = 0 };
        rowsClip = new Border
        {
            Child = listContent,
            Padding = new Thickness(0, 6, 0, 0), // :124 .padding(.top, 6)
            ClipToBounds = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // The list's own height, not the viewport's: a stretched border would report the scroll view's height back
            // as the natural height (:125-127).
            VerticalAlignment = VerticalAlignment.Top
        };
        // :87,132 the rows live in a scroll view that scrolls only when the list is taller than its capped frame.
        scrollView = new ScrollViewer
        {
            Content = rowsClip,
            ClipToBounds = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        scrollView.SizeChanged += (_, _) => UpdateRowsHeight();
        // :128-130 `listHeight` is the rows' own height, measured inside the scroll view.
        rowsClip.SizeChanged += (_, _) => UpdateRowsHeight();

        rowsPanel.Children.Add(title);
        rowsPanel.Children.Add(scrollView);

        var shape = new Border
        {
            Child = rowsPanel,
            Padding = new Thickness(12, 7, 12, 7 + Overlap),
            MaxWidth = 560, // :153 .frame(maxWidth: 560); the rows' text is maxWidth .infinity, so the tab takes what it is offered
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        SwiftGlass.SetEffect(shape, GlassEffect.Regular(GlassShape.RoundedRectangle(12, RoundedCornerStyle.Continuous)));
        Child = shape;

        RefreshRows();
        this.Track(() => runtime.FollowUps, _ => RefreshRows());
        this.Track(() => runtime.FollowUpsCollapsed, _ => UpdateCollapsed());
    }

    private Control MakeTitle()
    {
        var button = new Button
        {
            Padding = new Thickness(0),
            // :71 .contentShape(.rect): the whole title line is the button, the empty stretch between the words and the
            // chevron included, so the line needs a (clear) fill to be hit.
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Focusable = false
        };

        var line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = SwiftImage.SystemName("arrow.turn.down.right");
        icon.Font(Chrome.InlineIconFont);
        icon.ForegroundStyle(Chrome.SecondaryText);
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        line.Children.Add(icon);

        // :55 HStack(spacing: 7): 7 points between every pair of neighbours.
        var count = new TextBlock
        {
            Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(button),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).MonospacedDigit().Apply(count);
        Grid.SetColumn(count, 1);
        line.Children.Add(count);

        var queued = new TextBlock
        {
            Text = "queued",
            Foreground = Chrome.SecondaryText.Brush(button),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).MonospacedDigit().Apply(queued);
        Grid.SetColumn(queued, 2);
        line.Children.Add(queued);

        // :63 Spacer(minLength: 8) between its two 7-point gaps.
        var spacer = new Border { MinWidth = 8, Margin = new Thickness(7, 0, 7, 0) };
        Grid.SetColumn(spacer, 3);
        line.Children.Add(spacer);

        var chevron = SwiftImage.SystemName("chevron.down");
        chevron.Font(Chrome.InlineIconFont);
        chevron.ForegroundStyle(Chrome.SecondaryText);
        chevron.Width = 22;
        chevron.Height = 22;
        Grid.SetColumn(chevron, 4);
        line.Children.Add(chevron);

        button.Content = line;
        ToolTip.SetTip(button, "Collapse queued follow-ups");
        AutomationProperties.SetName(button, "1 queued follow-up");
        AutomationProperties.SetHelpText(button, "Collapse queued follow-ups");

        button.Click += (_, _) =>
        {
            Transaction.WithAnimation(Chrome.PanelSlide, () => runtime.FollowUpsCollapsed = !runtime.FollowUpsCollapsed);
            Update();
        };

        void Update()
        {
            var promptCount = runtime.FollowUps.Count;
            count.Text = promptCount == 1 ? "1 follow-up" : promptCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " follow-ups";
            var collapsed = runtime.FollowUpsCollapsed;
            chevron.RenderTransform = new RotateTransform(collapsed ? 180 : 0);
            var help = collapsed ? "Expand queued follow-ups" : "Collapse queued follow-ups";
            ToolTip.SetTip(button, help);
            AutomationProperties.SetName(button, promptCount == 1 ? "1 queued follow-up" : promptCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " queued follow-ups");
            AutomationProperties.SetHelpText(button, help);
            // :82 .accessibilityValue(Text(isCollapsed ? "Collapsed" : "Expanded")); the port maps an accessibility value
            // to the item status (ChatBadge.cs:123, SidebarView.cs:3892).
            AutomationProperties.SetItemStatus(button, collapsed ? "Collapsed" : "Expanded");
        }

        button.Tag = (Action)Update;
        Update();
        button.Track(() => runtime.FollowUps, _ => Update());
        button.Track(() => runtime.FollowUpsCollapsed, _ => Update());
        return button;
    }

    // :94-122 ForEach(runtime.followUps, id: \.element.id): a row whose prompt survives keeps its control (so a grip
    // under the pointer is never torn down mid-drag, :187-189), a new or changed prompt gets a new one, and the
    // children follow the queue's order.
    private void RefreshRows()
    {
        var prompts = runtime.FollowUps;
        var ids = prompts.Select(prompt => prompt.Id).ToHashSet();
        foreach (var id in measuredHeights.Keys.Where(id => !ids.Contains(id)).ToArray())
            measuredHeights.Remove(id); // :160-163 a row that left takes its measured height with it

        foreach (var (id, gone) in rowControls.Where(pair => !ids.Contains(pair.Key)).ToArray())
        {
            rowControls.Remove(id);
            rowsStack.Children.Remove(gone);
        }

        // :158-159 a row that left mid-drag (deleted, or sent) ends the drag cleanly.
        if (drag.Id is { } held && !ids.Contains(held))
        {
            drag = new RowDrag<Guid>();
            pairTarget = null;
        }

        var desired = new List<FollowUpRow>();
        foreach (var prompt in prompts)
        {
            if (rowControls.TryGetValue(prompt.Id, out var existing) && SameContent(existing.Prompt, prompt))
            {
                desired.Add(existing);
                continue;
            }

            if (existing is not null) rowsStack.Children.Remove(existing);
            var row = NewRow(prompt);
            rowControls[prompt.Id] = row;
            desired.Add(row);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            if (index < rowsStack.Children.Count && ReferenceEquals(rowsStack.Children[index], desired[index])) continue;
            var at = rowsStack.Children.IndexOf(desired[index]);
            if (at < 0) rowsStack.Children.Insert(index, desired[index]);
            else rowsStack.Children.Move(at, index); // a move, not a remove and add: nothing leaves the visual tree
        }

        UpdateRowStates();
        UpdateRowsHeight();
        UpdateCollapsed();
    }

    // The bundle is the queue's to change mid-drag (:253-258): the row keeps its control, and its grip, through it.
    private static bool SameContent(FollowUpPrompt a, FollowUpPrompt b) => a with { BundleID = null } == b with { BundleID = null };

    private FollowUpRow NewRow(FollowUpPrompt prompt)
    {
        var row = new FollowUpRow(
            1,
            prompt,
            runtime,
            false,
            false,
            false,
            false,
            false,
            false,
            translation => DragChanged(prompt.Id, translation),
            DragEnded);
        // :115 RowHeightReporter: a row's measured height, including its padding, is its slot in the stack.
        row.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Height <= 0) return;
            if (!rowControls.ContainsKey(prompt.Id)) return;
            measuredHeights[prompt.Id] = args.NewSize.Height;
            UpdateRowsHeight();
        };
        return row;
    }

    private void UpdateRowsHeight()
    {
        if (rowsClip.Bounds.Height > 0) listHeight = rowsClip.Bounds.Height;
        var heights = new ValueMap<Guid, double>(measuredHeights.Select(pair =>
            new KeyValuePair<Guid, double>(pair.Key, pair.Value)));
        // :136 the frame: zero while folded, else the natural height cut to whole rows past seven.
        var height = IsCollapsed ? 0 : CappedHeight(listHeight, heights, runtime.FollowUps.Count);
        scrollView.Height = height;
        // :142-146 the mask: 32 points past the sides always, 16 past top and bottom while open, closing with the fold.
        scrollView.Clip = new RectangleGeometry(new Rect(
            -32,
            IsCollapsed ? 0 : -16,
            scrollView.Bounds.Width + 64,
            height + (IsCollapsed ? 0 : 32)));
    }

    private void UpdateCollapsed()
    {
        scrollView.Opacity = IsCollapsed ? 0 : 1; // :137
        scrollView.IsHitTestVisible = !IsCollapsed; // :147
        // :148 .accessibilityHidden(isCollapsed): the port maps accessibilityHidden to the Raw view (ChatBadge.cs:106).
        AutomationProperties.SetAccessibilityView(scrollView,
            IsCollapsed ? AccessibilityView.Raw : AccessibilityView.Content);
        UpdateRowsHeight();
    }

    // :95-111 each row's place in the stack: its number, its bundle neighbours, its rule, how far a held row is lifted.
    private void UpdateRowStates()
    {
        var prompts = runtime.FollowUps;
        var numbers = Numbers(prompts);
        for (var index = 0; index < prompts.Count; index++)
        {
            var prompt = prompts[index];
            if (!rowControls.TryGetValue(prompt.Id, out var row)) continue;
            var previous = index > 0 ? prompts[index - 1] : null;
            var next = index + 1 < prompts.Count ? prompts[index + 1] : null;
            var withPrevious = prompt.BundleID is not null && prompt.BundleID == previous?.BundleID;
            var withNext = prompt.BundleID is not null && prompt.BundleID == next?.BundleID;
            var isDragged = drag.Id == prompt.Id;
            row.UpdateState(isDragged, pairTarget == prompt.Id, isDragged && pairTarget is not null,
                withPrevious, withNext, prompt.Id != prompts[prompts.Count - 1].Id && !isDragged && !withNext,
                numbers.TryGetValue(prompt.Id, out var position) ? position : index + 1,
                isDragged ? drag.VisualOffset : 0); // :118 .offset(y: isDragged ? drag.visualOffset : 0)
        }
    }

    // :212-226
    private static ValueMap<Guid, int> Numbers(IReadOnlyList<FollowUpPrompt> prompts)
    {
        var numbers = new List<KeyValuePair<Guid, int>>();
        var number = 0;
        Guid? previous = null;
        var hadPrevious = false;
        foreach (var prompt in prompts)
        {
            if (prompt.BundleID is null || !hadPrevious || prompt.BundleID != previous) number++;
            numbers.Add(new KeyValuePair<Guid, int>(prompt.Id, number));
            previous = prompt.BundleID;
            hadPrevious = true;
        }
        return new ValueMap<Guid, int>(numbers);
    }

    // :204-208
    private static Animation Slide => Animation.Spring(response: 0.28, dampingFraction: 0.82);
    private const double PairBand = 0.25;

    // :229-282
    private void DragChanged(Guid id, Size translation)
    {
        if (drag.Id != id)
        {
            drag = new RowDrag<Guid> { Id = id };
            pairTarget = null;
        }

        drag = drag with { Translation = translation.Height };
        var order = new ValueList<Guid>(runtime.FollowUps.Select(prompt => prompt.Id));
        var heights = new ValueMap<Guid, double>(measuredHeights.Select(pair =>
            new KeyValuePair<Guid, double>(pair.Key, pair.Value)));
        var (nextDrag, moved) = drag.Settle(order, heights, fallbackHeight: 32,
            (neighbour, placeAfter) =>
            {
                runtime.MoveFollowUp(id, neighbour, placeAfter);
                return true;
            },
            pastCentre: PairBand);
        drag = nextDrag;

        if (runtime.FollowUps.FirstOrDefault(prompt => prompt.Id == id) is { BundleID: not null } held
            && Math.Abs(drag.VisualOffset) > (measuredHeights.TryGetValue(id, out var rowHeight) ? rowHeight : 32) * 0.6)
            runtime.UnbundleFollowUp(held.Id);

        var ids = runtime.FollowUps.Select(prompt => prompt.Id).ToArray();
        Guid? target = null;
        var index = Array.IndexOf(ids, id);
        if (index >= 0)
        {
            var own = measuredHeights.TryGetValue(id, out var ownHeight) ? ownHeight : 32;
            var offset = drag.VisualOffset;
            Guid? neighbour = offset > 0 && index + 1 < ids.Length ? ids[index + 1]
                : offset < 0 && index > 0 ? ids[index - 1]
                : null;
            if (neighbour is { } neighbourID)
            {
                var slot = measuredHeights.TryGetValue(neighbourID, out var slotHeight) ? slotHeight : 32;
                var toCentre = (own + slot) / 2;
                if (Math.Abs(Math.Abs(offset) - toCentre) <= slot * PairBand) target = neighbourID;
            }
        }

        if (target != pairTarget)
            Transaction.WithAnimation(Slide, () => pairTarget = target);
        RefreshRows();
    }

    // :284-296
    private void DragEnded()
    {
        if (drag.Id is { } held && pairTarget is { } target)
        {
            Transaction.WithAnimation(Animation.Spring(response: 0.36, dampingFraction: 0.78),
                () => runtime.BundleFollowUp(held, target));
            pairTarget = null;
        }
        if (drag.Id is null) return;
        Transaction.WithAnimation(Animation.Spring(response: 0.32, dampingFraction: 0.8),
            () => drag = new RowDrag<Guid>());
        RefreshRows();
    }

    /// <summary>:190-199 <c>private struct RowHeightReporter: ViewModifier</c>.</summary>
    private sealed record RowHeightReporter(Guid Id, Binding<ValueMap<Guid, double>> Heights)
    {
        // :194-198
        public Control Body(Control content) => content;
    }
}

/// <summary>:299-510 <c>private struct FollowUpRow: View</c>.</summary>
public sealed class FollowUpRow : Decorator
{
    private int position;
    private readonly FollowUpPrompt prompt;
    private readonly ThreadRuntime runtime;
    private readonly AttachmentPreviewSlot preview = new();
    private readonly PromptEditorPopover<FollowUpEditor> editor = new();
    private readonly Action<Size> onDragChanged;
    private readonly Action onDragEnded;
    private bool isDragged;
    private bool isBundledWithPrevious;
    private bool isBundledWithNext;
    private bool isPairTarget;
    private bool isPairing;
    private bool showsRule;
    // The tab's `drag.visualOffset` for the held row (:118).
    private double offsetY;
    // :324 whether the pointer is over the reorder grip, which brightens its icon (no cursor call - that is
    // .pointerStyle(isDragged ? .grabActive : .grabIdle) alone).
    private bool isHoveringGrip;
    private Point? dragOrigin;
    private Control? dragRoot;
    private readonly Border rowSurface;
    private readonly Grid rowLine;
    private readonly Border rule;
    private readonly Border pairFrame;
    private SwiftImage? grip;
    private QueueIconButton? sendButton;

    /// <summary>:301 <c>let prompt: FollowUpPrompt</c>.</summary>
    internal FollowUpPrompt Prompt => prompt;

    // :331-334
    private static double ProseLift
    {
        get
        {
            var font = SwiftFont.System(12).Resolve();
            var typeface = new Typeface(font.FontFamily, font.FontStyle, font.FontWeight);
            var cap = new FormattedText("H", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, font.FontSize, Brushes.Black).BuildGeometry(default)?.Bounds.Height ?? 0;
            var xHeight = new FormattedText("x", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, font.FontSize, Brushes.Black).BuildGeometry(default)?.Bounds.Height ?? 0;
            return Math.Round((cap - xHeight), MidpointRounding.AwayFromZero) / 2;
        }
    }

    /// <param name="position">:300</param>
    /// <param name="prompt">:301</param>
    /// <param name="runtime">:302</param>
    /// <param name="isDragged">:304</param>
    /// <param name="showsRule">:306</param>
    /// <param name="isBundledWithPrevious">:308</param>
    /// <param name="isBundledWithNext">:310</param>
    /// <param name="isPairTarget">:312</param>
    /// <param name="isPairing">:314</param>
    /// <param name="onDragChanged">:315</param>
    /// <param name="onDragEnded">:316</param>
    // :336-492
    public FollowUpRow(
        int position,
        FollowUpPrompt prompt,
        ThreadRuntime runtime,
        bool isDragged,
        bool showsRule,
        bool isBundledWithPrevious,
        bool isBundledWithNext,
        bool isPairTarget,
        bool isPairing,
        Action<Size> onDragChanged,
        Action onDragEnded)
    {
        this.position = position;
        this.prompt = prompt;
        this.runtime = runtime;
        this.isDragged = isDragged;
        this.showsRule = showsRule;
        this.isBundledWithPrevious = isBundledWithPrevious;
        this.isBundledWithNext = isBundledWithNext;
        this.isPairTarget = isPairTarget;
        this.isPairing = isPairing;
        this.onDragChanged = onDragChanged;
        this.onDragEnded = onDragEnded;

        // :343 HStack(alignment: .center, spacing: 8): the number or link, the grip, the thumbnails, the text, a spacer
        // and the buttons. Columns, so the text takes what is left and the buttons sit at the trailing edge (:426).
        rowLine = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        // :457 .padding(.vertical, 5): a one-line row is 32 tall.
        var padded = new Border { Padding = new Thickness(0, 5), Child = rowLine };
        // :458-460 the rule is an overlay along the bottom edge: it adds no height to the row.
        rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        // :469-472 the pair target's frame is a stroke over the capsule: it adds no size either.
        pairFrame = new Border { IsHitTestVisible = false, IsVisible = false, CornerRadius = new CornerRadius(999) };
        var body = new Grid();
        body.Children.Add(padded);
        body.Children.Add(rule);
        body.Children.Add(pairFrame);
        rowSurface = new Border
        {
            Child = body,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Child = rowSurface;
        rowSurface.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) InvalidateVisual(); }; // the held capsule's shadow (Render)
        BuildRow();
        UpdateLeading();
        UpdateGrip();
        UpdateSurface();

        this.Track(() => runtime.IsRunning, _ => UpdateSendHelp());
        this.Track(() => runtime.HasWorkingNativeHeads, _ => UpdateSendHelp());
        this.Track(() => runtime.FollowUpEdit, _ => SyncEditor());

        // :386 the DragGesture is chained directly onto the grip Image (28x22), not the row - PointerPressed/
        // Moved/Released are wired onto the grip itself inside BuildRow(), not here.
        DetachedFromVisualTree += (_, _) =>
        {
            preview.Close();
            editor.Close();
        };
    }

    // The number, the link or the unlink button: the first cell of the line, rebuilt when the row's bundle state changes.
    private void UpdateLeading()
    {
        Control leading;
        if (isBundledWithPrevious || isPairTarget || isPairing)
        {
            var link = SwiftImage.SystemName("link");
            link.Font(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold));
            link.ForegroundStyle(Chrome.Accent);
            if (isBundledWithPrevious)
            {
                var unlink = new Button
                {
                    Content = link,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent), // :361 .contentShape(.rect): the whole 14 by 22 cell takes the click
                    BorderThickness = new Thickness(0),
                    Focusable = false,
                    Width = 14,
                    Height = 22
                };
                ToolTip.SetTip(unlink, "Unlink from the follow-up above");
                AutomationProperties.SetName(unlink, "Unlink");
                unlink.Click += (_, _) =>
                {
                    Transaction.WithAnimation(Animation.Spring(response: 0.3, dampingFraction: 0.8),
                        () => runtime.UnbundleFollowUp(prompt.Id));
                };
                leading = unlink;
            }
            else
            {
                link.IsHitTestVisible = false;
                link.Width = 14; // :377 the group's frame(width: 14)
                // :368 .accessibilityHidden(true)
                AutomationProperties.SetAccessibilityView(link, AccessibilityView.Raw);
                leading = link;
            }
        }
        else
        {
            var number = new TextBlock
            {
                Text = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Width = 14,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (isBundledWithNext ? Chrome.Accent : Chrome.SecondaryText).Brush(this)
            };
            SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium).MonospacedDigit().Apply(number);
            // :374 .accessibilityHidden(true)
            AutomationProperties.SetAccessibilityView(number, AccessibilityView.Raw);
            leading = number;
        }

        Grid.SetColumn(leading, 0);
        if (leadingCell is { } old) rowLine.Children.Remove(old);
        leadingCell = leading;
        rowLine.Children.Add(leading);
    }

    private Control? leadingCell;

    // Built once: the grip under the pointer must survive every state change of the row (:187-189).
    private void BuildRow()
    {
        var gripImage = SwiftImage.SystemName("line.3.horizontal");
        grip = gripImage;
        gripImage.Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium));
        gripImage.Width = 28;
        gripImage.Height = 22;
        // :382 .contentShape(.rect): the whole 28 by 22 takes the pointer, not only the strokes of the glyph, so the
        // gesture's host is a clear cell around it.
        var gripHost = new Border { Background = new SolidColorBrush(Colors.Transparent), Child = gripImage, Margin = new Thickness(8, 0, 0, 0) };
        // :384 .pointerStyle(isDragged ? .grabActive : .grabIdle)
        // :48 (Marrow.UI.Port.PointerStyleControl) resolves RenderScaling as 1 until the control has a TopLevel;
        // the cursor is baked when the grip is built, before the caller attaches the row. Re-apply once the
        // grip is actually attached, so a real monitor's RenderScaling gets picked up.
        gripImage.AttachedToVisualTree += (_, _) => gripImage.PointerStyle(isDragged ? PointerStyle.GrabActive : PointerStyle.GrabIdle);
        ToolTip.SetTip(gripHost, "Drag above or below to reorder · drop onto the middle of another to send them together"); // U+00B7 MIDDLE DOT
        AutomationProperties.SetName(gripHost, "Drag to reorder");
        // :383 .onHover { hovering in isHoveringGrip = hovering } - brightness only, no cursor call.
        gripHost.PointerEntered += (_, _) => { isHoveringGrip = true; UpdateGripForeground(gripImage); };
        gripHost.PointerExited += (_, _) => { isHoveringGrip = false; UpdateGripForeground(gripImage); };
        // :386-389 the DragGesture (minimumDistance 1, .global) is chained onto the grip Image itself, so SwiftUI
        // only recognizes the reorder drag inside this 28x22 hit rect - not anywhere else in the row.
        gripHost.PointerPressed += OnPointerPressed;
        gripHost.PointerMoved += OnPointerMoved;
        gripHost.PointerReleased += OnPointerReleased;
        gripHost.PointerCaptureLost += (_, _) => EndPointerDrag();
        Grid.SetColumn(gripHost, 1);
        rowLine.Children.Add(gripHost);

        if (prompt.Attachments.Count > 0)
        {
            var thumbnails = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var attachment in prompt.Attachments)
                thumbnails.Children.Add(new Marrow.App.Views.Chat.AttachmentThumbnail(
                    attachment, preview, size: 22, anchorless: true));
            thumbnails.WindowRectAnchor(rect => preview.SetAnchor(rect));
            thumbnails.DetachedFromVisualTree += (_, _) => preview.Close();
            Grid.SetColumn(thumbnails, 2);
            rowLine.Children.Add(thumbnails);
        }

        var empty = prompt.Text.Trimmed().Length == 0;
        var text = new TextBlock
        {
            Text = empty ? AttachmentOnlyLabel : prompt.Text,
            Foreground = (empty ? Chrome.SecondaryText : Chrome.PrimaryText.Opacity(0.9)).Brush(this),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2, // :413,422 .lineLimit(2)
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            // :429 .alignmentGuide(.center) { $0[.center] + proseLift }: the text sits `proseLift` higher than the
            // centre line, which moves it without changing the row's layout.
            RenderTransform = new TranslateTransform(0, -ProseLift)
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(text);
        Grid.SetColumn(text, 3);
        rowLine.Children.Add(text);

        // :430 Spacer(minLength: 4)
        var spacer = new Border { MinWidth = 4, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(spacer, 4);
        rowLine.Children.Add(spacer);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(8, 0, 0, 0) };
        sendButton = new QueueIconButton("paperplane", GetSendHelp(), () => runtime.SendFollowUpNow(prompt.Id, byOwner: true));
        buttons.Children.Add(sendButton);
        var pencil = new QueueIconButton("pencil", "Edit follow-up", () =>
        {
            runtime.FollowUpEdit = runtime.FollowUpEdit?.Id == prompt.Id
                ? null
                : new PromptEdit(prompt.Id, prompt.Text, prompt.Attachments);
        });
        pencil.WindowRectAnchor(rect =>
        {
            editor.SetAnchor(rect);
            SyncEditor();
        });
        buttons.Children.Add(pencil);
        buttons.Children.Add(new QueueIconButton("trash", "Delete follow-up", () => runtime.RemoveFollowUp(prompt.Id)));
        Grid.SetColumn(buttons, 5);
        rowLine.Children.Add(buttons);
    }

    // :380 `isBundledWithNext ? Color.accentColor.opacity(isHoveringGrip ? 1 : 0.85) : Chrome.secondaryText.opacity(isHoveringGrip ? 1 : 0.7)`
    private void UpdateGripForeground(SwiftImage image) => image.ForegroundStyle(isBundledWithNext
        ? Chrome.Accent.Opacity(isHoveringGrip ? 1 : 0.85)
        : Chrome.SecondaryText.Opacity(isHoveringGrip ? 1 : 0.7));

    private void UpdateGrip()
    {
        if (grip is null) return;
        UpdateGripForeground(grip); // :380
        grip.PointerStyle(isDragged ? PointerStyle.GrabActive : PointerStyle.GrabIdle); // :384
    }

    private string GetSendHelp() =>
        runtime.HasWorkingNativeHeads && runtime.IsRunning
            ? "Send as soon as the heads report"
            : runtime.IsRunning ? "Send now (stops the running turn)" : "Send now";

    private void UpdateSendHelp()
    {
        sendButton?.UpdateHelp(GetSendHelp());
    }

    private void SyncEditor()
    {
        editor.Sync(runtime.FollowUpEdit?.Id == prompt.Id ? runtime.FollowUpEdit : null,
            () => runtime.FollowUpEdit = null,
            edit => new FollowUpEditor(edit, runtime));
    }

    private string AttachmentOnlyLabel
    {
        get
        {
            var images = prompt.Attachments.Count(attachment => attachment.IsImage);
            var files = prompt.Attachments.Count - images;
            var parts = new List<string>();
            if (images == 1) parts.Add("1 image");
            else if (images > 1) parts.Add(images.ToString(System.Globalization.CultureInfo.InvariantCulture) + " images");
            if (files == 1) parts.Add("1 file");
            else if (files > 1) parts.Add(files.ToString(System.Globalization.CultureInfo.InvariantCulture) + " files");
            return parts.Count == 0 ? "Empty follow-up" : string.Join(", ", parts);
        }
    }

    internal void UpdateState(bool dragged, bool pairTarget, bool pairing, bool bundledWithPrevious,
        bool bundledWithNext, bool rule, int? position = null, double offsetY = 0)
    {
        var newPosition = position ?? this.position;
        if (isDragged == dragged && isPairTarget == pairTarget && isPairing == pairing
            && isBundledWithPrevious == bundledWithPrevious && isBundledWithNext == bundledWithNext
            && showsRule == rule && this.position == newPosition && this.offsetY == offsetY) return;
        var leadingChanged = isPairTarget != pairTarget || isPairing != pairing
            || isBundledWithPrevious != bundledWithPrevious || isBundledWithNext != bundledWithNext
            || this.position != newPosition;
        isDragged = dragged;
        isPairTarget = pairTarget;
        isPairing = pairing;
        isBundledWithPrevious = bundledWithPrevious;
        isBundledWithNext = bundledWithNext;
        showsRule = rule;
        this.position = newPosition;
        this.offsetY = offsetY;
        // Only the number or link cell and the surface follow the state: the grip under the pointer stays the
        // same control (:187-189).
        if (leadingChanged) UpdateLeading();
        UpdateGrip();
        UpdateSurface();
    }

    // :474-477 `Capsule(style: .continuous).fill(Chrome.overlay(0.12)).shadow(color: .black.opacity(0.28), radius: 10, y: 4)` while held: the
    // capsule's shadow is its own coverage at the fill's alpha, drawn under it (SwiftShadow.DrawShadowOf, ED-358; sigma 10, ED-327).
    // The row draws it before its children, so under the capsule; the pair target's capsule (:466-472) casts none.
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (LiftShadow is { } shadow) SwiftShadow.DrawShadowOf(context, this, rowSurface, shadow, LiftShadowRadius, y: LiftShadowY);
    }

    internal const double LiftShadowRadius = 10, LiftShadowY = 4; // :477 radius 10 (sigma 10, ED-327), y 4

    /// <summary>:477 the held capsule's <c>.shadow(color:)</c> as the Swift states it (black 0.28), or null while the row is not held.</summary>
    internal Color? LiftShadow => isDragged ? Color.FromArgb((byte)(0.28 * 255), 0, 0, 0) : null;

    /// <summary>What Render draws: <see cref="LiftShadow"/> cast at the capsule fill's alpha (SwiftShadow.Cast), transparent while not held.</summary>
    internal Color DrawnLiftShadow => LiftShadow is { } shadow ? SwiftShadow.Cast(shadow, SwiftShadow.FillAlpha(rowSurface.Background) * rowSurface.Opacity) : default;

    private void UpdateSurface()
    {
        // :118 .offset(x: pairTarget != nil && isDragged ? 14 : 0, y: isDragged ? drag.visualOffset : 0)
        RenderTransform = new TranslateTransform(isPairing ? 14 : 0, isDragged ? offsetY : 0);
        // :482 .scaleEffect(isPairTarget ? 1.008 : isDragged ? 1.01 : 1)
        var scale = isPairTarget ? 1.008 : isDragged ? 1.01 : 1;
        rowSurface.RenderTransform = new ScaleTransform(scale, scale);
        // :464-479 the lifted row rides on a capsule filled Chrome.overlay(0.12) with a black 0.28 shadow (radius 10,
        // y 4); a pair target is lit with an accent capsule at 0.18 and framed 1.5 wide at 0.65.
        rowSurface.Background = isDragged
            ? Chrome.Overlay(0.12).Brush(this)
            : isPairTarget ? Chrome.Accent.Opacity(0.18).Brush(this) : null;
        rowSurface.CornerRadius = isDragged || isPairTarget ? new CornerRadius(999) : new CornerRadius(0);
        // :477 the lifted capsule's shadow (black 0.28, radius 10, y 4; sigma 10, ED-327) is its own coverage, cast at the fill's alpha and
        // drawn under it (ED-358): drawn by this row in Render, before its children. The pair target's capsule casts none.
        InvalidateVisual();
        pairFrame.IsVisible = isPairTarget;
        if (isPairTarget)
        {
            pairFrame.BorderBrush = Chrome.Accent.Opacity(0.65).Brush(this);
            pairFrame.BorderThickness = new Thickness(1.5);
        }
        // :458-460 the rule under the row, off for the last row, while lifted and when the next row shares the bundle.
        rule.IsVisible = showsRule;
        rule.Background = Chrome.SecondaryText.Opacity(0.35).Brush(this);
        ZIndex = isDragged ? 1 : 0; // :120
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false) return;
        dragRoot = TopLevel.GetTopLevel(this);
        dragOrigin = e.GetPosition(dragRoot);
        // Captured on the grip itself (the event's source - see the grip.PointerPressed wiring in BuildRow()), not
        // the whole row, so the drag that follows stays scoped to :386's 28x22 hit rect regardless of where the
        // pointer travels afterward.
        e.Pointer.Capture(sender as IInputElement);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragOrigin is not { } origin || dragRoot is null) return;
        var current = e.GetPosition(dragRoot);
        onDragChanged(new Size(current.X - origin.X, current.Y - origin.Y));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => EndPointerDrag();

    private void EndPointerDrag()
    {
        if (dragOrigin is null) return;
        dragOrigin = null;
        dragRoot = null;
        onDragEnded();
    }
}


/// <summary>:512-532 <c>private struct QueueIconButton: View</c>.</summary>
public sealed class QueueIconButton : Decorator
{
    private readonly Button button;

    /// <param name="symbol">:513</param>
    /// <param name="help">:514</param>
    /// <param name="action">:516</param>
    /// <param name="isEnabled">:515</param>
    // :518-531
    public QueueIconButton(string symbol, string help, Action action, bool isEnabled = true)
    {
        button = new Button
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), // :524 .contentShape(.rect): the whole 22 by 22 takes the click
            BorderThickness = new Thickness(0),
            Width = 22,
            Height = 22,
            Focusable = false,
            IsEnabled = isEnabled
        };
        var icon = SwiftImage.SystemName(symbol);
        icon.Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold));
        icon.ForegroundStyle(Chrome.SecondaryText.Opacity(isEnabled ? 1 : 0.35));
        icon.Width = 22;
        icon.Height = 22;
        button.Content = icon;
        UpdateHelp(help);
        button.Click += (_, _) => action();
        Child = button;
    }

    internal void UpdateHelp(string help)
    {
        ToolTip.SetTip(button, help);
        AutomationProperties.SetName(button, help);
    }
}
