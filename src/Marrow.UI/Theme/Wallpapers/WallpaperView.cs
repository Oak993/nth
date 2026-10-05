using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>The app-owned backdrop, drawn on the compositor so glass can sample its pixels.</summary>
public sealed class WallpaperView : Control
{
    public static readonly StyledProperty<WallpaperSelection> SelectionProperty =
        AvaloniaProperty.Register<WallpaperView, WallpaperSelection>(nameof(Selection), new());
    public WallpaperSelection Selection { get => GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    private CompositionCustomVisual? visual;
    private Window? window;

    public WallpaperView()
    {
        IsHitTestVisible = false; Focusable = false; ClipToBounds = true;
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Remove();
        ActualThemeVariantChanged += (_, _) => Send();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectionProperty || change.Property == BoundsProperty
            || change.Property == IsVisibleProperty || change.Property.Name == nameof(IsEffectivelyVisible)
            || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) Send();
    }

    private void Install()
    {
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is not null) window.PropertyChanged += WindowChanged;
        visual = host.Compositor.CreateCustomVisual(new WallpaperVisualHandler());
        ElementComposition.SetElementChildVisual(this, visual); Send();
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) Send();
    }

    private void Send()
    {
        if (visual is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        visual.SendHandlerMessage(new WallpaperSnapshot(Selection, ActualThemeVariant == ThemeVariant.Dark,
            EnvironmentValues.GetAccessibilityReduceMotion(this), IsEffectivelyVisible && window?.IsVisible != false
                && window?.WindowState != WindowState.Minimized, Bounds.Size, SwiftClock.Provider.Current));
    }

    private void Remove()
    {
        if (window is not null) window.PropertyChanged -= WindowChanged;
        visual?.SendHandlerMessage(WallpaperVisualStop.Instance);
        ElementComposition.SetElementChildVisual(this, null); visual = null; window = null;
    }
}
