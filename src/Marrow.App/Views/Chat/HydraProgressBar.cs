using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Runtime;
using Marrow.App.Views.Tour;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/HydraProgressBar.swift (B3 52bc792), :4-603.

namespace Marrow.App.Views.Chat;

/// <summary>:4-447 <c>struct HydraProgressBar: View</c>: how far a head has got, as one bar of staves.</summary>
public sealed class HydraProgressBar : Decorator
{
    private const double HydraProgressBarHeight = 28; // :43
    private const double StaveWidth = 3; // :44
    private const double Gap = 3; // :45
    private const double Pitch = StaveWidth + Gap; // :46
    private const double Rise = 0.45; // :48
    private const double Sweep = 2.6; // :50
    private const double SweepRest = 3.4; // :52
    private const double CardWidth = 260; // :53
    public const long SweepSlots = 3; // :59

    private readonly ThreadRuntime runtime;
    private DateTimeOffset startedAt;      // :21, handed again by the view that holds the bar (UpdateTimes)
    private DateTimeOffset? finishedAt;    // :22
    private HydraHeadInfo.Status status    // :23, read by the clock's schedule, so it is observed
    {
        get => motion.Status;
        set => motion.Status = value;
    }
    private WorkItem? settleTask;
    private readonly SwiftColor tint;
    private IReadOnlyList<TimelineEntry>? suppliedEntries;
    private readonly StaveCache cache = new();
    private readonly BarDrawing bar;
    private readonly StackPanel content;
    private readonly TextBlock countsText;
    private readonly Border card;
    private readonly TextBlock cardLabel;
    private Control cardMark;
    private readonly Grid overlay;
    private double width;
    private int? hoveredIndex;
    private DateTimeOffset drawDate = SwiftDate.Now;
    private readonly MotionState motion;
    private WorkItem? riseTask;
    private long taskEventCount = -1;
    private bool attached;
    private readonly AnimatedDouble cardOpacity;
    private readonly AnimatedDouble cardX;
    private WorkItem? hideCard;
    private bool cardShown;
    private double? cardTargetX;

    private sealed class MotionState(HydraHeadInfo.Status status) : MainActorModel
    {
        private bool rising;
        private bool settled;
        private HydraHeadInfo.Status status = status; // seeded, not set: a bar may be made before it reaches the main actor's tree
        public bool Rising { get => Read(rising); set => Set(ref rising, value); }
        public bool Settled { get => Read(settled); set => Set(ref settled, value); }
        public HydraHeadInfo.Status Status { get => Read(status); set => Set(ref status, value); }
    }

    /// <summary>The entries the bar draws its staves from, handed down again by the view that holds it (ThreadTimeline.swift:2139 <c>entries: turnEntries</c>):
    /// a view whose inputs changed keeps its state, so the bar goes on from where it was instead of starting again.</summary>
    internal void UpdateEntries(IReadOnlyList<TimelineEntry>? entries) => suppliedEntries = entries;

    /// <summary>The times and the status handed again by the view that holds the bar (ThreadTimeline.swift:2139 `startedAt`, `finishedAt`, `status: isRunning ? .running :
    /// .stopped`: a running line that becomes an earlier segment's): the same bar, its staves and its rise kept, with what the status decides (:129-134 the sweep and its clock,
    /// :186-191 `.task(id: isRunning ? -1 : events.count)` unsettled and then settled once the last stave has risen, :192-198 `.task(id: isRunning)` the sweep slot joined or
    /// given back, :204 the label). ED-255 (5).</summary>
    internal void UpdateTimes(DateTimeOffset nextStartedAt, DateTimeOffset? nextFinishedAt, HydraHeadInfo.Status nextStatus)
    {
        if (nextStartedAt == startedAt && nextFinishedAt == finishedAt && nextStatus == status) return;
        var wasRunning = status == HydraHeadInfo.Status.Running;
        startedAt = nextStartedAt; finishedAt = nextFinishedAt; status = nextStatus;
        var isRunning = status == HydraHeadInfo.Status.Running;
        if (attached && isRunning != wasRunning)
        {
            var id = (object)runtime;
            if (isRunning) SweepBudget.Shared.Join(id); else SweepBudget.Shared.Leave(id); // :192-198
            settleTask?.Cancel(); settleTask = null;
            motion.Settled = false; // :187
            if (!isRunning)
                settleTask = MainActor.AsyncAfter(TimeSpan.FromSeconds(Rise + 0.1), () => { if (attached && status != HydraHeadInfo.Status.Running) motion.Settled = true; }); // :188-190
        }
        Refresh();
    }

