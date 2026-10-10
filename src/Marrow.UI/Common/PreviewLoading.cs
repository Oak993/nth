// PORT-ONLY: OD-0136 amendment 2, step 13 "Large image preview" (ED-NEW-nth-motion). The big loader of the large image and
// video preview: what stands in the frame while the picture loads, and the glass puck over it.
//  - NthPreviewBackdrop: the blurred thumbnail (sigma 12, scale 1.15) fills the frame at once when the site has the
//    thumbnail, else the skeleton colour; the light band crosses it at 35 % strength every 2.2 s. One composition visual on
//    the render thread (an NthMotionControl); the blur is made once, never per frame.
//  - PreviewPuck: the 44 px puck (rgba(18,20,26,.62) + 1 px white 18 % rim) holding the step 4 ring in white (24 px) when the
//    progress is known, or the step 1 spinner in white (20 px) when it is not. Arrival: opacity to 0 and scale to .9 in 0.24 s.
// Omitted on purpose: the 10 px backdrop blur behind the puck. What lies behind it here is the already blurred thumbnail or a
// flat plate, so a second blur would change no pixel, and a backdrop filter on the render thread would cost every frame.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using SkiaSharp;

namespace Marrow.UI.Common;

/// <summary>The backdrop's state: the thumbnail to blur (null: the plain plate) and when the picture arrived (clock seconds).</summary>
public sealed record PreviewBackdropState(DecodedBitmap? Thumbnail, double? ArrivedAt);

/// <summary>Step 13's placeholder behind a loading large preview. Call <see cref="Arrive"/> when the sharp picture lands.</summary>
public sealed class NthPreviewBackdrop : NthMotionControl
{
    private readonly DecodedBitmap? thumbnail;
    private double? arrivedAt;

    public NthPreviewBackdrop(DecodedBitmap? thumbnail = null)
    {
        this.thumbnail = thumbnail;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>Whether the blurred thumbnail is drawn (the site had one).</summary>
    public bool HasThumbnail => thumbnail is not null;

    /// <summary>The picture arrived: the light band fades out in 0.2 s (the blur stays under the sharp picture).</summary>
    public void Arrive() { arrivedAt ??= Now; Send(); }

    public bool HasArrived => arrivedAt is not null;

    protected override object? State => new PreviewBackdropState(thumbnail, arrivedAt);

    protected override NthPainter CreatePainter() => new PreviewBackdropPainter();
}

/// <summary>Draws step 13's blurred thumbnail and light band (render thread, or any canvas for tests).</summary>
public sealed class PreviewBackdropPainter : NthSkiaPainter
{
    /// <summary>The spec's blur sigma and zoom.</summary>
    public const double BlurSigma = 12, Zoom = 1.15, BandStrength = .35, BandSeconds = 2.2;

    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private SKImage? blurred;
    private (DecodedBitmap? Source, double Width, double Height) blurredFor;
    private SKShader? band;
    private (double W, double H, bool Dark) bandFor = (-1, -1, false);

    /// <summary>How many times the blur was made (tests: once per thumbnail and size, never per frame).</summary>
    public int BlursMade { get; private set; }

