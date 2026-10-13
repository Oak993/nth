using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineMinimap.swift (B3 52bc792), :1-435.

/// <summary>:4-13 <c>struct TimelineMinimapEntry: Identifiable, Hashable</c>.</summary>
public sealed record TimelineMinimapEntry
{
    public required string Id { get; init; } // :8
    public required string Title { get; init; } // :9
    public required string Snippet { get; init; } // :10
    /// <summary>0...1, drives the tick width. Long replies read wide, short notices narrow.</summary>
    public required double Weight { get; init; } // :12
}

/// <summary>:15-87 <c>enum TimelineMinimap</c>, a caseless enum used as a namespace.</summary>
public static class TimelineMinimap
{
    // ED-NEW-perf-ui-thread-4: a block is immutable and a pass of a running turn keeps every block before the last run as the very object it was, so a block's tick is made once per
    // block object. The 400-entry cache by id below is swept in order by a thread with more user messages than it holds and misses every lookup: every pass remade every tick's words.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DisplayBlock, System.Runtime.CompilerServices.StrongBox<TimelineMinimapEntry?>> BlockEntries = new();

    // :16-19
    public static ValueList<TimelineMinimapEntry> Entries(IReadOnlyList<DisplayBlock> @for)
    {
        var result = new List<TimelineMinimapEntry>();
        foreach (var block in @for)
        {
            var entry = BlockEntries.GetValue(block, static made => new System.Runtime.CompilerServices.StrongBox<TimelineMinimapEntry?>(Entry(made))).Value;
            if (entry is not null) result.Add(entry);
        }
        return new ValueList<TimelineMinimapEntry>(result);
    }

    // :21-35
    private static TimelineMinimapEntry? Entry(DisplayBlock block)
    {
        UserMessage? message = null;
        string? id = null;

        switch (block)
        {
            case DisplayBlock.Turn turn:
                id = turn.Id;
                foreach (var timelineEntry in turn.Entries)
                {
                    if (timelineEntry.Item.TimelineItemContent is not TimelineItem.Content.User user) continue;
                    message = user.Value;
                    break;
                }
                break;
            case DisplayBlock.Group { Value: TimelineGroup.Single single }:
                if (single.Value.Item.TimelineItemContent is TimelineItem.Content.User singleUser)
                {
                    id = single.Value.Id;
                    message = singleUser.Value;
                }
                break;
            case DisplayBlock.Group:
            case DisplayBlock.Working:
            case DisplayBlock.Merging:
            case DisplayBlock.HeadsWorking:
                break;
            default:
                throw new System.Diagnostics.UnreachableException();
        }

        return message is null || id is null ? null : UserEntry(id, message);
    }

    // :37-54
    private static readonly RecentCache<string, TimelineMinimapEntry> EntryCache = new(400);

    // :42-54
    private static TimelineMinimapEntry UserEntry(string id, UserMessage message)
    {
        if (EntryCache.TryGetValue(id, out var cached)) return cached;

        var title = UserTitle(message);
        var snippet = Preview(message.Text);
        var entry = new TimelineMinimapEntry
        {
            Id = id,
            Title = title,
            Snippet = snippet.Length == 0 || SwiftString.Equal(snippet, title) ? "" : snippet,
            Weight = Weight(message.Text),
        };
        EntryCache.Insert(entry, id);
        return entry;
    }

    // :56-63
    private static string UserTitle(UserMessage message)
    {
        var title = FirstWords(message.Text, 8);
        if (title.Length != 0) return title;

        var images = 0;
        foreach (var attachment in message.Attachments)
            if (attachment.IsImage) images++;

        if (images == 1) return "1 image";
        if (images > 1) return images.ToString(System.Globalization.CultureInfo.InvariantCulture) + " images";
        return message.Attachments.Count > 0 ? message.Attachments[0].Name : "Message";
    }

    // :71-76
    private static string FirstWords(string text, int limit)
    {
        var words = Words(text);
        if (words.Count == 0) return "";
        var count = Math.Min(limit, words.Count);
        var result = string.Join(" ", words.GetRange(0, count));
        return words.Count > limit ? result + "\U00002026" : result; // U+2026 HORIZONTAL ELLIPSIS
    }