    // :20-27, :122-209
    public HydraProgressBar(
        ThreadRuntime runtime,
        DateTimeOffset startedAt,
        DateTimeOffset? finishedAt,
        HydraHeadInfo.Status status,
        SwiftColor tint,
        IReadOnlyList<TimelineEntry>? entries = null)
    {
        this.runtime = runtime;
        this.startedAt = startedAt;
        this.finishedAt = finishedAt;
        motion = new MotionState(status); // the bar's status (:23), observed by its clock's schedule
        this.tint = tint;
        suppliedEntries = entries;

        bar = new BarDrawing
        {
            Height = HydraProgressBarHeight,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        countsText = new TextBlock
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            Foreground = Chrome.SecondaryText.Brush(this),
        };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(countsText);

        cardLabel = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Foreground = Chrome.PrimaryText.Brush(this),
        };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(cardLabel);

        cardMark = new Border { Width = 14, Height = 14 };
        card = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(Chrome.CardCornerRadius),
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children = { cardMark, cardLabel },
            },
            IsHitTestVisible = false,
            IsVisible = false,
        }.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous); // :572 `shape`; one line tall the card is 37 pt, in ED-275 (2)'s band (OD-0102)
        cardOpacity = new AnimatedDouble(value => card.Opacity = value);
        cardOpacity.Set(0, null);
        cardX = new AnimatedDouble(value => card.Margin = new Thickness(value, 0, 0, 0));
        void Surface() => new ProgressCardSurface(RenderBudget.GetIsOnGlassPanel(this), ActualThemeVariant == ThemeVariant.Dark).Apply(card);
        ActualThemeVariantChanged += (_, _) => Surface();
        PropertyChanged += (_, e) => { if (e.Property == RenderBudget.IsOnGlassPanelProperty) Surface(); };
        Surface();

        overlay = new Grid();
        overlay.Children.Add(bar);
        overlay.Children.Add(card);

        content = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 6,
            Children = { overlay },
        };
        Child = content;

        bar.SizeChanged += (_, _) =>
        {
            width = bar.Bounds.Width;
            Refresh();
        };
        card.SizeChanged += (_, _) => PositionCard();
        bar.PointerMoved += OnPointerMoved;
        bar.PointerExited += OnPointerExited;

        var id = (object)runtime;
        AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            taskEventCount = -1;
            if (this.status == HydraHeadInfo.Status.Running) // the status as it is now (UpdateTimes), not the one the bar was made with
                SweepBudget.Shared.Join(id);
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            riseTask?.Cancel();
            settleTask?.Cancel(); settleTask = null;
            hideCard?.Cancel();
            cardOpacity.Set(0, null);
            cardX.Set(0, null);
            cardTargetX = null;
            cardShown = false;
            card.IsVisible = false;
            SweepBudget.Shared.Leave(id);
        };

        bar.TimelineView(
            () =>
            {
                var stavesNow = Resolve().Staves;
                var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
                var isRising = motion.Rising;
                var isSweeping = this.status == HydraHeadInfo.Status.Running && // observed: a status handed again (UpdateTimes) reschedules the clock
                    !reduceMotion &&
                    stavesNow.Count > 0 &&
                    SweepBudget.Shared.Sweeps(id);

                return new AnimationTimelineSchedule(
                    1.0 / 24.0,
                    reduceMotion ||
                    motion.Settled ||
                    ScrollActivity.Shared.Current.IsScrolling ||
                    (!isSweeping && !isRising));
            },
            date =>
            {
                drawDate = date;
                Refresh();
            });

        this.Track(
            () => runtime.Entries,
            _ => Refresh());
        this.Track(
            () => SweepBudget.Shared.Sweepers,
            _ => Refresh());

        Refresh();
    }

    // :61-120
    private sealed class StaveCache
    {
        private sealed record Key
        {
            public required object Id { get; init; } // :67
            public ToolCall.Status? Status { get; init; } // :68
            public string? Title { get; init; } // :71
        }

        private Key[] key = [];
        private long capacity;
        private ValueList<Stave> events = ValueList<Stave>.Empty;
        private ValueList<Stave> staves = ValueList<Stave>.Empty;
        private Counts counts = new(Array.Empty<Stave>());

        // :80-97
        public (ValueList<Stave> Events, ValueList<Stave> Staves, Counts Counts) Resolve(
            IReadOnlyList<TimelineEntry> entries,
            long capacity)
        {
            if (capacity == this.capacity && Matches(entries))
                return (events, staves, counts);

            key = entries.Select(KeyOf).ToArray();
            this.capacity = capacity;
            events = EventsIn(entries);
            staves = StavesFor(events, capacity);
            counts = new Counts(events);
            return (events, staves, counts);
        }

        // :99-112
        private bool Matches(IReadOnlyList<TimelineEntry> entries)
        {
            if (entries.Count != key.Length) return false;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var cached = key[index];
                if (!ReferenceEquals(entry, cached.Id)) return false;
                if (entry.TimelineEntryKind == TimelineEntry.Kind.Tool &&
                    entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool &&
                    (tool.Value.ToolCallStatus != cached.Status || tool.Value.Title != cached.Title))
                    return false;
            }
            return true;
        }

        // :114-119
        private static Key KeyOf(TimelineEntry entry)
        {
            if (entry.TimelineEntryKind == TimelineEntry.Kind.Tool &&
                entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool)
                return new Key { Id = entry, Status = tool.Value.ToolCallStatus, Title = tool.Value.Title };
            return new Key { Id = entry };
        }
    }

    // :247-276
    private sealed record Stave
    {
        public enum Kind
        {
            Lookup, // :251
            Command, // :252
            Edit, // :253
            Reply, // :254
        }

        public required Kind StaveKind { get; init; } // :257
        public required DateTimeOffset ArrivedAt { get; init; } // :258
        public required string Step { get; init; } // :260
        public MCPCatalogEntry? Mcp { get; init; } // :264
        public string? Symbol { get; init; } // :265
        public ToolCall? Call { get; init; } // :312-337

        // :268-275
        public double Height => StaveKind switch
        {
            Kind.Lookup => 0.45,
            Kind.Command => 0.65,
            Kind.Edit => 0.85,
            Kind.Reply => 1,
            _ => throw new InvalidOperationException(),
        };
    }

    // :278-309
    private sealed record Counts
    {
        public long Edits { get; init; } = 0; // :280
        public long Commands { get; init; } = 0; // :281
        public long Reads { get; init; } = 0; // :282

        // :284-293
        public Counts(IReadOnlyList<Stave> of)
        {
            long edits = 0;
            long commands = 0;
            long reads = 0;
            foreach (var item in of)
            {
                switch (item.StaveKind)
                {
                    case Stave.Kind.Edit: edits++; break;
                    case Stave.Kind.Command: commands++; break;
                    case Stave.Kind.Lookup: reads++; break;
                    case Stave.Kind.Reply: break;
                    default: throw new InvalidOperationException();
                }
            }

            Edits = edits;
            Commands = commands;
            Reads = reads;
        }

        // :295-304
        private IReadOnlyList<string> Parts
        {
            get
            {
                var parts = new List<string>();
                if (Edits > 0) parts.Add($"{Edits.ToString(CultureInfo.InvariantCulture)} {(Edits == 1 ? "edit" : "edits")}");
                if (Commands > 0) parts.Add($"{Commands.ToString(CultureInfo.InvariantCulture)} {(Commands == 1 ? "command" : "commands")}");
                if (Reads > 0) parts.Add($"{Reads.ToString(CultureInfo.InvariantCulture)} {(Reads == 1 ? "read" : "reads")}");
                return parts;
            }
        }

        // :304
        public string Line => string.Join(" \U000000B7 ", Parts); // U+00B7 MIDDLE DOT

        // :306-308
        public string Sentence => Parts.Count == 0 ? "Nothing done yet" : string.Join(", ", Parts);
    }

    // :312-337
    private static ValueList<Stave> EventsIn(IReadOnlyList<TimelineEntry> entries)
    {
        var result = new List<Stave>();
        foreach (var entry in entries)
        {
            switch (entry.TimelineEntryKind)
            {
                case TimelineEntry.Kind.Tool:
                    if (entry.Item.TimelineItemContent is not TimelineItem.Content.Tool toolContent) break;
                    var call = toolContent.Value;
                    var kind = call.ToolCallKind switch
                    {
                        ToolCall.Kind.Edit => Stave.Kind.Edit,
                        ToolCall.Kind.Command => call.Edits.Count == 0 ? Stave.Kind.Command : Stave.Kind.Edit,
                        _ => call.Edits.Count == 0 ? Stave.Kind.Lookup : Stave.Kind.Edit,
                    };
                    var mcp = ToolPresentation.McpParts(call)?.Entry;
                    result.Add(new Stave
                    {
                        StaveKind = kind,
                        ArrivedAt = entry.Item.Date,
                        Step = ToolPresentation.Label(call),
                        Call = call,
                        Mcp = mcp,
                        Symbol = mcp is null ? ToolPresentation.Symbol(call.ToolCallKind) : null,
                    });
                    break;

                case TimelineEntry.Kind.Assistant:
                    if (entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant assistant) break;
                    var words = TextCleanup.SingleLine(SwiftString.Prefix(assistant.Value.Text, 600), 60);
                    result.Add(new Stave
                    {
                        StaveKind = Stave.Kind.Reply,
                        ArrivedAt = entry.Item.Date,
                        Step = words.Length == 0 ? "Replied" : $"Replied: {words}",
                        Symbol = "bubble.left",
                    });
                    break;
            }
        }

        return new ValueList<Stave>(result);
    }

    // :341-343
    private static ValueList<Stave> StavesFor(IReadOnlyList<Stave> events, long capacity) =>
        new(events.Count > capacity ? events.Skip(events.Count - (int)capacity) : events);

    // :347-353
    private static SwiftColor ColorFor(Stave.Kind kind, SwiftColor tint) => kind switch
    {
        Stave.Kind.Edit or Stave.Kind.Reply => tint,
        Stave.Kind.Command => tint.Opacity(0.7),
        Stave.Kind.Lookup => tint.Opacity(0.4),
        _ => throw new InvalidOperationException(),
    };

    // :122-124
    private (ValueList<Stave> Events, ValueList<Stave> Staves, Counts Counts) Resolve()
    {
        var capacity = Math.Max(1L, (long)Math.Floor((width + Gap) / Pitch));
        return cache.Resolve(suppliedEntries ?? runtime.Entries, capacity);
    }

    // :122-209
    private void Refresh()
    {
        var capacity = Math.Max(1L, (long)Math.Floor((width + Gap) / Pitch));
        var resolved = cache.Resolve(suppliedEntries ?? runtime.Entries, capacity);
        // :186-198 The source tasks are keyed by event count, not by current stave age.
        // Their state writes cause the animation schedule to pause even if entries never change again.
        if (attached && taskEventCount != resolved.Events.Count)
        {
            taskEventCount = resolved.Events.Count;
            riseTask?.Cancel();
            motion.Settled = false;
            motion.Rising = resolved.Events.Count > 0 && !EnvironmentValues.GetAccessibilityReduceMotion(this);
            riseTask = MainActor.AsyncAfter(TimeSpan.FromSeconds(Rise + 0.1), () =>
            {
                if (!attached) return;
                motion.Rising = false;
                motion.Settled = status != HydraHeadInfo.Status.Running;
                drawDate = SwiftDate.Now;
                Refresh();
            });
        }
        countsText.Text = resolved.Counts.Line;
        if (resolved.Counts.Line.Length == 0)
        {
            if (content.Children.Contains(countsText))
                content.Children.Remove(countsText);
        }
        else
        {
            if (!content.Children.Contains(countsText))
                content.Children.Add(countsText);
        }

        var id = (object)runtime;
        var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
        var sweeping = status == HydraHeadInfo.Status.Running &&
            !reduceMotion &&
            resolved.Staves.Count > 0 &&
            SweepBudget.Shared.Sweeps(id);
        bar.Update(
            resolved.Staves,
            capacity,
            drawDate,
            status,
            tint,
            hoveredIndex,
            !reduceMotion,
            sweeping);

        if (hoveredIndex is { } index && index < resolved.Staves.Count)
        {
            var stave = resolved.Staves[index];
            cardLabel.Text = stave.Step;
            cardMark = MakeMark(stave);
            if (card.Child is Panel oldCard) oldCard.Children.Clear();
            card.Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children = { cardMark, cardLabel },
            };
            ShowCard(true);
            card.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            card.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            PositionCard(index);
        }
        else
        {
            ShowCard(false);
        }

        AutomationProperties.SetName(
            this,
            $"{resolved.Counts.Sentence}, {RelativeTime.Duration((status == HydraHeadInfo.Status.Running ? SwiftDate.Now : finishedAt ?? SwiftDate.Now) - startedAt)}{(status == HydraHeadInfo.Status.Running ? " so far" : "")}");
    }

    // :177,242 The hover overlay has a smooth 0.15s opacity transition in both directions.
    private void ShowCard(bool show)
    {
        if (show == cardShown) return;
        cardShown = show;
        hideCard?.Cancel();
        if (show) card.IsVisible = true;
        var fade = Animation.Smooth(0.15);
        cardOpacity.Set(show ? 1 : 0, fade);
        if (!show) hideCard = MainActor.AsyncAfter(TimeSpan.FromSeconds(fade.Duration), () =>
        {
            if (!cardShown) card.IsVisible = false;
        });
    }

    // :213-243
    private Control MakeMark(Stave stave)
    {
        if (stave.Mcp is { } entry) return new MCPGlyph(entry, 14);
        if (stave.Symbol is { } symbol)
        {
            var image = SwiftImage.SystemName(symbol).Font(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold));
            image.ForegroundStyle(Chrome.SecondaryText);
            image.Width = 14;
            image.Height = 14;
            return image;
        }
        return new Border { Width = 14 };
    }

    // :213-243
    private void PositionCard(int? index = null)
    {
        var staveIndex = index ?? hoveredIndex;
        if (staveIndex is not { } value) return;
        var centre = value * Pitch + StaveWidth / 2;
        var x = Math.Min(Math.Max(0, centre - CardWidth / 2), Math.Max(0, width - CardWidth));
        if (cardTargetX != x)
        {
            cardTargetX = x;
            cardX.Set(x, Animation.Smooth(0.15));
        }
        card.RenderTransform = new TranslateTransform(0, -card.DesiredSize.Height - 8);
    }

    // :162-170
    private void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        var point = args.GetPosition(bar);
        int? index = point.X >= 0 && point.Y >= 0 && point.Y <= HydraProgressBarHeight
            ? (int)Math.Floor(point.X / Pitch)
            : null;
        var capacity = Math.Max(1L, (long)Math.Floor((width + Gap) / Pitch));
        var count = cache.Resolve(suppliedEntries ?? runtime.Entries, capacity).Staves.Count;
        int? picked = index is { } value && value < count ? value : null;
        if (picked == hoveredIndex) return;
        hoveredIndex = picked;
        Refresh();
    }

    // :167-170
    private void OnPointerExited(object? sender, PointerEventArgs args)
    {
        if (hoveredIndex is null) return;
        hoveredIndex = null;
        Refresh();
    }

    // :357-446
    private sealed class BarDrawing : Control
    {
        private IReadOnlyList<Stave> staves = Array.Empty<Stave>();
        private long capacity;
        private DateTimeOffset date = SwiftDate.Now;
        private HydraHeadInfo.Status status;
        private SwiftColor tint = SwiftColor.White;
        private int? hovered;
        private bool rising;
        private bool sweeping;

        // :357-367, :369-446
        public void Update(
            IReadOnlyList<Stave> staves,
            long capacity,
            DateTimeOffset date,
            HydraHeadInfo.Status status,
            SwiftColor tint,
            int? hovered,
            bool rising,
            bool sweeping)
        {
            this.staves = staves;
            this.capacity = capacity;
            this.date = date;
            this.status = status;
            this.tint = tint;
            this.hovered = hovered;
            this.rising = rising;
            this.sweeping = sweeping;
            Invalidations++;
            InvalidateVisual();
        }

        /// <summary>ED-NEW-lean-8 (tests): how many updates have repainted the bar.</summary>
        internal int Invalidations { get; private set; }

        // :357-446
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var bottom = Bounds.Height;
            var filled = new GeometryGroup();
            var byKind = new Dictionary<Stave.Kind, GeometryGroup>();
            Geometry? ending = null;
            Geometry? hover = null;

            for (var index = 0; index < staves.Count; index++)
            {
                var stave = staves[index];
                var factor = 1.0;
                if (rising)
                {
                    var age = (date - stave.ArrivedAt).TotalSeconds;
                    if (age < Rise)
                    {
                        var t = Math.Max(0, age / Rise);
                        factor = 1 - Math.Pow(1 - t, 3);
                    }
                }

                var height = bottom * stave.Height * factor;
                if (height <= 0) continue;
                var rect = new Rect(index * Pitch, bottom - height, StaveWidth, height);
                var shape = new RectangleGeometry { Rect = rect, RadiusX = StaveWidth / 2, RadiusY = StaveWidth / 2 };
                filled.Children.Add(shape);

                if (index == staves.Count - 1 && status == HydraHeadInfo.Status.Failed)
                    ending = shape;
                else if (index == staves.Count - 1 && status == HydraHeadInfo.Status.Stopped)
                    ending = shape;
                else
                {
                    if (!byKind.TryGetValue(stave.StaveKind, out var group))
                        byKind[stave.StaveKind] = group = new GeometryGroup();
                    group.Children.Add(shape);
                }

                if (index == hovered) hover = shape;
            }

            foreach (var kind in new[] { Stave.Kind.Lookup, Stave.Kind.Command, Stave.Kind.Edit, Stave.Kind.Reply })
                if (byKind.TryGetValue(kind, out var paths))
                    context.DrawGeometry(ColorFor(kind, tint).Brush(this), null, paths);

            if (ending is not null)
            {
                var color = status == HydraHeadInfo.Status.Failed ? Chrome.Danger : Chrome.SecondaryText;
                context.DrawGeometry(color.Brush(this), null, ending);
            }

            if (hover is not null)
                context.DrawGeometry(SwiftColor.White.Opacity(0.4).Brush(this), null, hover);

            if (staves.Count < capacity)
            {
                var slots = new GeometryGroup();
                for (var slot = staves.Count; slot < capacity; slot++)
                    slots.Children.Add(new RectangleGeometry
                    {
                        Rect = new Rect(slot * Pitch, bottom - bottom * 0.45, StaveWidth, bottom * 0.45),
                        RadiusX = StaveWidth / 2,
                        RadiusY = StaveWidth / 2,
                    });
                context.DrawGeometry(Chrome.Overlay(0.06).Brush(this), null, slots);
            }

            if (!sweeping || staves.Count == 0) return;

            var filledWidth = staves.Count * Pitch - Gap;
            var band = Math.Max(24, filledWidth / 3);
            var referenceDate = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var seconds = (date - referenceDate).TotalSeconds;
            var within = seconds % (Sweep + SweepRest);
            if (within >= Sweep) return;
            var phase = within / Sweep;
            var x = -band + phase * (filledWidth + band);
            var bandBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(140, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                },
            };
            using (context.PushGeometryClip(filled))
                context.DrawRectangle(bandBrush, null, new Rect(x, 0, band, bottom));
        }
    }
}

