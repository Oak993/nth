using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Styling;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>
/// WL-2 (ED-NEW-wltwo-1): one live wallpaper picture in the Settings library, drawn by the real engine (<see cref="WallpaperRenderer"/>)
/// on the compositor at the picture's own small size, so the GPU draws it and the UI thread only sends a step. Until the shared
/// <see cref="WallpaperThumbnailTicker"/> admits it, it shows its pack's colours as a soft gradient, and the engine's picture fades in
/// over it. It is advanced only while it is in view, its window is shown and Reduce Motion is off; a still wallpaper is drawn once and
/// cached by the renderer, and only a change (a pack, an option, the theme) is eased.
/// </summary>
public sealed class WallpaperThumbnail : Control, IWallpaperThumbnailClient
{
    /// <summary>The picture's corner (the design pages' tiles: 12 to 13 px at a larger size).</summary>
    public const double Radius = 10;
    /// <summary>How far outside the view a picture still counts as in view, so one scrolling in is already moving.</summary>
    public const double ViewMargin = 48;
    private const double EaseSeconds = .6;

    private readonly WallpaperThumbnailTicker ticker;
    private WallpaperSelection selection;
    private CompositionCustomVisual? visual;
    private Window? window;
    private AnimatedScalar? fade;
    private double easeLeft;
    private bool inView;

    public WallpaperThumbnail(WallpaperDefinition wallpaper, WallpaperSelection selection, WallpaperThumbnailTicker? ticker = null)
    {
        Wallpaper = wallpaper; this.ticker = ticker ?? WallpaperThumbnailTicker.Shared;
        this.selection = WallpaperLibraryStore.Normalize(selection);
        IsHitTestVisible = false; Focusable = false; ClipToBounds = true;
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => Uninstall();
        EffectiveViewportChanged += (_, e) => SetInView(e.EffectiveViewport);
        ActualThemeVariantChanged += (_, _) => { InvalidateVisual(); Send(ease: true); };
    }

    public WallpaperDefinition Wallpaper { get; }

    /// <summary>What it shows: the wallpaper's own choices (its pack, speed and options); a change eases, as on the backdrop.</summary>
    public WallpaperSelection Selection
    {
        get => selection;
        set
        {
            var next = WallpaperLibraryStore.Normalize(value);
            if (Same(next, selection)) return;
            selection = next; InvalidateVisual(); Send(ease: true);
        }
    }

    /// <summary>The chosen wallpaper animates first when the animating ones are capped.</summary>
    public bool IsChosen { get; set { if (field == value) return; field = value; ticker.Wake(); } }
    /// <summary>Its place on the page (top to bottom), the order the rest are admitted and animated in.</summary>
    public int Order { get; set; }
    /// <summary>Drawn by the engine (admitted by the ticker).</summary>
    public bool Live { get; private set; }
    /// <summary>The seconds it has been moved on in all (a test reads it; the render thread's renderer gets the same steps).</summary>
    public double Ticked { get; private set; }
    /// <summary>On screen, or within <see cref="ViewMargin"/> of it.</summary>
    public bool InView => inView;
    /// <summary>The window's Reduce Motion (the inherited environment value the backdrop's WallpaperView reads too).</summary>
    public bool ReduceMotion => EnvironmentValues.GetAccessibilityReduceMotion(this);

    private bool Shown => VisualRoot is not null && IsEffectivelyVisible && window?.IsVisible != false && window?.WindowState != WindowState.Minimized;

    public bool WantsAdmission => !Live && inView && Shown && Bounds.Width > 0 && Bounds.Height > 0;
    public bool Animating => Live && inView && Shown && Wallpaper.Kind == WallpaperKind.Animated && !ReduceMotion;
    public bool Easing => Live && easeLeft > 0;
    public int Rank => IsChosen ? -1 : Order;

    public void Admit()
    {
        if (Live || visual is null) return;
        Live = true; Send(ease: false);
        fade ??= new AnimatedScalar(this, 0, v => { if (visual is not null) visual.Opacity = (float)v; });
        fade.Set(0, null); fade.Set(1, Animation.EaseOut(.45)); // the design pages' tiles: the drawn picture fades in over half a second
    }

