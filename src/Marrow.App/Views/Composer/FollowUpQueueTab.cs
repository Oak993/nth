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

// Source: DroppyCode/App/Views/Composer/FollowUpQueueTab.swift (B2 1fbf81e), :1-547.

namespace Marrow.App.Views.Composer;

/// <summary>:8-299 <c>struct FollowUpQueueTab: View</c>.</summary>
public sealed class FollowUpQueueTab : Decorator
{
    // :9
    public const double Overlap = ThreadChangesTab.Overlap;

    // :13
    public const int MaxRows = 7;

    private readonly ThreadRuntime runtime;
    private readonly StackPanel rowsPanel;
    private readonly ScrollViewer scrollView;
    private readonly StackPanel listContent;
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
        rowsPanel = new StackPanel { Spacing = 0 };
        listContent = new StackPanel { Spacing = 4 };
        rowsClip = new Border
        {
            Child = listContent,
            ClipToBounds = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scrollView = new ScrollViewer
        {
            Content = rowsClip,
            ClipToBounds = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        scrollView.SizeChanged += (_, _) => UpdateRowsHeight();

        rowsPanel.Children.Add(title);
        rowsPanel.Children.Add(scrollView);

        var shape = new Border
        {
            Child = rowsPanel,
            Padding = new Thickness(12, 7, 12, 7 + Overlap),
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        SwiftGlass.SetEffect(shape, GlassEffect.Regular(GlassShape.RoundedRectangle(12, RoundedCornerStyle.Continuous)));
        Child = shape;

        RefreshRows();
        this.Track(() => runtime.FollowUps, _ => RefreshRows());
        this.Track(() => runtime.FollowUpsCollapsed, _ => UpdateCollapsed());
        this.Track(() => pairTarget, _ => UpdateRowStates());
    }

    private Control MakeTitle()
    {
        var button = new Button
        {
            Padding = new Thickness(0),
            Background = null,
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
        Grid.SetColumn(icon, 0);
        line.Children.Add(icon);

        var count = new TextBlock { Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(button) };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).MonospacedDigit().Apply(count);
        Grid.SetColumn(count, 1);
        line.Children.Add(count);

        var queued = new TextBlock { Text = "queued", Foreground = Chrome.SecondaryText.Brush(button) };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).MonospacedDigit().Apply(queued);
        Grid.SetColumn(queued, 2);
        line.Children.Add(queued);

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
            UpdateTitle(button, count, chevron);
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
        }

        void UpdateTitle(Button target, TextBlock countBlock, SwiftImage chevronImage) => Update();

        button.Tag = (Action)Update;
        Update();
        button.Track(() => runtime.FollowUps, _ => Update());
        button.Track(() => runtime.FollowUpsCollapsed, _ => Update());
        return button;
    }

    private void RefreshRows()
    {
        var ids = runtime.FollowUps.Select(prompt => prompt.Id).ToHashSet();
        foreach (var id in measuredHeights.Keys.Where(id => !ids.Contains(id)).ToArray())
            measuredHeights.Remove(id);

        listContent.Children.Clear();
        listContent.Children.Add(new Border
        {
            Height = 1,
            Background = Chrome.SecondaryText.Brush(this),
            Opacity = 0.5
        });

        var numbers = Numbers(runtime.FollowUps);
        rowControls.Clear();
        for (var index = 0; index < runtime.FollowUps.Count; index++)
        {
            var prompt = runtime.FollowUps[index];
            var previous = index > 0 ? runtime.FollowUps[index - 1] : null;
            var next = index + 1 < runtime.FollowUps.Count ? runtime.FollowUps[index + 1] : null;
            var withPrevious = prompt.BundleID is not null && prompt.BundleID == previous?.BundleID;
            var withNext = prompt.BundleID is not null && prompt.BundleID == next?.BundleID;
            var isDragged = drag.Id == prompt.Id;

            var row = new FollowUpRow(
                numbers.TryGetValue(prompt.Id, out var position) ? position : index + 1,
                prompt,
                runtime,
                isDragged,
                prompt.Id != runtime.FollowUps[runtime.FollowUps.Count - 1].Id && !isDragged && !withNext,
                withPrevious,
                withNext,
                pairTarget == prompt.Id,
                isDragged && pairTarget is not null,
                translation => DragChanged(prompt.Id, translation),
                DragEnded);

            rowControls[prompt.Id] = row;
            row.SizeChanged += (_, args) =>
            {
                if (args.NewSize.Height <= 0) return;
                measuredHeights[prompt.Id] = args.NewSize.Height;
                UpdateRowsHeight();
            };
            listContent.Children.Add(row);
        }

        UpdateRowsHeight();
        UpdateCollapsed();
    }