/// <summary>:449-481 <c>struct HydraHeadProgress: View</c>.</summary>
public sealed class HydraHeadProgress : Decorator
{
    // :453-454, :456-480
    public HydraHeadProgress(ChatThread head, ThreadRuntime runtime)
    {
        var info = head.Hydra;
        var task = (info?.Task ?? "").Trimmed();
        var status = info?.HydraHeadInfoStatus ?? HydraHeadInfo.Status.Running;
        var startedAt = info?.StartedAt ?? head.CreatedAt;

        var title = new HydraWorkingTitle(task.Length == 0 ? head.Title : task, status == HydraHeadInfo.Status.Running)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        var color = (info?.Persona ?? HydraRoster.Persona(0)).Color;
        var bar = new HydraProgressBar(runtime, startedAt, info?.FinishedAt, status,
            new SwiftColor(color.Red, color.Green, color.Blue, color.Alpha));

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto,*"),
            Margin = new Thickness(20, HydraPanel.StripHeight, 20, 0),
            Children = { title, bar },
        };
        Grid.SetRow(title, 1);
        Grid.SetRow(bar, 2);
        bar.Margin = new Thickness(0, 10, 0, 0);
        Child = body;
    }
}

/// <summary>:483-539 <c>struct HydraWorkingTitle: View</c>.</summary>
public sealed class HydraWorkingTitle : Decorator
{
    private const double Sweep = 3.2; // :492
    private const double SweepRest = 3.0; // :494
    private const double Breath = 3.2; // :496
    private const double Reach = 0.3; // :498