    // :78-82
    private static string Preview(string text)
    {
        var flat = string.Join(" ", Words(text));
        return SwiftString.Count(flat) > 160
            ? SwiftString.Prefix(flat, 160) + "\U00002026" // U+2026 HORIZONTAL ELLIPSIS
            : flat;
    }

    // :71-82. Swift String.split(whereSeparator: \.isWhitespace), preserving each non-whitespace Character.
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var character in SwiftString.Characters(text))
        {
            if (SwiftCharacter.IsWhitespace(character))
            {
                if (word.Length == 0) continue;
                words.Add(word.ToString());
                word.Clear();
            }
            else
            {
                word.Append(character);
            }
        }
        if (word.Length > 0) words.Add(word.ToString());
        return words;
    }

    // :84-86
    private static double Weight(string text) =>
        Math.Min(1, Math.Max(0.15, (double)SwiftString.Count(text) / 420));
}

/// <summary>:89-131 <c>struct TimelineMinimapColumn: View, Equatable</c>.</summary>
public sealed class TimelineMinimapColumn : Decorator
{
    private IReadOnlyList<DisplayBlock> blocks;
    private readonly TimelineScrollTracking tracking;
    private readonly double centerHeight;
    private readonly Action<string, bool> onNavigate;
    private readonly IReadOnlyList<MinimapOutlineBlock> outline;
    private ValueList<TimelineMinimapEntry> lastEntries = ValueList<TimelineMinimapEntry>.Empty;
    private ValueList<TimelineMinimapEntry> builtEntries = ValueList<TimelineMinimapEntry>.Empty; // the ticks of the rail on show
    private bool attachLooked; // the attach just looked at the ticks: the resize watch's first look (made on attach as well) has nothing to add

