// PORT-ONLY (PR-COMPOSER-003, ED-333): SwiftUI animates the frames of the views that stay when a layout changes inside an animated
// transaction, wherever the change moves them: in a stack pinned to the bottom of its window, the views above a new one rise in the window although
// their place in the stack is the same. ED-218's ForEach glide follows a row's place in its parent, and a leaving row keeps its place until its
// removal is over. This glides a panel's children in window coordinates instead: Capture reads where each one is drawn in its top level (with any
// glide running), the change is made, and after the layout pass each child that moved is drawn back where it was and glides to its new place along
// the transaction's animation. Depart takes a leaving view out of the layout at once and lets it fade where it was drawn, on an overlay over the panel.
// The curve given is followed as given, Reduce Motion included (Chrome.panelSlide is itself the 0.18 s ease-out there): the source animates the
// layout under Reduce Motion too.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>Glides the children of a panel to their new places in window coordinates (see the file note).</summary>
public sealed class WindowGlide
{
    private readonly Panel panel;
    private readonly Canvas departures;
    private readonly Dictionary<Control, Glide> glides = [];
    // The leaving views and where in the window each was drawn when it left: the overlay moves with the panel (its height changes, and a panel pinned
    // to the bottom of its window moves its top), so each is put back there after every layout pass until it has gone.
    private readonly Dictionary<Control, Point> departed = [];

    /// <param name="panel">The panel whose children glide.</param>
    /// <param name="departures">An overlay laid over the panel (the same size and place) on which leaving views fade.</param>
    public WindowGlide(Panel panel, Canvas departures)
    {
        this.panel = panel;
        this.departures = departures;
        departures.LayoutUpdated += (_, _) => KeepDepartedInPlace();
    }

    private void KeepDepartedInPlace()
    {
        if (departed.Count == 0 || TopLevel.GetTopLevel(departures) is not { } top || departures.TranslatePoint(default, top) is not { } origin) return;
        foreach (var (container, at) in departed.ToArray())
        {
            if (!departures.Children.Contains(container)) { departed.Remove(container); continue; }
            var left = at.X - origin.X; var topAt = at.Y - origin.Y;
            if (Canvas.GetLeft(container) != left) Canvas.SetLeft(container, left);
            if (Canvas.GetTop(container) != topAt) Canvas.SetTop(container, topAt);
        }
    }

    /// <summary>Where each child of the panel is drawn now, in its top level's coordinates.</summary>
    public IReadOnlyDictionary<Control, Point> Capture()
    {
        var places = new Dictionary<Control, Point>();
        if (TopLevel.GetTopLevel(panel) is not { } top) return places;
        foreach (var child in panel.Children)
            if (child.TranslatePoint(default, top) is { } at) places[child] = at;
        return places;
    }

    /// <summary>
    /// After the next layout pass, each child in <paramref name="before"/> that is still the panel's and was laid out elsewhere in the window is drawn
    /// where it was and glides to its new place along <paramref name="animation"/>.
    /// </summary>
    public void GlideAfterLayout(IReadOnlyDictionary<Control, Point> before, Animation animation)
    {
        if (before.Count == 0) return;
        EventHandler? laidOut = null;
        laidOut = (_, _) =>
        {
            panel.LayoutUpdated -= laidOut;
            if (TopLevel.GetTopLevel(panel) is not { } top) return;
            foreach (var (child, was) in before)
            {
                if (!panel.Children.Contains(child)) continue;
                var glide = glides.TryGetValue(child, out var running) ? running : null;
                var shown = glide?.Offset ?? default;
                // where the layout puts it now, without the glide it is drawn with
                if (child.TranslatePoint(default, top) is not { } drawn) continue;
                var laid = drawn - shown;
                var offset = was - laid;
                if (Math.Abs(offset.X) < 0.5 && Math.Abs(offset.Y) < 0.5) continue;
                if (glide is null) glides[child] = glide = new Glide(child, () => glides.Remove(child));
                glide.Run(offset, animation);
            }
        };
        panel.LayoutUpdated += laidOut;
    }

    /// <summary>
    /// Takes <paramref name="view"/> out of the panel's layout at once and fades it, with <paramref name="removal"/>, where it is drawn now: on the
    /// overlay, at its place and size. Nothing of it is left once its removal is over.
    /// </summary>
    public void Depart(Control view, Action<Panel, Control> removal)
    {
        var container = SwiftTransition.Container(view);
        // not one of the panel's views: the removal is the transition's own, wherever the view is (it is always run, so its completion is too)
        if (!panel.Children.Contains(container)) { removal(panel, view); return; }
        var at = container.TranslatePoint(default, departures);
        var inWindow = TopLevel.GetTopLevel(panel) is { } top ? container.TranslatePoint(default, top) : null;
        var size = container.Bounds.Size;
        if (glides.Remove(container, out var glide)) glide.Stop();
        panel.Children.Remove(container);
        container.IsHitTestVisible = false;
        if (at is { } place && size.Width > 0 && size.Height > 0)
        {
            container.Width = size.Width;
            container.Height = size.Height;
            Canvas.SetLeft(container, place.X);
            Canvas.SetTop(container, place.Y);
        }
        else
        {
            // never laid out (it left in the pass that brought it): nothing of it was drawn, and its removal still runs to its end
            container.Width = container.Height = 0;
            inWindow = null;
        }
        departures.Children.Add(container);
        if (inWindow is { } fixedAt) departed[container] = fixedAt;
        removal(departures, view);
    }

    // One child's glide: a translation from where it was drawn back to nothing, on the frame clock.
    private sealed class Glide
    {
        private readonly Control child;
        private readonly Action finished;
        private readonly TranslateTransform shift = new();
        private IFrameSource? source;
        private Action<TimeSpan>? pending;

        public Glide(Control child, Action finished)
        {
            this.child = child;
            this.finished = finished;
            child.RenderTransform = shift;
        }

        public Vector Offset => new(shift.X, shift.Y);

        public void Run(Vector from, Animation animation)
        {
            Stop();
            if (!ReferenceEquals(child.RenderTransform, shift)) child.RenderTransform = shift;
            shift.X = from.X;
            shift.Y = from.Y;
            if (!FrameClock.IsBound) { End(); return; }
            var start = SwiftClock.Now;
            var span = TimeSpan.FromSeconds(animation.Duration);
            var frames = FrameClock.Source.Current;
            Action<TimeSpan>? tick = null;
            tick = _ =>
            {
                var now = SwiftClock.Now;
                if (FrameClock.IsComplete(start, now, span)) { End(); return; }
                var left = 1 - animation.Progress((now - start).TotalSeconds);
                shift.X = from.X * left;
                shift.Y = from.Y * left;
                frames.RequestFrame(tick!);
            };
            source = frames;
            pending = tick;
            frames.RequestFrame(tick);
        }

        public void Stop()
        {
            if (pending is { } frame) source?.CancelFrame(frame);
            pending = null;
        }

        private void End()
        {
            pending = null;
            shift.X = shift.Y = 0;
            if (ReferenceEquals(child.RenderTransform, shift)) child.RenderTransform = null;
            finished();
        }
    }
}