    // :487-489, :500-534
    public HydraWorkingTitle(string text, bool isRunning, Avalonia.Media.TextAlignment alignment = Avalonia.Media.TextAlignment.Center)
    {
        var label = new TextBlock
        {
            Text = text,
            TextAlignment = alignment,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Chrome.PrimaryText.Brush(this),
        };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Medium).Apply(label);

        Child = new Grid { Children = { label } };

        // Lead (native gallery, 2026-10-08): a finished head's title is not ticking, so a theme change left it in the old
        // theme's colour (white on the light panel). It is painted again whenever the theme changes.
        void Paint(DateTimeOffset date)
        {
            var referenceDate = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var now = (date - referenceDate).TotalSeconds;
            var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
            var animates = isRunning && !reduceMotion;
            var phase = Math.Min((now % (Sweep + SweepRest)) / Sweep, 1);
            var centre = animates ? -Reach + phase * (1 + 2 * Reach) : 0.5;
            var dip = animates ? 0.6 : 1.0;
            var breath = animates ? 0.5 + 0.5 * Math.Sin(now / Breath * 2 * Math.PI) : 1.0;
            label.Opacity = animates ? 0.86 + 0.14 * breath : 1;
            var primary = Chrome.PrimaryText.Resolve(ActualThemeVariant);
            label.Foreground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(primary, 0),
                    new GradientStop(primary, (float)Math.Clamp(centre - Reach, 0, 1)),
                    new GradientStop(Color.FromArgb((byte)(primary.A * dip), primary.R, primary.G, primary.B), (float)Math.Clamp(centre, 0, 1)),
                    new GradientStop(primary, (float)Math.Clamp(centre + Reach, 0, 1)),
                    new GradientStop(primary, 1),
                },
            };
        }
        Child.TimelineView(
            () => new AnimationTimelineSchedule(
                1.0 / 20.0,
                !isRunning ||
                EnvironmentValues.GetAccessibilityReduceMotion(this) ||
                ScrollActivity.Shared.Current.IsScrolling),
            Paint);
        ActualThemeVariantChanged += (_, _) => Paint(DateTimeOffset.UtcNow);
    }
}