    // :94-101, :112-130
    public TimelineMinimapColumn(
        IReadOnlyList<DisplayBlock> blocks,
        TimelineScrollTracking tracking,
        double centerHeight,
        Action<string, bool> onNavigate)
    {
        this.blocks = blocks;
        this.tracking = tracking;
        this.centerHeight = centerHeight;
        this.onNavigate = onNavigate;

        lastEntries = TimelineMinimap.Entries(blocks);
        var outlineBlocks = new List<MinimapOutlineBlock>();
        foreach (var block in blocks)
            outlineBlocks.Add(new MinimapOutlineBlock { Id = block.Id, HasUserMessage = block.HasUserMessage }); // :117 blocks.map
        outline = outlineBlocks;

        Child = MakeSelection(lastEntries);

        // :107-109,112-130 @State lastEntries: the ticks as last built. The owning timeline redraws by creating a new
        // column each time, so the previous outline lives beside the tracking object that outlasts every column.
        AttachedToVisualTree += (_, _) =>
        {
            var resizing = GetValue(AppEnvironment.WindowLiveResizeProperty)?.IsActive == true;
            if (resizing && Held.TryGetValue(tracking, out var held)) lastEntries = held.Value!; // :114 liveResize.isActive ? lastEntries
            else Remember(TimelineMinimap.Entries(this.blocks)); // :126 onAppear
            // ED-NEW-perf-ui-thread-4: the constructor made the rail from these ticks when they are the same ones (the common attach), so it is not made again (it was made twice more here).
            if (!lastEntries.Equals(builtEntries)) Child = MakeSelection(lastEntries);
            attachLooked = true;
        };
        // :120-124 rebuilt once the resize settles, so the ticks land correct after scaling in place.
        this.Track(() => GetValue(AppEnvironment.WindowLiveResizeProperty)?.IsActive == true, resizing =>
        {
            var looked = attachLooked; attachLooked = false; // the watch's first look comes with the attach, which just looked (and a resize settling later is not that look)
            if (resizing || looked) return;
            Remember(TimelineMinimap.Entries(this.blocks));
            Child = MakeSelection(lastEntries);
        });
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TimelineScrollTracking, System.Runtime.CompilerServices.StrongBox<ValueList<TimelineMinimapEntry>>> Held = new();

    private void Remember(ValueList<TimelineMinimapEntry> entries)
    {
        lastEntries = entries;
        Held.Remove(tracking);
        Held.Add(tracking, new System.Runtime.CompilerServices.StrongBox<ValueList<TimelineMinimapEntry>>(entries));
    }

    // :112-130
    private MinimapSelection MakeSelection(ValueList<TimelineMinimapEntry> entries)
    {
        builtEntries = entries;
        return new(entries, outline, tracking, centerHeight, onNavigate);
    }

    /// <summary>
    /// ED-NEW-perf-ui-thread-4: whether this rail already shows what a rail made from <paramref name="next"/> would: the same tracking, the same height and the same ticks (id, title,
    /// snippet, weight). A pass of a running turn changes the turn's block (a tool row, a reply, the turn's end) and nothing the rail shows, and the rail is kept: its hover and its scrub go
    /// on, and no control of it is made again. The rail reads nothing else of the blocks (its selection falls back to the last tick, which the ticks hold). When it shows the same, the
    /// column takes <paramref name="next"/> as its blocks, so a later attach or resize settle reads the latest.
    /// </summary>
    internal bool Adopt(IReadOnlyList<DisplayBlock> next, TimelineScrollTracking nextTracking, double nextCenterHeight)
    {
        if (!ReferenceEquals(tracking, nextTracking) || centerHeight != nextCenterHeight) return false;
        if (ReferenceEquals(blocks, next)) return true;
        if (!TimelineMinimap.Entries(next).Equals(lastEntries)) return false;
        blocks = next;
        return true;
    }

    // :103-105
    public static bool InputsEqual(TimelineMinimapColumn lhs, TimelineMinimapColumn rhs) =>
        BlocksEqual(lhs.blocks, rhs.blocks) &&
        ReferenceEquals(lhs.tracking, rhs.tracking) &&
        lhs.centerHeight == rhs.centerHeight;

    // :103-105
    private static bool BlocksEqual(IReadOnlyList<DisplayBlock> lhs, IReadOnlyList<DisplayBlock> rhs)
    {
        if (ReferenceEquals(lhs, rhs)) return true;
        if (lhs.Count != rhs.Count) return false;
        for (var i = 0; i < lhs.Count; i++)
            if (!EqualityComparer<DisplayBlock>.Default.Equals(lhs[i], rhs[i])) return false;
        return true;
    }
}

/// <summary>:133-137 <c>private struct MinimapOutlineBlock: Equatable</c>.</summary>
internal sealed record MinimapOutlineBlock
{
    public required string Id { get; init; } // :135
    public required bool HasUserMessage { get; init; } // :136
}

/// <summary>:139-162 <c>private struct MinimapSelection: View</c>.</summary>
internal sealed class MinimapSelection : Decorator
{
    private OnChange<string?>? selectionChange;
    // :142-146, :148-156
    public MinimapSelection(
        IReadOnlyList<TimelineMinimapEntry> entries,
        IReadOnlyList<MinimapOutlineBlock> outline,
        TimelineScrollTracking tracking,
        double centerHeight,
        Action<string, bool> onNavigate)
    {
        string? SelectedID()
        {
            if (tracking.ActiveBlockID is { } active) return active;
            for (var i = outline.Count - 1; i >= 0; i--)
            {
                if (!outline[i].HasUserMessage) continue;
                return outline[i].Id;
            }
            return null;
        }

        var rail = new TimelineMinimapRail(entries, centerHeight, SelectedID(), onNavigate);
        Child = rail;
        // :148-161. SwiftUI re-evaluates the binding selection without replacing the ForEach tick identities.
        void ObserveSelection() => selectionChange ??= new OnChange<string?>(SelectedID, (_, selected) => rail.SetSelectedID(selected));
        ObserveSelection();
        AttachedToVisualTree += (_, _) =>
        {
            rail.SetSelectedID(SelectedID(), animated: false);
            ObserveSelection();
        };
        DetachedFromVisualTree += (_, _) => { selectionChange?.Dispose(); selectionChange = null; };
    }
}

/// <summary>:164-366 <c>struct TimelineMinimapRail: View, Equatable</c>.</summary>
public sealed class TimelineMinimapRail : Decorator
{
    private const double CardWidth = 300; // :342

