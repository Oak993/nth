using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
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
    private AmbientMotionController? ambient;
    private Window? window;
    private IPlatformSettings? platform;
    // This window's own place on the screen, for Perspective; the render thread reads it.
    private readonly WallpaperWindowPosition position = new();
    private WallpaperEnvironment? environment;
    private StaticWallpaperInputs? staticInputs;
    // Legacy WL-4 host adapter; production uses WallpaperEnvironment for both collections.
    public StaticWallpaperInputs? StaticInputs
    {
        get => staticInputs;
        set
        {
            staticInputs = value;
            if (visual is null) return;
            wakes?.Dispose();
            environment = value ?? WallpaperEnvironment.Create();
            wakes = new(environment, () => Avalonia.Threading.Dispatcher.UIThread.Post(() => visual?.SendHandlerMessage(WallpaperWake.Instance)));
            Send();
        }
    }
    private WallpaperWakeSource? wakes;

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
        if (window is not null) { window.PropertyChanged += WindowChanged; window.PositionChanged += WindowMoved; }
        environment = staticInputs ?? new WallpaperEnvironment(SwiftClock.Provider.Current, SystemWallpaperAccent.Shared, position);
        wakes = new WallpaperWakeSource(environment, () => Avalonia.Threading.Dispatcher.UIThread.Post(() => visual?.SendHandlerMessage(WallpaperWake.Instance)));
        platform = Application.Current?.PlatformSettings;
        if (platform is not null) platform.ColorValuesChanged += AccentChanged;
        var availability = AmbientWindowState.Acquire(this);
        ambient = availability is null ? null : new(availability);
        visual = host.Compositor.CreateCustomVisual(new WallpaperVisualHandler(ambient));
        ambient?.Bind(visual);
        ElementComposition.SetElementChildVisual(this, visual); Send();
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) Send();
        else if (e.Property == BoundsProperty) Send();
    }

    private void WindowMoved(object? sender, PixelPointEventArgs e) => Send();
    // the accent source raises Changed, which wakes every wallpaper's renderer (a Match accent one eases to the new colour)
    private void AccentChanged(object? sender, PlatformColorValues values) => SystemWallpaperAccent.Shared.Update(values.AccentColor1.R, values.AccentColor1.G, values.AccentColor1.B);

    // OPTIONS-FORMAT 3: the window centre minus the screen centre, over half the screen size (all in pixels of the screen the window is on).
    private void UpdatePosition()
    {
        if (window?.Screens.ScreenFromWindow(window) is not { } screen) return;
        var scale = window.RenderScaling; var centreX = window.Position.X + window.Bounds.Width * scale / 2; var centreY = window.Position.Y + window.Bounds.Height * scale / 2;
        position.Update(centreX - screen.Bounds.X, centreY - screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
    }

    private void Send()
    {
        if (visual is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (platform?.GetColorValues().AccentColor1 is { } accent) SystemWallpaperAccent.Shared.Update(accent.R, accent.G, accent.B);
        UpdatePosition();
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        visual.SendHandlerMessage(new WallpaperSnapshot(Selection, ActualThemeVariant == ThemeVariant.Dark,
            EnvironmentValues.GetAccessibilityReduceMotion(this), IsEffectivelyVisible && window?.IsVisible != false
                && window?.WindowState != WindowState.Minimized, Bounds.Size, SwiftClock.Provider.Current,
            environment));
        var active = IsEffectivelyVisible && window?.IsVisible != false && window?.WindowState != WindowState.Minimized;
        wakes?.Update(Selection.Kind != WallpaperKind.None && WallpaperLibraryStore.Normalize(Selection).Dynamic == true
            && (Selection.Kind == WallpaperKind.Animated || active));
    }

    private void Remove()
    {
        if (window is not null) { window.PropertyChanged -= WindowChanged; window.PositionChanged -= WindowMoved; }
        if (platform is not null) platform.ColorValuesChanged -= AccentChanged;
        platform = null; wakes?.Dispose(); wakes = null; environment = null;
        visual?.SendHandlerMessage(WallpaperVisualStop.Instance);
        ambient?.Dispose(); ambient = null;
        ElementComposition.SetElementChildVisual(this, null); visual = null; window = null;
    }
}