/// <summary>:541-562 <c>struct HydraElapsedTime: View</c>.</summary>
public sealed class HydraElapsedTime : Decorator
{
    private readonly TextBlock label;
    private DateTimeOffset startedAt; // :544
    private DateTimeOffset? finishedAt; // :545
    private bool isRunning; // :546
    private ITimer? nextTick;
    private bool attached;
    private int ticking; // the generation of the tick in force: a tick of an earlier one does nothing
    private Action<TimeSpan>? swap;
    private IFrameSource? swapFrames;

    // :544-546, :548-561
    public HydraElapsedTime(DateTimeOffset startedAt, DateTimeOffset? finishedAt, bool isRunning)
    {
        label = new TextBlock
        {
            Foreground = Chrome.SecondaryText.Brush(this),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, // :137 beside the 16-point glyph an HStack centres the clock on it
        };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).MonospacedDigit().Apply(label);
        Child = label;
        this.startedAt = startedAt; this.finishedAt = finishedAt; this.isRunning = isRunning;
        if (!isRunning) label.Text = Finished(); // :555
        label.AttachedToVisualTree += (_, _) => { attached = true; if (this.isRunning) Tick(++ticking); };
        label.DetachedFromVisualTree += (_, _) => { attached = false; ticking++; nextTick?.Dispose(); nextTick = null; };
    }

    private string Finished() => RelativeTime.Duration((finishedAt ?? SwiftDate.Now) - startedAt); // :555

    // :551 ED-117: periodic-from-date is separate from the display's animation clock.
    private void Tick(int generation)
    {
        nextTick?.Dispose(); nextTick = null;
        if (!attached || !isRunning || generation != ticking) return;
        var now = SwiftDate.Now;
        var n = Math.Floor((now - startedAt).TotalSeconds);
        var gridDate = startedAt.AddSeconds(n);
        label.Text = RelativeTime.Duration(gridDate - startedAt); // :552
        nextTick = SwiftClock.Provider.Current.CreateTimer(_ => MainActor.Post(() => Tick(generation)), null,
            startedAt.AddSeconds(n + 1) - now, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The view handed its inputs again (:544-546 `let`s of a view that stays: a body run again): the same text, now counting from <paramref name="nextStartedAt"/>,
    /// ticking only while it runs (:549-556 the branch). When the words it says change under <paramref name="change"/> they swap with a fade (OD-0076, content that swaps
    /// crossfades: the old time fades out, the new one comes in where it stood, each over half the motion), else at once. ED-255 (5).</summary>
    internal void Update(DateTimeOffset nextStartedAt, DateTimeOffset? nextFinishedAt, bool nextIsRunning, Animation? change)
    {
        if (nextStartedAt == startedAt && nextFinishedAt == finishedAt && nextIsRunning == isRunning) return;
        startedAt = nextStartedAt; finishedAt = nextFinishedAt; isRunning = nextIsRunning;
        var generation = ++ticking; nextTick?.Dispose(); nextTick = null;
        void Show() { if (isRunning) Tick(generation); else label.Text = Finished(); }
        StopSwap();
        var next = isRunning ? RelativeTime.Duration(startedAt.AddSeconds(Math.Floor((SwiftDate.Now - startedAt).TotalSeconds)) - startedAt) : Finished();
        if (change is null || !attached || label.Text == next) { label.Opacity = 1; Show(); return; }
        var half = change.Duration / 2; var frames = FrameClock.Source.Current; var since = SwiftClock.Now; var shown = false;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            var elapsed = (SwiftClock.Now - since).TotalSeconds;
            if (elapsed < half) { label.Opacity = 1 - Animation.EaseIn(half).Progress(elapsed); frames.RequestFrame(frame!); return; }
            if (!shown) { shown = true; Show(); }
            var done = elapsed + 0.001 >= 2 * half;
            label.Opacity = done ? 1 : Animation.EaseOut(half).Progress(elapsed - half);
            if (done) { swap = null; return; }
            frames.RequestFrame(frame!);
        };
        swap = frame; swapFrames = frames; frames.RequestFrame(frame);
    }

    private void StopSwap()
    {
        if (swap is { } pending) swapFrames?.CancelFrame(pending);
        swap = null;
    }
}

