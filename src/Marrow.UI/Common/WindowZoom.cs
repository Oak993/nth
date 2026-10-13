// PORT-ONLY (ED-NEW-perf-motion-12; the lead's ruling 2026-10-13 on finding 7). A window of its own that is not a hosted root (the
// welcome tour, a floating guide panel, an alert) takes the app zoom of the window it serves when it opens (AppZoom.Zoomed,
// ED-NEW-textsize-1). The zoom of that window glides (ZoomMotion, ED-NEW-textsize-3), and a zoom changed while such a window was
// open left it at the scale it opened with beside a window gliding to another. It now follows the glide frame by frame, as a
// popover does (PopupZoom, ED-NEW-perf-motion-7): each frame of the motion draws its content at the served window's scale, and the
// site sizes and places its window for that scale.

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Marrow.UI.Common;

/// <summary>A window of its own drawn at the zoom of the window it serves, through every frame of a zoom (ED-NEW-perf-motion-12).</summary>
public static class WindowZoom
{
    /// <summary>
    /// Keeps <paramref name="content"/>, shown in <paramref name="window"/> at the app zoom of <paramref name="served"/>, at that zoom on
    /// every frame of a zoom motion of <paramref name="served"/>; <paramref name="resized"/> is told each new scale (to size and place the
    /// window). It ends when the window closes or the result is disposed; nothing follows when <paramref name="served"/> does not zoom.
    /// </summary>
    public static IDisposable Follow(Window window, Control content, TopLevel? served, Action<double>? resized = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(content);
        if (served?.GetVisualDescendants().OfType<ZoomLayout>().FirstOrDefault() is not { } root || ZoomMotion.For(root) is not { } motion)
            return new Following(null, null, null);
        var drawn = AppZoom.ScaleOf(content);
        void Moved(double _)
        {
            var scale = AppZoom.ScaleOfWindow(served);
            if (!(scale > 0) || scale == drawn) return;
            drawn = scale;
            Draw(window, content, scale);
            resized?.Invoke(scale);
        }
        var following = new Following(motion, Moved, window);
        motion.Moved += Moved;
        window.Closed += following.Closed;
        return following;
    }

    /// <summary>
    /// A <see cref="Follow"/> step for a window sized to its content and centred on its owner (an alert, a dialog): it lays the window
    /// out at the new scale and keeps its centre where it was, so it grows and shrinks about its middle as it did when it opened there.
    /// </summary>
    public static Action<double> KeepingCentre(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        (double X, double Y)? centre = null;
        PixelPoint placed = default;
        return _ =>
        {
            var scaling = window.RenderScaling;
            var before = window.ClientSize;
            // Taken afresh when the window was moved since the last frame (an alert can be dragged); kept exactly otherwise, so the
            // rounding of each frame's position never walks the window off its centre.
            if (centre is null || window.Position != placed)
                centre = (window.Position.X + before.Width * scaling / 2, window.Position.Y + before.Height * scaling / 2);
            // The size the window takes for its content at the new scale: the layout's, which the platform's resize then follows.
            window.UpdateLayout();
            var after = window.DesiredSize.Width > 0 && window.DesiredSize.Height > 0 ? window.DesiredSize : window.ClientSize;
            placed = new PixelPoint((int)Math.Round(centre.Value.X - after.Width * scaling / 2), (int)Math.Round(centre.Value.Y - after.Height * scaling / 2));
            window.Position = placed;
        };
    }

    // The content drawn at a scale: a ZoomLayout it is in takes the scale (AppZoomModifier keeps its layout); one shown at 100 % with
    // no layout (AppZoom.Zoomed returns it bare) is put in one.
    private static void Draw(Window window, Control content, double scale)
    {
        var zoomed = content.Parent is ZoomLayout || content.Parent is AppZoomModifier.ZoomScaler { Parent: ZoomLayout };
        if (!zoomed && ReferenceEquals(window.Content, content)) window.Content = null;
        var shown = new AppZoomModifier(100) { BaseScale = scale }.Body(content);
        if (!ReferenceEquals(window.Content, shown)) window.Content = shown;
    }

    private sealed class Following(ZoomMotion? motion, Action<double>? moved, Window? window) : IDisposable
    {
        private ZoomMotion? motion = motion;

        internal void Closed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (motion is not null && moved is not null) motion.Moved -= moved;
            if (window is not null) window.Closed -= Closed;
            motion = null;
        }
    }
}