    private readonly IReadOnlyList<TimelineMinimapEntry> entries;
    private readonly double centerHeight;
    private string? selectedID;
    private readonly Action<string, bool> onNavigate;
    private string? hoveredID;
    private bool isPressing;
    private Border? hitArea;
    private Grid? root;
    private MinimapRailMetrics? metrics;
    private StackPanel? tickStack;
    private readonly List<(TimelineMinimapEntry Entry, Button Tick, AnimatedColor Fill)> ticks = [];
    private IFrameSource? tickFrameSource;
    private Action<TimeSpan>? tickFrame;
    private Control? previewCard;
    private string? previewID;
    private IFrameSource? previewFrameSource;
    private Action<TimeSpan>? previewFrame;

    // :169-177, :201-240
    public TimelineMinimapRail(
        IReadOnlyList<TimelineMinimapEntry> entries,
        double centerHeight,
        string? selectedID,
        Action<string, bool> onNavigate)
    {
        this.entries = entries;
        this.centerHeight = centerHeight;
        this.selectedID = selectedID;
        this.onNavigate = onNavigate;

        AutomationProperties.SetName(this, "Conversation outline");
        BuildBody();

        DetachedFromVisualTree += (_, _) =>
        {
            hoveredID = null;
            isPressing = false;
            RefreshVisuals(animated: false);
        };
    }

    // :241-243. The binding may change the lit block while the rail itself retains its view identity.
    internal void SetSelectedID(string? id, bool animated = true)
    {
        if (OptionalStringEqual(selectedID, id)) return;
        selectedID = id;
        RefreshVisuals(animated);
    }

    // :193-199. Both environment values are inherited from the hosted window; an unattached rail has
    // no active window resize. Read IsActive, not IsReshaping (pane resizing is not this Swift condition).
    private Animation? HoverAnimation =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) ||
        GetValue(AppEnvironment.WindowLiveResizeProperty)?.IsActive == true ? null : Animation.Smooth(duration: 0.22);

    // :181-183
    public static bool InputsEqual(TimelineMinimapRail lhs, TimelineMinimapRail rhs) =>
        EntriesEqual(lhs.entries, rhs.entries) &&
        lhs.centerHeight == rhs.centerHeight &&
        OptionalStringEqual(lhs.selectedID, rhs.selectedID);

    // :181-183
    private static bool OptionalStringEqual(string? lhs, string? rhs) =>
        lhs is null ? rhs is null : rhs is not null && SwiftString.Equal(lhs, rhs);

    // :181-183
    private static bool EntriesEqual(IReadOnlyList<TimelineMinimapEntry> lhs, IReadOnlyList<TimelineMinimapEntry> rhs)
    {
        if (ReferenceEquals(lhs, rhs)) return true;
        if (lhs.Count != rhs.Count) return false;
        for (var i = 0; i < lhs.Count; i++)
        {
            var left = lhs[i];
            var right = rhs[i];
            if (!SwiftString.Equal(left.Id, right.Id) ||
                !SwiftString.Equal(left.Title, right.Title) ||
                !SwiftString.Equal(left.Snippet, right.Snippet) ||
                left.Weight != right.Weight)
                return false;
        }
        return true;
    }

    // :201-240
    private void BuildBody()
    {
        var localMetrics = new MinimapRailMetrics(centerHeight, entries.Count);
        var localRoot = new Grid();
        metrics = localMetrics;
        root = localRoot;

        var hitRect = localMetrics.HitRect(entries.Count, new Size(0, 0));
        var localHitArea = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Height = hitRect.Height,
            Margin = new Thickness(0, hitRect.Y, 0, 0),
        };
        localHitArea.PointerStyle(PointerStyle.Link); // :211
        hitArea = localHitArea;