    private void UpdateRowsHeight()
    {
        if (listContent.Bounds.Height > 0) listHeight = listContent.Bounds.Height;
        var heights = new ValueMap<Guid, double>(measuredHeights.Select(pair =>
            new KeyValuePair<Guid, double>(pair.Key, pair.Value)));
        var height = IsCollapsed ? 0 : CappedHeight(listHeight, heights, runtime.FollowUps.Count);
        scrollView.Height = height;
        rowsClip.Height = height;
        rowsClip.Clip = new RectangleGeometry(new Rect(
            -32,
            IsCollapsed ? 0 : -16,
            rowsClip.Bounds.Width + 64,
            rowsClip.Bounds.Height + (IsCollapsed ? 0 : 32)));
        UpdateRowStates();
    }

    private void UpdateCollapsed()
    {
        scrollView.Opacity = IsCollapsed ? 0 : 1;
        scrollView.IsHitTestVisible = !IsCollapsed;
        AutomationProperties.SetAccessibilityView(scrollView,
            IsCollapsed ? AccessibilityView.Control : AccessibilityView.Content);
        UpdateRowsHeight();
    }

    private void UpdateRowStates()
    {
        foreach (var (id, row) in rowControls)
        {
            var index = runtime.FollowUps.IndexOf(runtime.FollowUps.FirstOrDefault(prompt => prompt.Id == id)!);
            if (index < 0) continue;
            var prompt = runtime.FollowUps[index];
            var previous = index > 0 ? runtime.FollowUps[index - 1] : null;
            var next = index + 1 < runtime.FollowUps.Count ? runtime.FollowUps[index + 1] : null;
            var withPrevious = prompt.BundleID is not null && prompt.BundleID == previous?.BundleID;
            var withNext = prompt.BundleID is not null && prompt.BundleID == next?.BundleID;
            row.UpdateState(drag.Id == id, pairTarget == id, drag.Id == id && pairTarget is not null,
                withPrevious, withNext, prompt.Id != runtime.FollowUps[runtime.FollowUps.Count - 1].Id
                && drag.Id != id && !withNext);
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

    // :229-283
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

    // :285-298
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

/// <summary>:301-525 <c>private struct FollowUpRow: View</c>.</summary>
public sealed class FollowUpRow : Decorator
{
    private readonly int position;
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
    private Point? dragOrigin;
    private Control? dragRoot;
    private readonly Border rowSurface;
    private readonly StackPanel rowLine;
    private QueueIconButton? sendButton;

    // :333-336
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

    /// <param name="position">:302</param>
    /// <param name="prompt">:303</param>
    /// <param name="runtime">:304</param>
    /// <param name="isDragged">:306</param>
    /// <param name="showsRule">:308</param>
    /// <param name="isBundledWithPrevious">:310</param>
    /// <param name="isBundledWithNext">:312</param>
    /// <param name="isPairTarget">:314</param>
    /// <param name="isPairing">:316</param>
    /// <param name="onDragChanged">:317</param>
    /// <param name="onDragEnded">:318</param>
    // :338-508
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

        rowLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        rowSurface = new Border
        {
            Child = rowLine,
            Padding = new Thickness(0, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Child = rowSurface;
        BuildRow();
        UpdateSurface();

        this.Track(() => runtime.IsRunning, _ => UpdateSendHelp());
        this.Track(() => runtime.HasWorkingNativeHeads, _ => UpdateSendHelp());
        this.Track(() => runtime.FollowUpEdit, _ => SyncEditor());

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => EndPointerDrag();
        SizeChanged += (_, _) => UpdateSurface();
        DetachedFromVisualTree += (_, _) =>
        {
            preview.Close();
            editor.Close();
        };
    }

    private void BuildRow()
    {
        rowLine.Children.Clear();

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
                    Background = null,
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
                AutomationProperties.SetAccessibilityView(link, AccessibilityView.Control);
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
            AutomationProperties.SetAccessibilityView(number, AccessibilityView.Control);
            leading = number;
        }
        rowLine.Children.Add(leading);

        var grip = SwiftImage.SystemName("line.3.horizontal");
        grip.Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium));
        grip.ForegroundStyle(isBundledWithNext ? Chrome.Accent.Opacity(0.85) : Chrome.SecondaryText.Opacity(0.7));
        grip.Width = 28;
        grip.Height = 22;
        grip.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(grip, "Drag above or below to reorder \U000000B7 drop onto the middle of another to send them together"); // U+00B7 MIDDLE DOT
        AutomationProperties.SetName(grip, "Drag to reorder");
        grip.PointerEntered += (_, _) => grip.Cursor = new Cursor(StandardCursorType.Hand);
        rowLine.Children.Add(grip);

        if (prompt.Attachments.Count > 0)
        {
            var thumbnails = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var attachment in prompt.Attachments)
                thumbnails.Children.Add(new Marrow.App.Views.Chat.AttachmentThumbnail(
                    attachment, preview, size: 22, anchorless: true));
            thumbnails.WindowRectAnchor(rect => preview.SetAnchor(rect));
            thumbnails.DetachedFromVisualTree += (_, _) => preview.Close();
            rowLine.Children.Add(thumbnails);
        }

        var text = new TextBlock
        {
            Text = prompt.Text.Trimmed().Length == 0 ? AttachmentOnlyLabel : prompt.Text,
            Foreground = (prompt.Text.Trimmed().Length == 0 ? Chrome.SecondaryText : Chrome.PrimaryText.Opacity(0.9)).Brush(this),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 34,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, ProseLift, 0, 0)
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(text);
        rowLine.Children.Add(text);

        rowLine.Children.Add(new Border { MinWidth = 4, HorizontalAlignment = HorizontalAlignment.Stretch });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        sendButton = new QueueIconButton("paperplane", GetSendHelp(), () => runtime.SendFollowUpNow(prompt.Id));
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
        rowLine.Children.Add(buttons);

        rowLine.SizeChanged += (_, _) => UpdateSurface();
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
        bool bundledWithNext, bool rule)
    {
        if (isDragged == dragged && isPairTarget == pairTarget && isPairing == pairing
            && isBundledWithPrevious == bundledWithPrevious && isBundledWithNext == bundledWithNext
            && showsRule == rule) return;
        isDragged = dragged;
        isPairTarget = pairTarget;
        isPairing = pairing;
        isBundledWithPrevious = bundledWithPrevious;
        isBundledWithNext = bundledWithNext;
        showsRule = rule;
        BuildRow();
        UpdateSurface();
    }

