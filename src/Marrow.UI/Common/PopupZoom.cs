// PORT-ONLY (ED-NEW-textsize-1): the app zoom (AppZoom.cs) for the surfaces that are not inside a window root's ZoomLayout.
//
// The Settings zoom scales a window root (WindowManager.HostedEnvironment, AppZoom.swift:24 scaleEffect). A popover, a tooltip
// or a menu is a Popup, which opens in a top-level of its own (a PopupRoot window, or the overlay layer's host above the window's
// content): neither is under the root's ZoomLayout, so at 125 % every popover's words were still drawn at 100 % beside a
// sidebar and composer drawn at 125 % (owner, 2026-10-11). Every Popup of the process now takes the zoom of the place it hangs
// from: when it opens, its child is hung on a PopupZoom in its host, which lays it out at the host's size divided by the scale
// and draws it scaled back up, as ZoomLayout does for a window. The popup's child keeps its own logical parent (the Popup), so
// its styles, appearance and the sites that read popup.Child are as before; only the visual parent changes, and only while the
// zoom is not 100 %.

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Marrow.UI.Common;

/// <summary>A popup's content drawn at the app zoom of the control it hangs from (ED-NEW-textsize-1).</summary>
public sealed class PopupZoom : Control
{
    private static bool installed;
    private static readonly ConditionalWeakTable<Popup, PopupZoom> zooms = new();

    private readonly Scaler scaler = new();

    internal PopupZoom(double scale)
    {
        Scale = scale;
        scaler.RenderTransform = new ScaleTransform(scale, scale);
        scaler.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        VisualChildren.Add(scaler);
    }

    /// <summary>The scale the content is drawn at.</summary>
    public double Scale { get; }

    /// <summary>The popup's content, a visual child only (its logical parent stays the Popup).</summary>
    public Control? Child
    {
        get => scaler.Child;
        internal set => scaler.Child = value;
    }

    /// <summary>Gives every Popup of the process the app zoom of its anchor (once; PortControlThemes.Register calls it).</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, change) =>
        {
            if (change.GetNewValue<bool>()) Opened(popup);
            else Release(popup);
        });
    }

    /// <summary>What <paramref name="host"/> shows of a popup: the content a PopupZoom carries, or its content itself.</summary>
    public static object? Shown(ContentControl host) => host.Content is PopupZoom zoom ? zoom.Child : host.Content;

    /// <summary>
    /// Shows <paramref name="child"/> in the popup host <paramref name="host"/> at the zoom of <paramref name="popup"/>'s anchor:
    /// what a site that replaces a popup's content while it is open uses (PopoverHost.Follow), so the new content is zoomed too.
    /// </summary>
    public static void Show(Popup popup, ContentControl host, Control? child)
    {
        if (ReferenceEquals(Shown(host), child)) return;
        var scale = child is null ? 1 : AppZoom.ScaleOf(Anchor(popup));
        if (host.Content is PopupZoom old) old.Child = null;
        if (child is null || scale == 1)
        {
            host.Content = child;
            return;
        }
        Hang(popup, host, child, scale);
    }

    // The popup has opened (Popup.Open has given its child to its host): hang the child on a zoom when its anchor is zoomed.
    private static void Opened(Popup popup)
    {
        if (popup.Child is not { } child || HostOf(popup, child) is not ContentControl host || !ReferenceEquals(host.Content, child)) return;
        var scale = AppZoom.ScaleOf(Anchor(popup));
        if (scale == 1) return;
        Hang(popup, host, child, scale);
        // Popup.UpdateHostSizing gives the host the popup's own Width, MinWidth, MaxWidth and heights, in the anchor's unzoomed
        // points; the host draws them zoomed.
        if (host is Avalonia.Layout.Layoutable sized)
        {
            if (double.IsFinite(sized.Width)) sized.Width *= scale;
            if (double.IsFinite(sized.Height)) sized.Height *= scale;
            if (sized.MinWidth > 0 && double.IsFinite(sized.MinWidth)) sized.MinWidth *= scale;
            if (sized.MinHeight > 0 && double.IsFinite(sized.MinHeight)) sized.MinHeight *= scale;
            if (double.IsFinite(sized.MaxWidth)) sized.MaxWidth *= scale;
            if (double.IsFinite(sized.MaxHeight)) sized.MaxHeight *= scale;
        }
    }

    private static void Hang(Popup popup, ContentControl host, Control child, double scale)
    {
        var zoom = new PopupZoom(scale);
        host.Content = zoom; // the host lets go of the child first: a visual has one parent
        zoom.Child = child;
        zooms.AddOrUpdate(popup, zoom);
    }

    // The popup has closed (Popup.Close has taken the zoom from its host): its child leaves the zoom, so the next host it opens
    // in can show it. Not on the zoom's own detach: a host's presenter may take its content away and give it back while it is open.
    private static void Release(Popup popup)
    {
        if (!zooms.TryGetValue(popup, out var zoom)) return;
        zooms.Remove(popup);
        zoom.Child = null;
    }

    private static Visual? Anchor(Popup popup) => popup.PlacementTarget ?? popup.Parent as Visual ?? popup.GetVisualParent();

    // Avalonia 12.1.2 keeps the host a popup opened in to itself (Popup.Host is internal); the host shows the child as its
    // Content, and a host that has drawn it is also among the child's visual ancestors.
    private static readonly System.Reflection.PropertyInfo? HostProperty =
        typeof(Popup).GetProperty("Host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static Control? HostOf(Popup popup, Visual child) =>
        HostProperty?.GetValue(popup) as Control
        ?? child.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(ancestor => ancestor is PopupRoot or OverlayPopupHost);

    // AppZoom.swift:34-38, as ZoomLayout: the content is measured in the space divided by the scale.
    protected override Size MeasureOverride(Size availableSize)
    {
        scaler.Measure(new Size(availableSize.Width / Scale, availableSize.Height / Scale));
        // Whole points up, so the layout rounding of the host never takes the last fraction of a zoomed line away.
        return new Size(Math.Ceiling(scaler.DesiredSize.Width * Scale - 1e-9), Math.Ceiling(scaler.DesiredSize.Height * Scale - 1e-9));
    }

    // AppZoom.swift:40-46: placed at the origin in the space divided by the scale, then drawn scaled.
    protected override Size ArrangeOverride(Size finalSize)
    {
        scaler.Arrange(new Rect(0, 0, finalSize.Width / Scale, finalSize.Height / Scale));
        return finalSize;
    }

    // Carries the scale as its render transform, so the content keeps its own (a popover's grow-in scales the card).
    private sealed class Scaler : Control
    {
        private Control? child;

        public Control? Child
        {
            get => child;
            set
            {
                if (ReferenceEquals(child, value)) return;
                if (child is not null) VisualChildren.Remove(child);
                child = value;
                if (value is not null) VisualChildren.Add(value);
                InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (child is null) return default;
            child.Measure(availableSize);
            return child.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