/// <summary>:564-579 <c>private struct ProgressCardSurface: ViewModifier</c>.</summary>
internal sealed record ProgressCardSurface(bool IsOnGlassPanel, bool IsDark)
{
    // :571-578
    public Control Body(Control content)
    {
        var surface = new Border
        {
            CornerRadius = new CornerRadius(Chrome.CardCornerRadius),
            Child = content,
        }.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous); // :572 `shape`; the card can be 32 to 49 pt tall, ED-275 (2)'s band (OD-0102)
        Apply(surface);
        return surface;
    }

    public void Apply(Border surface)
    {
        surface.Background = IsOnGlassPanel ? Chrome.PanelControlFill(IsDark).Brush(surface) : null;
        SwiftGlass.SetEffect(surface, IsOnGlassPanel ? null : GlassEffect.Regular(
            GlassShape.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous)));
    }
}

/// <summary>:581-603 <c>@MainActor @Observable final class SweepBudget</c> (R-OBS-6).</summary>
public sealed class SweepBudget : MainActorModel
{
    // :587
    public static SweepBudget Shared { get; } = new();

    private ValueList<object> sweepers = ValueList<object>.Empty;

    // :588
    public ValueList<object> Sweepers { get => Read(sweepers); private set => Set(ref sweepers, value); }

    // :590-593
    public void Join(object id)
    {
        if (Sweepers.Contains(id)) return;
        Sweepers = new ValueList<object>(Sweepers.Append(id));
    }

    // :595-598
    public void Leave(object id)
    {
        var index = -1;
        for (var i = 0; i < Sweepers.Count; i++)
            if (ReferenceEquals(Sweepers[i], id)) { index = i; break; }
        if (index < 0) return;
        Sweepers = new ValueList<object>(Sweepers.Where((_, i) => i != index));
    }

    // :600-602
    public bool Sweeps(object id)
    {
        for (var i = 0; i < Math.Min(HydraProgressBar.SweepSlots, Sweepers.Count); i++)
            if (ReferenceEquals(Sweepers[i], id)) return true;
        return false;
    }
}