        localHitArea.PointerEntered += (_, e) =>
            SetHovered(localMetrics.EntryID(e.GetPosition(localRoot).Y, entries));
        localHitArea.PointerMoved += (_, e) =>
        {
            var point = e.GetPosition(localRoot).Y;
            if (isPressing) UpdateScrub(point, ended: false);
            else SetHovered(localMetrics.EntryID(point, entries));
        };
        // :218-219 `case .ended: hoveredID = nil` - unconditional, no press-state guard.
        localHitArea.PointerExited += (_, _) => SetHovered(null);
        localHitArea.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(localHitArea).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            isPressing = true;
            RefreshVisuals();
            e.Pointer.Capture(localHitArea);
            UpdateScrub(e.GetPosition(localRoot).Y, ended: false);
            e.Handled = true;
        };
        localHitArea.PointerReleased += (_, e) =>
        {
            if (!isPressing || e.GetCurrentPoint(localHitArea).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
            isPressing = false;
            UpdateScrub(e.GetPosition(localRoot).Y, ended: true);
            e.Pointer.Capture(null);
            e.Handled = true;
            RefreshVisuals();
        };
        localHitArea.PointerCaptureLost += (_, _) =>
        {
            if (!isPressing) return;
            isPressing = false;
            RefreshVisuals();
        };

        localRoot.Children.Add(localHitArea);
        Child = localRoot;

        localRoot.SizeChanged += (_, e) => UpdateHitArea(e.NewSize);
        RefreshVisuals();
    }

    // :412-424
    private void UpdateHitArea(Size size)
    {
        if (hitArea is null || metrics is null) return;
        var updated = metrics.HitRect(entries.Count, size);
        hitArea.Height = updated.Height;
        hitArea.Margin = new Thickness(0, updated.Y, 0, 0);
        // :224-231,289-300. Geometry changes re-clamp the mounted card without replacing the ticks.
        if (previewCard is { } card && hoveredID is { } id && FindEntry(id) is { } entry)
        {
            CancelPreviewFrame();
            var offset = (TranslateTransform)card.RenderTransform!;
            offset.X = 0;
            offset.Y = PreviewY(entry, metrics, size.Height);
            card.Opacity = 1;
        }
    }

    // :245-254,289-330
    private void RefreshVisuals(bool animated = true)
    {
        if (root is null || metrics is null) return;

        // :245-254 ForEach keeps the same ticks through hover/press/selection.
        if (tickStack is null)
        {
            tickStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = metrics.Spacing,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, metrics.TopInset, 0, 0),
                IsHitTestVisible = false,
            };
            foreach (var entry in entries) tickStack.Children.Add(CreateTick(entry));
            root.Children.Add(tickStack);
        }
        else
        {
            Transaction.WithAnimation(animated ? HoverAnimation : null, UpdateTicks); // :241-243
        }

        Transaction.WithAnimation(animated ? HoverAnimation : null, UpdatePreview); // :230,238-243

        IsHitTestVisible = true;
    }

    // :224-240,289-330. Preview insertion/removal combines opacity and x=-6; an existing preview glides in y.
    // Both scalar and color animations share the hover transaction. Interruption begins at displayed values.
    private void UpdatePreview()
    {
        CancelPreviewFrame();
        if (root is null || metrics is null) return;
        var animation = Transaction.Current;
        var entry = hoveredID is { } id ? FindEntry(id) : null;
        var previous = previewCard;
        Panel? crossfadeHost = null;
        Control? fadingOut = null;
        Control? fadingIn = null;
        double fadingOutFrom = 1;
        if (entry is not null && previous is not null && !OptionalStringEqual(previewID, entry.Id) && (previous as Border)?.Child is Panel host)
        {
            // :303-318 the same preview stays mounted: the title and the snippet change under .contentTransition(.opacity), so
            // the old text fades out while the new one fades in, inside the hover animation.
            while (host.Children.Count > 1) host.Children.RemoveAt(0); // an earlier crossfade cut short: only the latest text remains
            fadingOut = host.Children[0];
            fadingOutFrom = fadingOut.Opacity;
            fadingIn = CreatePreviewContent(entry);
            fadingIn.Opacity = animation is null ? 1 : 0;
            host.Children.Add(fadingIn);
            crossfadeHost = host;
            previewID = entry.Id;
            if (animation is null)
            {
                host.Children.Remove(fadingOut);
                fadingOut = null;
                crossfadeHost = null;
            }
        }
        else if (entry is not null && (previous is null || !OptionalStringEqual(previewID, entry.Id)))
        {
            var card = CreatePreviewCard(entry, metrics);
            var offset = (TranslateTransform)card.RenderTransform!;
            if (previous is not null)
            {
                var oldOffset = (TranslateTransform)previous.RenderTransform!;
                card.Opacity = previous.Opacity;
                offset.X = oldOffset.X;
                offset.Y = oldOffset.Y;
                root.Children.Remove(previous);
            }
            else if (animation is not null)
            {
                card.Opacity = 0;
                offset.X = EnvironmentValues.GetAccessibilityReduceMotion(this) ? 0 : -6;
            }
            previewCard = card;
            previewID = entry.Id;
            root.Children.Add(card);
        }
        if (previewCard is not { } preview) return;
        var transform = (TranslateTransform)preview.RenderTransform!;
        var opacityFrom = preview.Opacity;
        var xFrom = transform.X;
        var yFrom = transform.Y;
        var opacityTo = entry is null ? 0 : 1;
        var xTo = entry is null && !EnvironmentValues.GetAccessibilityReduceMotion(this) ? -6 : 0;
        var yTo = entry is null ? yFrom : PreviewY(entry, metrics, Bounds.Height);
        void Place(double progress)
        {
            if (fadingOut is not null && fadingIn is not null)
            {
                fadingOut.Opacity = fadingOutFrom * (1 - progress);
                fadingIn.Opacity = progress;
                if (progress == 1) crossfadeHost!.Children.Remove(fadingOut);
            }
            preview.Opacity = opacityFrom + (opacityTo - opacityFrom) * progress;
            transform.X = xFrom + (xTo - xFrom) * progress;
            transform.Y = yFrom + (yTo - yFrom) * progress;
            if (progress == 1 && entry is null)
            {
                root.Children.Remove(preview);
                previewCard = null;
                previewID = null;
            }
        }
        if (animation is null || (opacityFrom == opacityTo && xFrom == xTo && yFrom == yTo && fadingOut is null))
        {
            Place(1);
            return;
        }
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        var span = TimeSpan.FromSeconds(animation.Duration);
        Action<TimeSpan>? step = null;
        step = _ =>
        {
            var elapsed = SwiftClock.Now - start;
            var complete = elapsed >= span;
            Place(complete ? 1 : animation.Progress(elapsed.TotalSeconds));
            if (complete) previewFrame = null;
            else frames.RequestFrame(step!);
        };
        previewFrameSource = frames;
        previewFrame = step;
        frames.RequestFrame(step);
    }

    private void CancelPreviewFrame()
    {
        if (previewFrame is { } previous) previewFrameSource?.CancelFrame(previous);
        previewFrame = null;
    }

    // :215-217, :284-287
    private void SetHovered(string? id)
    {
        if (OptionalStringEqual(hoveredID, id)) return;
        hoveredID = id;
        RefreshVisuals();
    }

    // :245-274. First mount is immediate; subsequent changes use the transaction and retain this button/brush.
    private Control CreateTick(TimelineMinimapEntry entry)
    {
        var isSelected = selectedID is { } selected && SwiftString.Equal(entry.Id, selected);
        var isHovered = hoveredID is { } hovered && SwiftString.Equal(entry.Id, hovered);

        var tick = new Button
        {
            Width = TickWidth(entry, isHovered, isSelected),
            MinWidth = 0,
            Height = MinimapRailMetrics.TickHeight,
            MinHeight = 0,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, // :241 VStack(alignment: .leading): every tick starts at the same edge, 8 in; stretched, a fixed-width tick centres in the widest one's room
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(1),
            BorderThickness = new Thickness(0),
            Opacity = isHovered ? 0.95 : isSelected && !isHovered ? 1 : (hoveredID is not null || isPressing) ? 0.4 : 0.25,
            IsHitTestVisible = false,
            Focusable = false,
        };
        var fill = new AnimatedColor(tick);
        fill.Set(isSelected && !isHovered ? Chrome.Accent : Chrome.PrimaryText, null);
        tick.Background = fill.Brush;
        ticks.Add((entry, tick, fill));
        tick.Click += (_, _) => onNavigate(entry.Id, true);
        AutomationProperties.SetName(tick, entry.Title);
        AutomationProperties.SetAccessibilityView(tick, AccessibilityView.Content);
        return tick;
    }

    // :195-199,241-274. Frame-driven scalar interpolation uses the same transaction as AnimatedColor.
    // At rest there is no pending frame; interrupted changes start at the current displayed values.
    private void UpdateTicks()
    {
        if (tickFrame is { } previous) tickFrameSource?.CancelFrame(previous);
        tickFrame = null;
        var animation = Transaction.Current;
        var changes = new List<(Button Tick, double WidthFrom, double WidthTo, double OpacityFrom, double OpacityTo)>();
        foreach (var (entry, tick, fill) in ticks)
        {
            var selected = selectedID is { } selectedValue && SwiftString.Equal(entry.Id, selectedValue);
            var hovered = hoveredID is { } hoveredValue && SwiftString.Equal(entry.Id, hoveredValue);
            var width = TickWidth(entry, hovered, selected);
            var opacity = hovered ? 0.95 : selected ? 1 : (hoveredID is not null || isPressing) ? 0.4 : 0.25;
            fill.Set(selected && !hovered ? Chrome.Accent : Chrome.PrimaryText, animation);
            if (animation is null)
            {
                tick.Width = width;
                tick.Opacity = opacity;
            }
            else if (tick.Width != width || tick.Opacity != opacity)
                changes.Add((tick, tick.Width, width, tick.Opacity, opacity));
        }
        if (animation is null || changes.Count == 0) return;
        var start = SwiftClock.Now;
        var span = TimeSpan.FromSeconds(animation.Duration);
        var frames = FrameClock.Source.Current;
        Action<TimeSpan>? step = null;
        step = _ =>
        {
            var elapsed = SwiftClock.Now - start;
            var complete = elapsed >= span;
            var progress = complete ? 1 : animation.Progress(elapsed.TotalSeconds);
            foreach (var change in changes)
            {
                change.Tick.Width = change.WidthFrom + (change.WidthTo - change.WidthFrom) * progress;
                change.Tick.Opacity = change.OpacityFrom + (change.OpacityTo - change.OpacityFrom) * progress;
            }
            if (complete) tickFrame = null;
            else frames.RequestFrame(step!);
        };
        tickFrameSource = frames;
        tickFrame = step;
        frames.RequestFrame(step);
    }

    // :276-282
    private double TickWidth(TimelineMinimapEntry entry, bool isHovered, bool isSelected)
    {
        // 10 pt narrowest, 26 pt widest. Hovered and selected ticks punch
        // out to full width so the eye finds them while scrubbing.
        var baseWidth = 10 + entry.Weight * 12;
        if (isHovered || isSelected) return 26;
        return hoveredID is not null || isPressing ? Math.Min(26, baseWidth + 2) : baseWidth;
    }

    // :284-287
    private TimelineMinimapEntry? FindEntry(string id)
    {
        foreach (var entry in entries)
            if (SwiftString.Equal(entry.Id, id)) return entry;
        return null;
    }

    // :289-330
    private double PreviewY(TimelineMinimapEntry entry, MinimapRailMetrics metrics, double railHeight)
    {
        var tickCenterY = metrics.CenterY(IndexOf(entry));
        var half = EstimatedCardHeight(entry) / 2;
        var top = half + 2;
        var bottom = Math.Max(top, railHeight - half - 2);
        return Math.Min(Math.Max(tickCenterY, top), bottom) - railHeight / 2;
    }

    // :289-330
    private Control CreatePreviewCard(TimelineMinimapEntry entry, MinimapRailMetrics metrics)
    {
        var y = PreviewY(entry, metrics, Bounds.Height);

        var content = CreatePreviewContent(entry);

        var card = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(12, 10),
            Child = new Panel { Children = { content } }, // the host the title and snippet crossfade in
        }.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(card, GlassEffect.Regular(GlassShape.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous))); // :324
        card.Width = CardWidth;
        card.Margin = new Thickness(34, 0, 0, 0);
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.IsHitTestVisible = false;
        card.RenderTransform = new TranslateTransform(0, y);
        AutomationProperties.SetAccessibilityView(card, AccessibilityView.Raw);
        return card;
    }

    // :303-318 the card's title and snippet.
    private static Control CreatePreviewContent(TimelineMinimapEntry entry)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
        var title = new TextBlock
        {
            Text = entry.Title,
            MaxLines = 1,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(title);
        title.Foreground = Chrome.PrimaryText.Brush(title);
        content.Children.Add(title);

        if (entry.Snippet.Length != 0)
        {
            var snippet = new TextBlock
            {
                Text = entry.Snippet,
                MaxLines = 3,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(snippet);
            snippet.Foreground = Chrome.SecondaryText.Brush(snippet);
            content.Children.Add(snippet);
        }

        return content;
    }

    // :333-340
    private double EstimatedCardHeight(TimelineMinimapEntry entry)
    {
        const double title = 17;
        const double padding = 20;
        if (entry.Snippet.Length == 0) return title + padding;

        var glyphsPerLine = Math.Max(1, ((int)CardWidth - 24) / 7);
        var count = SwiftString.Count(entry.Snippet);
        var lines = Math.Min(3, Math.Max(1, (count + glyphsPerLine - 1) / glyphsPerLine));
        return title + 3 + lines * 15 + padding;
    }

    // :344-346
    private int IndexOf(TimelineMinimapEntry entry)
    {
        for (var i = 0; i < entries.Count; i++)
            if (EqualityComparer<TimelineMinimapEntry>.Default.Equals(entries[i], entry)) return i;
        return 0;
    }

    // :348-365
    private void UpdateScrub(double y, bool ended)
    {
        var id = new MinimapRailMetrics(centerHeight, entries.Count).EntryID(y, entries);
        if (id is null) return;
        if (!SwiftString.Equal(hoveredID ?? "", id))
        {
            SetHovered(id);
            if (!ended) onNavigate(id, false);
        }
        if (ended) onNavigate(id, true);
    }
}