    public void Advance(double seconds)
    {
        if (seconds <= 0 || !double.IsFinite(seconds)) return;
        Ticked += seconds; easeLeft = Math.Max(0, easeLeft - seconds);
        visual?.SendHandlerMessage(new WallpaperThumbnailStep(seconds));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            if (visual is not null) visual.Size = new Vector(Bounds.Width, Bounds.Height);
            Send(ease: false); ticker.Wake();
        }
        else if (change.Property == EnvironmentValues.AccessibilityReduceMotionProperty) { Send(ease: false); ticker.Wake(); }
        else if (change.Property == IsVisibleProperty) ticker.Wake();
    }

    private void SetInView(Rect viewport)
    {
        var next = viewport.Width > 0 && viewport.Height > 0 && viewport.Inflate(ViewMargin).Intersects(new Rect(Bounds.Size));
        if (next == inView) return;
        inView = next; ticker.Wake();
    }

    private void Install()
    {
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is not null) window.PropertyChanged += WindowChanged;
        visual = host.Compositor.CreateCustomVisual(new WallpaperThumbnailHandler());
        visual.Size = new Vector(Bounds.Width, Bounds.Height); visual.Opacity = 0;
        ElementComposition.SetElementChildVisual(this, visual);
        ticker.Add(this);
    }

    private void Uninstall()
    {
        ticker.Remove(this);
        if (window is not null) window.PropertyChanged -= WindowChanged;
        window = null;
        visual?.SendHandlerMessage(WallpaperVisualStop.Instance);
        ElementComposition.SetElementChildVisual(this, null);
        visual = null; Live = false; inView = false; easeLeft = 0;
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) ticker.Wake();
    }

    private void Send(bool ease)
    {
        if (!Live || visual is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        visual.SendHandlerMessage(new WallpaperThumbnailSnapshot(selection, ActualThemeVariant == ThemeVariant.Dark, ReduceMotion, Bounds.Size));
        if (ease) { easeLeft = EaseSeconds; ticker.Wake(); }
    }

    private static bool Same(WallpaperSelection a, WallpaperSelection b) =>
        a.Kind == b.Kind && a.Id == b.Id && a.Pack == b.Pack && a.Speed == b.Speed && a.Dynamic == b.Dynamic && a.Persp == b.Persp
        && a.Focus == b.Focus && (a.Options ?? new Dictionary<string, double>()).OrderBy(o => o.Key, StringComparer.Ordinal)
            .SequenceEqual((b.Options ?? new Dictionary<string, double>()).OrderBy(o => o.Key, StringComparer.Ordinal));

    /// <summary>Until the engine's picture is in, the pack's own six colours as a soft diagonal gradient on a rounded plate.</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var pack = Wallpaper.Packs.FirstOrDefault(p => p.Id == selection.Pack) ?? Wallpaper.Packs[0];
        var colours = dark ? pack.Dark : pack.Light;
        var stops = new GradientStops();
        for (var i = 0; i < colours.Length; i++) stops.Add(new GradientStop(Color.Parse(colours[i]), colours.Length == 1 ? 0 : (double)i / (colours.Length - 1)));
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = stops };
        context.DrawRectangle(brush, null, new Rect(Bounds.Size), Radius, Radius);
    }
}

internal sealed record WallpaperThumbnailSnapshot(WallpaperSelection Selection, bool Dark, bool ReduceMotion, Size Size);

internal sealed class WallpaperThumbnailStep(double seconds)
{
    public double Seconds { get; } = seconds;
}

/// <summary>The render thread's half: one renderer per picture, drawn on the GPU when there is one, clipped to the rounded corner.</summary>
internal sealed class WallpaperThumbnailHandler : CompositionCustomVisualHandler
{
    private WallpaperRenderer? renderer;
    private WallpaperThumbnailSnapshot? snapshot;
    private bool stopped;
    private readonly SKPaint opacityPaint = new();

    public override void OnMessage(object message)
    {
        if (message is WallpaperVisualStop) { stopped = true; renderer?.Dispose(); renderer = null; opacityPaint.Dispose(); return; }
        if (stopped) return;
        if (message is WallpaperThumbnailSnapshot next)
        {
            if (renderer is null) renderer = new(next.Selection, next.Dark);
            else renderer.Set(next.Selection, next.Dark);
            renderer.ReduceMotion = next.ReduceMotion; snapshot = next; Invalidate(); return;
        }
        if (message is WallpaperThumbnailStep step && renderer is not null) { renderer.Advance(step.Seconds); Invalidate(); }
    }

    public override Rect GetRenderBounds() => snapshot is { } s ? new Rect(s.Size) : default;

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (stopped || renderer is null || snapshot is not { } s || s.Size.Width <= 0 || s.Size.Height <= 0) return;
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas; var matrix = canvas.TotalMatrix;
        var scale = (float)Math.Max(1, Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY));
        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, (float)s.Size.Width, (float)s.Size.Height), (float)WallpaperThumbnail.Radius), antialias: true);
        if (lease.CurrentOpacity < 1)
        {
            opacityPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(lease.CurrentOpacity * 255), 0, 255));
            canvas.SaveLayer(opacityPaint);
        }
        canvas.Scale(1 / scale);
        renderer.Draw(canvas, (int)Math.Ceiling(s.Size.Width * scale), (int)Math.Ceiling(s.Size.Height * scale), scale, lease.GrContext);
        if (lease.CurrentOpacity < 1) canvas.Restore();
        canvas.Restore();
    }
}
