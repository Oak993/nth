// PORT-ONLY: ChatView.swift:1124,1176-1180,1238 the chrome row's `HStack(alignment: .center, spacing: 10)` whose title takes the leftover width and whose
// layout glides under `.animation(Chrome.panelSlide, value: sidebarVisible)`. Avalonia's StackPanel gives a control only its own width and animates no
// layout. ED-NEW-parity-chatview (1).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port;

/// <summary>PORT-ONLY. The row's <c>HStack(alignment: .center, spacing: 10)</c> (ChatView.swift:1124) with the title's
/// <c>.frame(maxWidth: .infinity, alignment: .leading)</c> (:1238) in it: every other control takes its own width, and the
/// title's frame takes what is left, so the trailing capsules sit at the row's trailing end. A horizontal StackPanel gave
/// the title only its text's width and drew the capsules straight after it. It also carries what SwiftUI's stack gets from
/// `.animation(Chrome.panelSlide, value: sidebarVisible)` on the row (:1179): a control whose place in the row changes glides
/// from where it was (<see cref="Glide"/>), and a control that is removed gives up its slot at once while it fades out where it
/// stood (<see cref="Departing"/>). ED-NEW-parity-chatview (1).</summary>
internal sealed class ChatChromeRowStack : StackPanel
{
    private Animation? glide;
    private ContinuousInstant glideStart;
    // A control is known by its kind and its place among controls of that kind: the row rebuilds most of its controls on every
    // refresh (a SwiftUI body re-evaluates; the view's identity is its position), so the same control is a new instance.
    private Dictionary<(Type, int), double>? glideFrom; // asked for: where each control was drawn when the change began.
    private readonly Dictionary<(Type, int), double> glideOffsets = []; // how far from its new place each control began.
    private Action<TimeSpan>? pendingFrame;
    private IFrameSource? glideFrames; // the frame source of the context that asked for the glide: layout runs outside it.

    public ChatChromeRowStack()
    {
        Orientation = Orientation.Horizontal;
        DetachedFromVisualTree += (_, _) => StopFrames();
    }

    /// <summary>The controls whose removal is running: not in the row's layout, drawn where they stood.</summary>
    internal HashSet<Control> Departing { get; } = [];

    private static bool FillsRow(Control child) => child is ChatChromeTitle;

    private IEnumerable<Control> Shown => Children.Where(child => child.IsVisible);

    private static IEnumerable<((Type, int) Key, Control Child)> Keyed(IEnumerable<Control> children)
    {
        var counts = new Dictionary<Type, int>();
        foreach (var child in children)
        {
            var type = child.GetType();
            counts[type] = counts.GetValueOrDefault(type) + 1;
            yield return ((type, counts[type]), child);
        }
    }

    /// <summary>The next layout puts every control it has already drawn at its new place by gliding from where it is drawn
    /// now, on <paramref name="animation"/> (a control that arrives has no old place and comes in at its own).</summary>
    internal void Glide(Animation animation)
    {
        if (Bounds.Width <= 0) return; // nothing has been drawn yet: there is no old place to glide from.
        glide = animation;
        glideStart = SwiftClock.Now;
        glideFrames = FrameClock.Source.Current;
        glideOffsets.Clear();
        glideFrom = Keyed(Shown).ToDictionary(entry => entry.Key, entry => entry.Child.Bounds.X);
        InvalidateArrange();
    }

    private double GlideRemaining()
    {
        if (glide is null || glideOffsets.Count == 0) return 0;
        var elapsed = SwiftClock.Now - glideStart;
        return elapsed.Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(glide.Duration).Ticks ? 0 : 1 - glide.Progress(elapsed.TotalSeconds);
    }

    private void RequestGlideFrame()
    {
        if (pendingFrame is not null || glideFrames is not { } frames) return;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (!ReferenceEquals(pendingFrame, tick)) return;
            pendingFrame = null;
            InvalidateArrange();
            if (GlideRemaining() > 0) RequestGlideFrame();
        };
        pendingFrame = tick;
        frames.RequestFrame(tick);
    }

    private void StopFrames()
    {
        if (pendingFrame is { } frame) glideFrames?.CancelFrame(frame);
        pendingFrame = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var shown = Shown.ToArray();
        foreach (var child in shown.Where(Departing.Contains))
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var slots = shown.Where(child => !Departing.Contains(child)).ToArray();
        var gaps = Math.Max(0, slots.Length - 1) * Spacing;
        var used = gaps;
        var height = 0.0;
        foreach (var child in slots.Where(child => !FillsRow(child)))
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            used += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var left = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - used);
        var fills = false;
        foreach (var child in slots.Where(FillsRow))
        {
            fills = true;
            child.Measure(new Size(left, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            if (double.IsInfinity(left)) used += child.DesiredSize.Width;
        }
        // :1238 maxWidth .infinity: with a title the row is as wide as it is offered.
        var width = fills && !double.IsInfinity(availableSize.Width) ? availableSize.Width : used;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var keyed = Keyed(Shown).ToArray();
        var slots = keyed.Where(entry => !Departing.Contains(entry.Child)).ToArray();
        var fixedWidth = slots.Where(entry => !FillsRow(entry.Child)).Sum(entry => entry.Child.DesiredSize.Width) + Math.Max(0, slots.Length - 1) * Spacing;
        var left = Math.Max(0, finalSize.Width - fixedWidth);
        var places = new List<((Type, int) Key, Control Child, double X, double Width)>();
        var x = 0.0;
        foreach (var (key, child) in slots)
        {
            var width = FillsRow(child) ? left : child.DesiredSize.Width;
            places.Add((key, child, x, width));
            x += width + Spacing;
        }
        var from = glideFrom;
        if (from is not null)
        {
            glideFrom = null;
            glideOffsets.Clear();
            foreach (var (key, _, place, _) in places)
                if (from.TryGetValue(key, out var was) && Math.Abs(was - place) > 0.01) glideOffsets[key] = was - place;
            if (glideOffsets.Count != 0) RequestGlideFrame();
        }
        var remaining = GlideRemaining();
        if (remaining == 0) glideOffsets.Clear();
        foreach (var (key, child, place, width) in places)
            child.Arrange(new Rect(place + (glideOffsets.TryGetValue(key, out var offset) ? offset * remaining : 0), 0, width, finalSize.Height));
        foreach (var (key, child) in keyed.Where(entry => Departing.Contains(entry.Child))) // drawn where it stood.
            child.Arrange(new Rect(from is not null && from.TryGetValue(key, out var stood) ? stood : child.Bounds.X, 0, child.DesiredSize.Width, finalSize.Height));
        return finalSize;
    }
}