/// <summary>:368-425 <c>private struct MinimapRailMetrics</c>: pure rail geometry.</summary>
internal sealed record MinimapRailMetrics
{
    public const double TickHeight = 2; // :374
    private const double RoomySpacing = 8; // :375
    private const double TightestSpacing = 2; // :376
    private double Pitch => TickHeight + Spacing; // :399

    public double Spacing { get; init; } // :378
    public double TopInset { get; init; } // :380

    // :382-397
    public MinimapRailMetrics(double centeringHeight, long count)
    {
        if (count <= 0 || centeringHeight <= 0)
        {
            Spacing = RoomySpacing;
            TopInset = 0;
            return;
        }

        var ticks = count * TickHeight;
        var gaps = Math.Max(0, count - 1);
        Spacing = ticks + gaps * RoomySpacing <= centeringHeight
            ? RoomySpacing
            : Math.Max(TightestSpacing, (centeringHeight - ticks) / Math.Max(1, gaps));
        TopInset = (centeringHeight - (ticks + gaps * Spacing)) / 2;
    }

    // :401-403
    public double CenterY(long @for) => TopInset + @for * Pitch + TickHeight / 2;

    // :405-410
    public string? EntryID(double at, IReadOnlyList<TimelineMinimapEntry> entries)
    {
        if (entries.Count == 0) return null;
        var raw = (at - TopInset - TickHeight / 2 + Pitch / 2) / Pitch;
        var index = (int)Math.Floor(raw);
        index = Math.Clamp(index, 0, entries.Count - 1);
        return entries[index].Id;
    }

    // :412-424
    private static double ChromeClearance => Chrome.ChromeTopPadding + Chrome.CapsuleHeight;

    public Rect HitRect(long count, Size railSize)
    {
        if (count <= 0) return new Rect(0, 0, 0, 0);
        var top = Math.Max(CenterY(0) - Pitch / 2, ChromeClearance);
        var bottom = Math.Min(CenterY(count - 1) + Pitch / 2, railSize.Height);
        return bottom > top
            ? new Rect(0, top, railSize.Width, bottom - top)
            : new Rect(0, 0, 0, 0);
    }
}

/// <summary>:427-435 <c>private struct RailHitArea: Shape</c>.</summary>
internal sealed record RailHitArea(Rect Rect)
{
    // :432-434
    public Geometry Path(Rect @in) => new RectangleGeometry(Rect);
}