    private void UpdateSurface()
    {
        rowSurface.Margin = new Thickness(isPairing ? 14 : 0, 0, 0, 0);
        rowSurface.Opacity = isDragged ? 1 : 1;
        rowSurface.Background = isDragged
            ? Chrome.Overlay(0.12).Brush(this)
            : isPairTarget ? Chrome.Accent.Opacity(0.18).Brush(this) : null;
        if (showsRule)
            rowSurface.BorderBrush = Chrome.SecondaryText.Opacity(0.35).Brush(this);
        else
            rowSurface.BorderBrush = null;
        rowSurface.BorderThickness = showsRule ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        rowSurface.CornerRadius = isDragged || isPairTarget ? new CornerRadius(999) : new CornerRadius(0);
        ZIndex = isDragged ? 1 : 0;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false) return;
        dragRoot = TopLevel.GetTopLevel(this);
        dragOrigin = e.GetPosition(dragRoot);
        e.Pointer.Capture(this);
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

/// <summary>:527-547 <c>private struct QueueIconButton: View</c>.</summary>
public sealed class QueueIconButton : Decorator
{
    private readonly Button button;

    /// <param name="symbol">:528</param>
    /// <param name="help">:529</param>
    /// <param name="action">:531</param>
    /// <param name="isEnabled">:530</param>
    // :533-546
    public QueueIconButton(string symbol, string help, Action action, bool isEnabled = true)
    {
        button = new Button
        {
            Padding = new Thickness(0),
            Background = null,
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