    public override bool Moving(NthFrame frame, double now) =>
        frame.State is PreviewBackdropState s && (s.ArrivedAt is not { } at ? !frame.Calm : now - at < .2);

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not PreviewBackdropState s) return;
        double w = frame.Size.Width, h = frame.Size.Height;
        if (w <= 0 || h <= 0) return;
        var palette = frame.Palette;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)w, (float)h));
        paint.Shader = null;
        paint.Color = palette.Skeleton;
        canvas.DrawRect(new SKRect(0, 0, (float)w, (float)h), paint);
        if (s.Thumbnail is { } thumbnail && Blur(thumbnail, w, h) is { } image)
        {
            // UniformToFill into the frame, then the spec's scale(1.15) about the centre (the blur is made before the zoom, as CSS does)
            var fill = Math.Max(w / thumbnail.Width, h / thumbnail.Height);
            canvas.Save();
            canvas.Translate((float)(w / 2), (float)(h / 2));
            canvas.Scale((float)(fill * Zoom));
            canvas.DrawImage(image, -thumbnail.Width / 2f, -thumbnail.Height / 2f, paint);
            canvas.Restore();
        }
        var fade = s.ArrivedAt is { } at ? 1 - NthEase.Clamp01((now - at) / .2) : 1;
        if (fade > 0 && NthMotion.SkeletonLight(now - frame.Started, frame.Calm, BandSeconds) is { } a)
        {
            canvas.Translate((float)(-w + 2 * w * a), 0);
            paint.Shader = Band(w, h, palette, frame.Dark);
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * BandStrength * fade));
            canvas.DrawRect(new SKRect(0, 0, (float)w, (float)h), paint);
            paint.Shader = null;
        }
        canvas.Restore();
    }

    // The thumbnail blurred once, in its own pixels: a sigma of 12 frame points is 12 / fill thumbnail pixels.
    private SKImage? Blur(DecodedBitmap source, double w, double h)
    {
        if (blurred is not null && ReferenceEquals(blurredFor.Source, source) && blurredFor.Width == w && blurredFor.Height == h) return blurred;
        blurred?.Dispose();
        blurred = null;
        blurredFor = (source, w, h);
        if (source.Width <= 0 || source.Height <= 0 || source.Pixels.Length < source.BytesPerRow * (source.Height - 1) + source.Width * 4) return null;
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var raw = SKImage.FromPixelCopy(info, source.Pixels, source.BytesPerRow);
        if (raw is null) return null;
        using var surface = SKSurface.Create(info);
        var fill = Math.Max(w / source.Width, h / source.Height);
        var sigma = (float)(BlurSigma / Math.Max(fill, .0001));
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var blur = new SKPaint { ImageFilter = filter };
        surface.Canvas.DrawImage(raw, 0, 0, blur);
        blurred = surface.Snapshot();
        BlursMade++;
        return blurred;
    }

    // .pv .band::after: linear-gradient(100deg, transparent 30%, light 50%, transparent 70%) over the frame, as in step 5.
    private SKShader Band(double w, double h, NthPalette palette, bool dark)
    {
        if (band is not null && bandFor == (w, h, dark)) return band;
        band?.Dispose();
        var angle = 100 * Math.PI / 180;
        double dx = Math.Sin(angle), dy = -Math.Cos(angle);
        var half = (Math.Abs(w * Math.Sin(angle)) + Math.Abs(h * Math.Cos(angle))) / 2;
        var c = palette.SkeletonLight;
        band = SKShader.CreateLinearGradient(new SKPoint((float)(w / 2 - dx * half), (float)(h / 2 - dy * half)), new SKPoint((float)(w / 2 + dx * half), (float)(h / 2 + dy * half)),
            [c.WithAlpha(0), c.WithAlpha(0), c, c.WithAlpha(0), c.WithAlpha(0)], [0, .3f, .5f, .7f, 1], SKShaderTileMode.Clamp);
        bandFor = (w, h, dark);
        return band;
    }

    public override void Dispose()
    {
        paint.Dispose(); blurred?.Dispose(); band?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// The 44 px glass puck of step 13. It shows after the 0.4 s the old loader waited (UI-COMPOSER-077), so a picture that is
/// already at hand never flashes it; <see cref="Arrive"/> takes it away (opacity 0, scale .9, 0.24 s).
/// </summary>
public sealed class PreviewPuck : Border
{
    /// <summary>The puck's size, and the ring's and the spinner's inside it.</summary>
    public const double Size = 44, RingSize = 24, SpinnerSize = 20;

    private static readonly SplineEasing Ease = new(.25, .1, .25, 1); // CSS ease
    private readonly NthRing ring = new() { Width = RingSize, Height = RingSize, Puck = true };
    private readonly NthSpinner spinner = new() { Width = SpinnerSize, Height = SpinnerSize, Ink = NthSpinnerInk.Puck };
    private bool arrived;

    public PreviewPuck(double? progress = null)
    {
        Width = Size; Height = Size;
        CornerRadius = new CornerRadius(Size / 2);
        Background = Solid(NthPalette.PuckBackground);
        BorderBrush = Solid(NthPalette.PuckRim);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "Loading");
        Opacity = 0;
        Show(progress);
        AttachedToVisualTree += (_, _) => Appear();
    }

    /// <summary>The control inside: the ring when the progress is known, else the spinner.</summary>
    public Control Mark => Child!;

    public bool IsRing => ReferenceEquals(Child, ring);

    public bool HasArrived => arrived;

    /// <summary>Known progress (0..1) shows the ring; null shows the spinner.</summary>
    public void Show(double? progress)
    {
        if (progress is { } value)
        {
            ring.Value = value;
            Child = ring;
        }
        else Child = spinner;
    }

    private void Appear()
    {
        if (arrived) return;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(.2), Delay = TimeSpan.FromSeconds(.4), Easing = new LinearEasing() }];
        Dispatcher.UIThread.Post(() => { if (!arrived) Opacity = 1; }, DispatcherPriority.Render);
    }

    /// <summary>The sharp picture landed: the puck leaves (opacity to 0 and scale to .9 in 0.24 s; calm, the fade alone).</summary>
    public void Arrive()
    {
        if (arrived) return;
        arrived = true;
        var calm = EnvironmentValues.GetAccessibilityReduceMotion(this);
        var duration = TimeSpan.FromSeconds(.24);
        Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = Ease },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = duration, Easing = Ease },
        ];
        Opacity = 0;
        if (!calm) RenderTransform = TransformOperations.Parse("scale(0.9)");
    }

    private static IBrush Solid(SKColor color) => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(NthPalette.ToColor(color));
}
