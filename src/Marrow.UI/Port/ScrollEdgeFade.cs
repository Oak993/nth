// PORT-ONLY (OD-0151, ED-NEW-settingsinapp-1). The scroll-edge fade the owner asked for (2026-10-11): content that scrolls under what sits
// above a scroll area (a page's title row in Settings, the search pill over the model list) dissolves into it instead of being cut hard or
// running under it, and content below the fold fades out softly. "Still seeing overlap? Wasn't there a fade in effect?", "Implement it
// throughout the settings", "slight fade on the lower side too so it seems premium? Just a bit?", "Slight fade below the search models pill too?".
// One attached behaviour on a ScrollViewer: an opacity mask over the viewer, a vertical gradient that is clear over TopInset (the band of
// whatever floats over the viewer's top), ramps to opaque over Top, and ramps out over Bottom at the foot. Each edge fades only while there
// is content past it: the top as the content scrolls away from its start, the bottom while more lies below, each eased in over its own
// length, so a list at rest at its top, or one too short to scroll, has no mask at all.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port;

/// <summary>The scroll-edge fade of a <see cref="ScrollViewer"/> (see the file note).</summary>
public static class ScrollEdgeFade
{
    /// <summary>Whether the viewer fades its content at the edges it scrolls past.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(ScrollEdgeFade));

    /// <summary>The length of the top fade, in points.</summary>
    public static readonly AttachedProperty<double> TopProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>("Top", typeof(ScrollEdgeFade), DefaultLength);

    /// <summary>The length of the bottom fade, in points.</summary>
    public static readonly AttachedProperty<double> BottomProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>("Bottom", typeof(ScrollEdgeFade), DefaultLength);

    /// <summary>The band at the viewer's top that something floats over (a title row): scrolled content is clear there, and the fade starts below it.</summary>
    public static readonly AttachedProperty<double> TopInsetProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>("TopInset", typeof(ScrollEdgeFade));

    /// <summary>The gentle default, for popover and menu lists: about 16 points.</summary>
    public const double DefaultLength = 16;

    public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(IsEnabledProperty, value);

    public static double GetTop(ScrollViewer viewer) => viewer.GetValue(TopProperty);

    public static void SetTop(ScrollViewer viewer, double value) => viewer.SetValue(TopProperty, value);

    public static double GetBottom(ScrollViewer viewer) => viewer.GetValue(BottomProperty);

    public static void SetBottom(ScrollViewer viewer, double value) => viewer.SetValue(BottomProperty, value);

    public static double GetTopInset(ScrollViewer viewer) => viewer.GetValue(TopInsetProperty);

    public static void SetTopInset(ScrollViewer viewer, double value) => viewer.SetValue(TopInsetProperty, value);

    /// <summary>Turns the fade on for <paramref name="viewer"/> (one line at a site), with the given lengths; returns the viewer.</summary>
    public static T Fades<T>(this T viewer, double top = DefaultLength, double bottom = DefaultLength) where T : ScrollViewer
    {
        SetTop(viewer, top);
        SetBottom(viewer, bottom);
        SetIsEnabled(viewer, true);
        return viewer;
    }

    /// <summary>How strongly the top edge fades now, 0 (not at all) to 1.</summary>
    public static double TopStrength(ScrollViewer viewer) => States.TryGetValue(viewer, out var state) ? state.Top : 0;

    /// <summary>How strongly the bottom edge fades now, 0 (not at all) to 1.</summary>
    public static double BottomStrength(ScrollViewer viewer) => States.TryGetValue(viewer, out var state) ? state.Bottom : 0;

    private sealed class State
    {
        public readonly GradientStop[] Stops = [new(), new(), new(), new(), new(), new()];
        public LinearGradientBrush? Brush;
        public double Top;
        public double Bottom;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State> States = new();

    static ScrollEdgeFade()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, change) =>
        {
            if (change.GetNewValue<bool>())
            {
                viewer.ScrollChanged += OnScrollChanged;
                viewer.SizeChanged += OnSizeChanged;
                Update(viewer);
            }
            else
            {
                viewer.ScrollChanged -= OnScrollChanged;
                viewer.SizeChanged -= OnSizeChanged;
                if (States.TryGetValue(viewer, out var state) && ReferenceEquals(viewer.OpacityMask, state.Brush)) viewer.OpacityMask = null;
                States.Remove(viewer);
            }
        });
        TopProperty.Changed.AddClassHandler<ScrollViewer>((viewer, _) => Refresh(viewer));
        BottomProperty.Changed.AddClassHandler<ScrollViewer>((viewer, _) => Refresh(viewer));
        TopInsetProperty.Changed.AddClassHandler<ScrollViewer>((viewer, _) => Refresh(viewer));
    }

    private static void Refresh(ScrollViewer viewer)
    {
        if (GetIsEnabled(viewer)) Update(viewer);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer) Update(viewer);
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer) Update(viewer);
    }

    /// <summary>Puts the mask for where <paramref name="viewer"/> is scrolled now (the behaviour calls it on every scroll and resize).</summary>
    public static void Update(ScrollViewer viewer)
    {
        var state = States.GetValue(viewer, _ => new State());
        var height = viewer.Bounds.Height;
        var topLength = Math.Max(0, GetTop(viewer));
        var bottomLength = Math.Max(0, GetBottom(viewer));
        var inset = Math.Max(0, GetTopInset(viewer));
        var offset = viewer.Offset.Y;
        var below = viewer.Extent.Height - viewer.Viewport.Height - offset;
        // Each edge eases in over its own length of scrolling, so the mask grows from nothing and never jumps.
        state.Top = topLength > 0 && offset > 0.5 ? Math.Clamp(offset / topLength, 0, 1) : 0;
        state.Bottom = bottomLength > 0 && below > 0.5 ? Math.Clamp(below / bottomLength, 0, 1) : 0;
        if (height <= 0 || (state.Top <= 0 && state.Bottom <= 0))
        {
            if (state.Brush is not null && ReferenceEquals(viewer.OpacityMask, state.Brush)) viewer.OpacityMask = null;
            return;
        }
        // Lengths that do not fit the viewer share its height.
        var total = inset + topLength + bottomLength;
        var fit = total > height ? height / total : 1;
        var insetEnd = inset * fit / height;
        var topEnd = (inset + topLength) * fit / height;
        var bottomStart = 1 - bottomLength * fit / height;
        var stops = state.Stops;
        Set(stops[0], 0, 1 - state.Top);
        Set(stops[1], insetEnd, 1 - state.Top);
        Set(stops[2], Math.Max(insetEnd, topEnd), 1);
        Set(stops[3], Math.Max(topEnd, bottomStart), 1);
        Set(stops[4], 1, 1 - state.Bottom);
        Set(stops[5], 1, 1 - state.Bottom);
        if (state.Brush is null)
        {
            state.Brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            };
            foreach (var stop in stops) state.Brush.GradientStops.Add(stop);
        }
        if (!ReferenceEquals(viewer.OpacityMask, state.Brush)) viewer.OpacityMask = state.Brush;
        else viewer.InvalidateVisual();

        static void Set(GradientStop stop, double at, double alpha)
        {
            stop.Offset = at;
            stop.Color = Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), 0, 0, 0);
        }
    }
}
