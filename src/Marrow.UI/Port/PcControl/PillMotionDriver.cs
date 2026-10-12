// PORT-ONLY: Tactus pill v2, how the pill moves on screen (OD-0145, ED-NEW-pcctl-22 item 5, ED-NEW-pcctl-24; DESIGN.md 10.6
// "Glass, motion and one clock", 10.11 "MotionDriver"; canvas pill v2 `frame()`). The UI thread is never on the path of a
// travel or a morph. At a change it lays the controls out at their final places once and hands the motion to the
// compositor in one commit: the controls ride composition key frames sampled every 1 ms from the closed-form curves
// (a FLIP: the key frames carry the offset from the final place to zero, so input hit-tests the final layout and nothing
// is laid out per frame), and the glass body and Stop's silver capsule are drawn by composition custom visuals. With the
// default driver (`RenderThread`) those two evaluate the same curves at the composition frame time, exactly, from the
// commit that started the key frames; with the fallback (`KeyFrames`, MARROW_TACTUS_DRIVER=keyframes, for NP-16) their
// place and size ride key frames too, so everything is on one clock. A drag frame sets the values directly. Nothing here
// allocates per frame.
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port.PcControl.Pill;
using SkiaSharp;

namespace Marrow.UI.Port.PcControl;

/// <summary>Who draws the glass body and Stop's capsule while the pill moves (ED-NEW-pcctl-22 item 5).</summary>
public enum PillDriverKind
{
    /// <summary>Custom visuals evaluate the curves at the composition frame time (the default).</summary>
    RenderThread,

    /// <summary>Their place and size ride the same 1 ms key frames as the controls (the fallback NP-16 may choose).</summary>
    KeyFrames,
}

/// <summary>The one switch that picks the driver (as `OverlayHitTestSwitch` picks the hit test).</summary>
public static class PillDriverSwitch
{
    public const string Variable = "MARROW_TACTUS_DRIVER";

    public const PillDriverKind Default = PillDriverKind.RenderThread;

    public static PillDriverKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "keyframes" or "key-frames" or "keyframe" or "fallback" => PillDriverKind.KeyFrames,
        "render" or "renderthread" or "render-thread" => PillDriverKind.RenderThread,
        _ => Default,
    };

    public static PillDriverKind Current() => Parse(Environment.GetEnvironmentVariable(Variable));
}

/// <summary>What moves a control's composition visual: key frames from a track, or a value set at once.</summary>
public interface IPillAnimator
{
    void Translate(Visual target, PillTrack x, PillTrack y);

    void Resize(Visual target, PillTrack width, PillTrack height);

    void Scale(Visual target, PillTrack scale);

    void SetTranslation(Visual target, double x, double y);

    void SetSize(Visual target, double width, double height);

    void SetScale(Visual target, double scale);

    /// <summary>The point a scale grows from (the part's own coordinates).</summary>
    void SetCentre(Visual target, double x, double y);

    /// <summary>Reduce Motion's fades only.</summary>
    void Fade(Visual target, PillTrack opacity);

    void SetOpacity(Visual target, double opacity);

    /// <summary>A breathing loop: full, down to <paramref name="low"/> and back over <paramref name="seconds"/>, until stopped.</summary>
    void Breathe(Visual target, double low, double seconds);

    void StopBreathing(Visual target);
}

/// <summary>
/// The real animator: the controls' element visuals and Avalonia's composition key frames. Two rules from how Avalonia
/// 12.1 syncs an element visual (Visual.SynchronizeCompositionProperties writes Offset, Size, Visible and Opacity from the
/// control whenever the control is invalidated, and a client-side write equal to the client's last value is dropped):
/// a value set at once is written twice (a different value, then the value), so it always reaches the compositor and ends
/// any animation there, even after a key-frame animation left the compositor at another value; and an opacity set at once
/// is also set on the control, so a later sync writes the same value. No StopAnimation (it touches the compositor's
/// animation list from the UI thread): a direct write ends an animation in the same batch.
/// </summary>
public sealed class CompositionPillAnimator : IPillAnimator
{
    public static CompositionPillAnimator Instance { get; } = new();

    public void Translate(Visual target, PillTrack x, PillTrack y)
    {
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.Vector3D(visual, "Translation", x, y, 0);
    }

    public void Resize(Visual target, PillTrack width, PillTrack height)
    {
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.Vector(visual, "Size", width, height);
    }

    public void Scale(Visual target, PillTrack scale)
    {
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.Vector3D(visual, "Scale", scale, scale, 1);
    }

    public void SetTranslation(Visual target, double x, double y)
    {
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.Set(visual, "Translation", new Vector3D(x, y, 0));
    }

    public void SetSize(Visual target, double width, double height)
    {
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.SetSize(visual, new Vector(width, height));
    }

    public void SetScale(Visual target, double scale)
    {
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.Set(visual, "Scale", new Vector3D(scale, scale, 1));
    }

    public void SetCentre(Visual target, double x, double y)
    {
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.Set(visual, "CenterPoint", new Vector3D(x, y, 0));
    }

    public void Fade(Visual target, PillTrack opacity)
    {
        if (opacity.Constant || opacity.Duration < PillTrack.Step)
        {
            SetOpacity(target, opacity.Last);
            return;
        }
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.Scalar(visual, "Opacity", opacity);
    }

    public void SetOpacity(Visual target, double opacity)
    {
        target.Opacity = opacity;
        if (ElementComposition.GetElementVisual(target) is { } visual) PillComposition.SetOpacity(visual, opacity);
    }

    public void Breathe(Visual target, double low, double seconds)
    {
        if (ElementComposition.GetElementVisual(target) is not { } visual) return;
        target.Opacity = 1;
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromSeconds(seconds);
        animation.IterationBehavior = Avalonia.Rendering.Composition.Animations.AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0f, 1f);
        animation.InsertKeyFrame(0.5f, (float)low, PillComposition.Breath);
        animation.InsertKeyFrame(1f, 1f, PillComposition.Breath);
        visual.StartAnimation("Opacity", animation);
    }

    public void StopBreathing(Visual target) => SetOpacity(target, 1);
}

/// <summary>Key frames from tracks: one linear key per 1 ms sample, ending at the final value (SetToFinalValue).</summary>
public static class PillComposition
{
    private static readonly LinearEasing Linear = new();

    /// <summary>The breathing loops' ease (each half of the loop is a sine in and out).</summary>
    internal static readonly SineEaseInOut Breath = new();

    /// <summary>A Vector3D property ("Translation", "Offset", "Scale") from two tracks (z fixed); a constant pair is set at once.</summary>
    public static void Vector3D(CompositionVisual visual, string property, PillTrack x, PillTrack y, double z)
    {
        if ((x.Constant && y.Constant) || x.Duration < PillTrack.Step)
        {
            Set(visual, property, new Avalonia.Vector3D(x.Last, y.Last, z));
            return;
        }
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        Once(animation, x.Duration);
        var keys = x.Keys;
        for (var i = 0; i < keys.Count; i++)
            animation.InsertKeyFrame(keys[i], new Avalonia.Vector3D(x.Values[i], y.Values[i], z), Linear);
        visual.StartAnimation(property, animation);
    }

    /// <summary>The "Size" property from two tracks; a constant pair is set at once.</summary>
    public static void Vector(CompositionVisual visual, string property, PillTrack x, PillTrack y)
    {
        if ((x.Constant && y.Constant) || x.Duration < PillTrack.Step)
        {
            SetSize(visual, new Avalonia.Vector(x.Last, y.Last));
            return;
        }
        var animation = visual.Compositor.CreateVectorKeyFrameAnimation();
        Once(animation, x.Duration);
        var keys = x.Keys;
        for (var i = 0; i < keys.Count; i++)
            animation.InsertKeyFrame(keys[i], new Avalonia.Vector(x.Values[i], y.Values[i]), Linear);
        visual.StartAnimation(property, animation);
    }

    /// <summary>The "Opacity" property from one track; a constant track is set at once.</summary>
    public static void Scalar(CompositionVisual visual, string property, PillTrack value)
    {
        if (value.Constant || value.Duration < PillTrack.Step)
        {
            SetOpacity(visual, value.Last);
            return;
        }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        Once(animation, value.Duration);
        for (var i = 0; i < value.Keys.Count; i++) animation.InsertKeyFrame(value.Keys[i], (float)value.Values[i], Linear);
        visual.StartAnimation(property, animation);
    }

    /// <summary>Sets a Vector3D property at once, always reaching the compositor (see <see cref="CompositionPillAnimator"/>).</summary>
    public static void Set(CompositionVisual visual, string property, Avalonia.Vector3D value)
    {
        var other = new Avalonia.Vector3D(value.X + 1, value.Y, value.Z);
        switch (property)
        {
            case "Translation":
                visual.Translation = other;
                visual.Translation = value;
                break;
            case "Offset":
                visual.Offset = other;
                visual.Offset = value;
                break;
            case "Scale":
                visual.Scale = other;
                visual.Scale = value;
                break;
            case "CenterPoint":
                visual.CenterPoint = other;
                visual.CenterPoint = value;
                break;
            default:
                throw new ArgumentException("Not a Vector3D property the pill sets: " + property, nameof(property));
        }
    }

    /// <summary>Sets the size at once, always reaching the compositor.</summary>
    public static void SetSize(CompositionVisual visual, Avalonia.Vector value)
    {
        visual.Size = new Avalonia.Vector(value.X + 1, value.Y);
        visual.Size = value;
    }

    /// <summary>Sets the opacity at once, always reaching the compositor.</summary>
    public static void SetOpacity(CompositionVisual visual, double value)
    {
        visual.Opacity = (float)value + 0.5f;
        visual.Opacity = (float)value;
    }

    private static void Once(Avalonia.Rendering.Composition.Animations.KeyFrameAnimation animation, double seconds)
    {
        animation.Duration = TimeSpan.FromSeconds(seconds);
        animation.IterationBehavior = Avalonia.Rendering.Composition.Animations.AnimationIterationBehavior.Count;
        animation.IterationCount = 1;
        animation.StopBehavior = Avalonia.Rendering.Composition.Animations.AnimationStopBehavior.SetToFinalValue;
    }
}

/// <summary>Which of the pill's two drawn surfaces.</summary>
public enum PillSurfaceKind
{
    /// <summary>The glass body: shadow, tint, sheen, rim.</summary>
    Body,

    /// <summary>Stop's silver capsule.</summary>
    Stop,
}

/// <summary>Which layers a window draws (DESIGN.md 10.6: the region band never draws the shadow; the shadow band draws it).</summary>
[Flags]
public enum PillPaint
{
    None = 0,
    Shadow = 1,
    Body = 2,
    Content = 4,
    All = Shadow | Body | Content,
}

/// <summary>How a surface looks: whether the glass blur is under it (else the dense panel, ED-238), Stop's hover and focus, high contrast.</summary>
public readonly record struct PillSurfaceLook(bool Glass, bool Hover, bool Focused, bool HighContrast, PillPaint Paint, double Dense)
{
    public static PillSurfaceLook Default => new(false, false, false, false, PillPaint.All, 0);
}

/// <summary>
/// One message to a surface's handler. <see cref="Motion"/> set: evaluate it at <see cref="SentAt"/> plus the composition
/// time since the message arrived (the key frames of the same commit start at that arrival). <see cref="Motion"/> null: draw
/// <see cref="Pose"/> as it is (rest, a drag frame), or, with <see cref="Fill"/>, fill the visual's animated size.
/// </summary>
public sealed record PillSurfaceFrame(PillSurfaceKind Kind, PillMotion? Motion, PillPose Pose, PillParts Parts, double SentAt, PillSurfaceLook Look, bool Fill);

/// <summary>
/// The control that owns one drawn surface: a composition custom visual placed under the stage. With the render-thread
/// driver the visual covers a fixed area round the anchor and draws the pose it evaluates; with the key-frame driver its
/// own offset and size ride key frames and it draws to fill them.
/// </summary>
public sealed class PillSurface : Control
{
    /// <summary>The fixed area of the render-thread body surface, round the anchor (DIP): wider and taller than any pill or card.</summary>
    public static readonly Rect BodyArea = new(-700, -PillSurfacePainter.Pad, 1400, 760);

    /// <summary>The render-thread Stop surface's area, from Stop's slot (its capsule starts at the slot's left edge).</summary>
    public static readonly Rect StopArea = new(-PillSurfacePainter.StopPad, -PillSurfacePainter.StopPad, 640 + (2 * PillSurfacePainter.StopPad), PillSurfacePainter.StopHeight + (2 * PillSurfacePainter.StopPad));

    public static Rect AreaOf(PillSurfaceKind kind) => kind == PillSurfaceKind.Body ? BodyArea : StopArea;

    private readonly PillSurfaceHandler handler;
    private CompositionCustomVisual? visual;
    private PillSurfaceFrame? pending;

    public PillSurface(PillSurfaceKind kind, PillDriverKind driver)
    {
        Kind = kind;
        Driver = driver;
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        handler = new PillSurfaceHandler(new PillSurfacePainter());
    }

    public PillSurfaceKind Kind { get; }

    public PillDriverKind Driver { get; }

    /// <summary>The last frame sent (tests).</summary>
    public PillSurfaceFrame? Last { get; private set; }

    /// <summary>Frames the handler drew (tests).</summary>
    public int FramesDrawn => handler.Frames;

    public PillSurfaceLook Look { get; private set; } = PillSurfaceLook.Default;

    /// <summary>A motion: evaluated on the render thread, or (key-frame driver) its place and size on 1 ms key frames.</summary>
    public void Run(PillMotion motion, PillParts parts, double now)
    {
        if (Driver == PillDriverKind.KeyFrames && !motion.AtRest)
        {
            Send(new PillSurfaceFrame(Kind, null, motion.At(now), parts, now, Look, Fill: true));
            if (visual is null) return;
            var (x, y, w, h) = Tracks(motion, parts, now);
            PillComposition.Vector3D(visual, "Offset", x, y, 0);
            PillComposition.Vector(visual, "Size", w, h);
            return;
        }
        Hold(motion.AtRest ? motion.Final : motion.At(now), parts, now, motion.AtRest ? null : motion);
    }

    /// <summary>A pose as it is (at rest or a drag frame).</summary>
    public void Hold(PillPose pose, PillParts parts, double now) => Hold(pose, parts, now, null);

    /// <summary>A new look (glass attached or not, Stop's hover or focus, high contrast, which layers this window draws).</summary>
    public void Restyle(PillSurfaceLook look)
    {
        if (Look == look) return;
        Look = look;
        if (Last is { } last) Send(last with { Look = look });
    }

    private void Hold(PillPose pose, PillParts parts, double now, PillMotion? motion)
    {
        if (Driver == PillDriverKind.KeyFrames && visual is not null)
        {
            var box = Box(pose, parts);
            PillComposition.Set(visual, "Offset", new Vector3D(box.X, box.Y, 0));
            PillComposition.SetSize(visual, new Vector(box.Width, box.Height));
            Send(new PillSurfaceFrame(Kind, null, pose, parts, now, Look, Fill: true));
            return;
        }
        Send(new PillSurfaceFrame(Kind, motion, pose, parts, now, Look, Fill: false));
    }

    // The key-frame driver's box: the drawn shape plus its padding, in the surface control's coordinates (the anchor).
    private Rect Box(PillPose pose, PillParts parts)
    {
        var shape = PillSurfacePainter.ShapeOf(Kind, pose, parts);
        var pad = PillSurfacePainter.PadFor(Kind);
        return new Rect(shape.X - pad, shape.Y - pad, shape.Width + (2 * pad), shape.Height + (2 * pad));
    }

    private (PillTrack X, PillTrack Y, PillTrack W, PillTrack H) Tracks(PillMotion motion, PillParts parts, double now)
    {
        var pad = PillSurfacePainter.PadFor(Kind);
        var x = PillTrack.Sample(motion, p => PillSurfacePainter.ShapeOf(Kind, p, parts).X - pad, now);
        var y = PillTrack.Sample(motion, p => PillSurfacePainter.ShapeOf(Kind, p, parts).Y - pad, now);
        var w = PillTrack.Sample(motion, p => PillSurfacePainter.ShapeOf(Kind, p, parts).Width + (2 * pad), now);
        var h = PillTrack.Sample(motion, p => PillSurfacePainter.ShapeOf(Kind, p, parts).Height + (2 * pad), now);
        return (x, y, w, h);
    }

    private void Send(PillSurfaceFrame frame)
    {
        Last = frame;
        if (visual is null) { pending = frame; return; }
        visual.SendHandlerMessage(frame);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        visual = host.Compositor.CreateCustomVisual(handler);
        if (Driver == PillDriverKind.RenderThread)
        {
            var area = AreaOf(Kind);
            visual.Offset = new Vector3D(area.X, area.Y, 0);
            visual.Size = new Vector(area.Width, area.Height);
        }
        ElementComposition.SetElementChildVisual(this, visual);
        if (pending is { } frame)
        {
            pending = null;
            if (frame.Fill) Hold(frame.Pose, frame.Parts, frame.SentAt, null);
            else visual.SendHandlerMessage(frame);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (visual is null) return;
        ElementComposition.SetElementChildVisual(this, null);
        pending = Last;
        visual = null;
    }
}

/// <summary>The surfaces' handler: the painter at the composition frame time, frames asked for only while a motion runs.</summary>
internal sealed class PillSurfaceHandler(PillSurfacePainter painter) : CompositionCustomVisualHandler
{
    private PillSurfaceFrame? frame;
    private TimeSpan arrived;
    private int frames;

    internal int Frames => Volatile.Read(ref frames);

    public override void OnMessage(object message)
    {
        if (message is not PillSurfaceFrame next) return;
        frame = next;
        arrived = CompositionNow;
        Invalidate();
        if (next.Motion is not null) RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        if (frame is not { Motion: { } motion } current) return;
        Invalidate();
        if (!motion.Done(Time(current))) RegisterForNextAnimationFrameUpdate();
    }

    private double Time(PillSurfaceFrame current) => current.SentAt + (CompositionNow - arrived).TotalSeconds;

    public override Rect GetRenderBounds() => frame is null ? default : new Rect(0, 0, EffectiveSize.X, EffectiveSize.Y);

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (frame is not { } current || context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        var saved = canvas.Save();
        try
        {
            if (current.Fill)
            {
                var pad = PillSurfacePainter.PadFor(current.Kind);
                var size = EffectiveSize;
                var shape = new SKRect((float)pad, (float)pad, (float)(size.X - pad), (float)(size.Y - pad));
                painter.Paint(canvas, current.Kind, shape, current.Pose, current.Look, lease.CurrentOpacity);
            }
            else
            {
                var pose = current.Motion?.At(Time(current)) ?? current.Pose;
                var area = PillSurface.AreaOf(current.Kind);
                canvas.Translate((float)-area.X, (float)-area.Y);
                var shape = PillSurfacePainter.ShapeOf(current.Kind, pose, current.Parts);
                painter.Paint(canvas, current.Kind, new SKRect((float)shape.X, (float)shape.Y, (float)shape.Right, (float)shape.Bottom), pose, current.Look, lease.CurrentOpacity);
            }
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }
        Interlocked.Increment(ref frames);
    }
}

/// <summary>
/// The canvas's glass and Stop, in SkiaSharp (canvas `.pv-g`, `.pv-t b`, `.pb.stp`): the body's two outer shadows (clipped
/// outside the body, as CSS draws them), its tint (the glass tint over the blur, or the dense panel without it), the
/// highlight gradient and the sheen, the three inset rim lines; Stop's silver gradient, its inset highlight and ring, its
/// hover and focus ring. High contrast: system colours, a 2 DIP border, no gradients. Paints and shaders are made once and
/// reused (gradients in a unit square under a scale), so a frame allocates nothing.
/// </summary>
public sealed class PillSurfacePainter : IDisposable
{
    /// <summary>The body surface's padding round its shape: the deepest shadow (18 DIP down, sigma 20) fits.</summary>
    public const double Pad = 72;

    /// <summary>Stop's padding (its focus ring 2 DIP wide, 2 DIP out).</summary>
    public const double StopPad = 6;

    public const double StopHeight = 36;

    public static readonly SKColor Tint = new(26, 26, 30, 128);        // --v2-tint rgba(26,26,30,.5)
    public static readonly SKColor DenseTint = new(22, 22, 26, 214);   // --v2-tint-d rgba(22,22,26,.84)
    public static readonly SKColor SilverHigh = new(0xF6, 0xF6, 0xF8);  // --silver-hi
    public static readonly SKColor SilverLow = new(0xCD, 0xCD, 0xD2);   // --silver-lo

    private static readonly SKColor Rim = new(255, 255, 255, 102);       // --v2-rim .4
    private static readonly SKColor Rim2 = new(255, 255, 255, 36);       // --v2-rim2 .14
    private static readonly SKColor Rim3 = new(255, 255, 255, 26);       // --v2-rim3 .1
    private static readonly SKColor StopRing = new(0, 0, 0, 102);        // --v2-stp-ring .4
    private static readonly SKColor FocusRing = new(255, 255, 255, 204); // .pb:focus-visible .8

    private readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint shadow = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKShader highlight;
    private readonly SKShader dense;
    private readonly SKShader sheen;
    private readonly SKShader silver;
    private readonly SKMaskFilter?[] blurs = new SKMaskFilter?[64];
    private readonly SKRoundRect round = new();
    private readonly SKRoundRect other = new();

    public PillSurfacePainter()
    {
        // .tn: linear-gradient(180deg, hi .15, mid .05 60%, lo .07), in a unit square
        highlight = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 1),
            [new SKColor(255, 255, 255, 38), new SKColor(255, 255, 255, 13), new SKColor(255, 255, 255, 18)], [0, 0.6f, 1], SKShaderTileMode.Clamp);
        // .td: linear-gradient(180deg, dhi .11, dlo .04)
        dense = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 1),
            [new SKColor(255, 255, 255, 28), new SKColor(255, 255, 255, 10)], [0, 1], SKShaderTileMode.Clamp);
        // .sn: radial-gradient(120% 90% at 18% -20%, sheen .16, transparent 55%)
        sheen = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1,
            [new SKColor(255, 255, 255, 41), new SKColor(255, 255, 255, 0)], [0, 0.55f], SKShaderTileMode.Clamp,
            SKMatrix.CreateScale(1.2f, 0.9f).PostConcat(SKMatrix.CreateTranslation(0.18f, -0.2f)));
        silver = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 1), [SilverHigh, SilverLow], [0, 1], SKShaderTileMode.Clamp);
    }

    /// <summary>Layers drawn by the last call (tests).</summary>
    public PillPaint LastPainted { get; private set; }

    public static double PadFor(PillSurfaceKind kind) => kind == PillSurfaceKind.Body ? Pad : StopPad;

    /// <summary>
    /// A surface's shape for a pose: the body in the anchor's coordinates, or Stop's visible capsule in its slot's (the slot
    /// rides the same key frames as Stop's content, so capsule and content move as one).
    /// </summary>
    public static Rect ShapeOf(PillSurfaceKind kind, PillPose pose, PillParts parts)
    {
        if (kind == PillSurfaceKind.Body) return new Rect(pose.LocalLeft, 0, pose.Width, pose.Height);
        return new Rect(0, 0, Math.Max(0, PillPlaces.Of(pose, parts).StopVisible), StopHeight);
    }

    /// <summary>Draws <paramref name="kind"/> filling <paramref name="shape"/> (canvas DIP).</summary>
    public void Paint(SKCanvas canvas, PillSurfaceKind kind, SKRect shape, PillPose pose, PillSurfaceLook look, double opacity)
    {
        LastPainted = PillPaint.None;
        // Stop sits in the content layer, which fades as one (Reduce Motion's arrive and leave); the body carries the pose's own opacity
        var poseOpacity = kind == PillSurfaceKind.Body ? pose.Opacity : 1;
        if (shape.Width <= 0 || shape.Height <= 0 || opacity <= 0 || poseOpacity <= 0) return;
        var alpha = (float)Math.Clamp(opacity * poseOpacity, 0, 1);
        if (kind == PillSurfaceKind.Body) PaintBody(canvas, shape, pose, look, alpha);
        else if (look.Paint.HasFlag(PillPaint.Content)) PaintStop(canvas, shape, look, alpha);
    }

    private void PaintBody(SKCanvas canvas, SKRect body, PillPose pose, PillSurfaceLook look, float alpha)
    {
        var radius = (float)Math.Min(PillShape.Radius, Math.Min(body.Width, body.Height) / 2);
        round.SetRect(body, radius, radius);
        if (look.Paint.HasFlag(PillPaint.Shadow) && !look.HighContrast)
        {
            // 0 1px 2px .45 and 0 10px 26px -8px .5 (deeper while lifted: 18px down, 40px, .55), outside the body only
            var lift = (float)Math.Clamp((pose.Lift - 1) / (PillMotion.LiftScale - 1), 0, 1);
            var saved = canvas.Save();
            canvas.ClipRoundRect(round, SKClipOperation.Difference, antialias: true);
            Shadow(canvas, body, radius, 0, 1, 1f, 0.45f * alpha);
            Shadow(canvas, body, radius, 8, 10 + (8 * lift), 13 + (7 * lift), (0.5f + (0.05f * lift)) * alpha);
            canvas.RestoreToCount(saved);
            LastPainted |= PillPaint.Shadow;
        }
        if (!look.Paint.HasFlag(PillPaint.Body)) return;
        LastPainted |= PillPaint.Body;
        if (look.HighContrast)
        {
            fill.Shader = null;
            fill.Color = Alpha(SKColors.Black, alpha);
            canvas.DrawRoundRect(round, fill);
            stroke.Color = Alpha(SKColors.White, alpha);
            stroke.StrokeWidth = 2;
            other.SetRect(Inset(body, 1), radius - 1, radius - 1);
            canvas.DrawRoundRect(other, stroke);
            return;
        }
        var denseness = look.Glass ? Math.Clamp(look.Dense, 0, 1) : 1;
        var bodySaved = canvas.Save();
        canvas.ClipRoundRect(round, SKClipOperation.Intersect, antialias: true);
        fill.Shader = null;
        fill.Color = Alpha(Tint, alpha);
        if (look.Glass) canvas.DrawRect(body, fill);
        if (denseness > 0)
        {
            fill.Color = Alpha(DenseTint, (float)(alpha * denseness));
            canvas.DrawRect(body, fill);
        }
        Unit(canvas, body, highlight, alpha * (float)(1 - denseness));
        if (denseness > 0) Unit(canvas, body, dense, alpha * (float)denseness);
        Unit(canvas, body, sheen, alpha);
        canvas.RestoreToCount(bodySaved);
        // inset rims: 0 .75px rim at the top, 0 -.5px rim3 at the bottom, a .5px rim2 ring
        Crescent(canvas, body, radius, 0.75f, Alpha(Rim, alpha));
        Crescent(canvas, body, radius, -0.5f, Alpha(Rim3, alpha));
        stroke.Shader = null;
        stroke.Color = Alpha(Rim2, alpha);
        stroke.StrokeWidth = 0.5f;
        other.SetRect(Inset(body, 0.25f), radius - 0.25f, radius - 0.25f);
        canvas.DrawRoundRect(other, stroke);
    }

    private void PaintStop(SKCanvas canvas, SKRect capsule, PillSurfaceLook look, float alpha)
    {
        var radius = capsule.Height / 2;
        round.SetRect(capsule, radius, radius);
        LastPainted |= PillPaint.Content;
        if (look.HighContrast)
        {
            fill.Shader = null;
            fill.Color = Alpha(SKColors.Black, alpha);
            canvas.DrawRoundRect(round, fill);
            stroke.Color = Alpha(new SKColor(0x1A, 0xEB, 0xFF), alpha); // the canvas's high-contrast Highlight
            stroke.StrokeWidth = 2;
            other.SetRect(Inset(capsule, 1), radius - 1, radius - 1);
            canvas.DrawRoundRect(other, stroke);
        }
        else
        {
            if (look.Hover)
            {
                fill.Shader = null;
                fill.Color = Alpha(SilverHigh, alpha);
                canvas.DrawRoundRect(round, fill);
            }
            else
            {
                var saved = canvas.Save();
                canvas.ClipRoundRect(round, SKClipOperation.Intersect, antialias: true);
                Unit(canvas, capsule, silver, alpha);
                canvas.RestoreToCount(saved);
            }
            Crescent(canvas, capsule, radius, 0.5f, Alpha(SKColors.White, alpha));
            stroke.Shader = null;
            stroke.Color = Alpha(StopRing, alpha);
            stroke.StrokeWidth = 0.5f;
            other.SetRect(Inset(capsule, 0.25f), radius - 0.25f, radius - 0.25f);
            canvas.DrawRoundRect(other, stroke);
        }
        if (look.Focused)
        {
            stroke.Color = Alpha(FocusRing, alpha);
            stroke.StrokeWidth = 2;
            other.SetRect(Inset(capsule, -3), radius + 3, radius + 3);
            canvas.DrawRoundRect(other, stroke);
        }
    }

    private void Shadow(SKCanvas canvas, SKRect body, float radius, float spread, float dy, float sigma, float alpha)
    {
        var rect = Inset(body, spread);
        rect.Offset(0, dy);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var r = Math.Max(0, radius - spread);
        other.SetRect(rect, r, r);
        shadow.Color = new SKColor(0, 0, 0, (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)));
        shadow.MaskFilter = Blur(sigma);
        canvas.DrawRoundRect(other, shadow);
        shadow.MaskFilter = null;
    }

    // CSS `inset 0 dy 0 color`: the band inside the shape and outside the shape moved by dy.
    private void Crescent(SKCanvas canvas, SKRect shape, float radius, float dy, SKColor color)
    {
        var saved = canvas.Save();
        canvas.ClipRoundRect(round, SKClipOperation.Intersect, antialias: true);
        var moved = shape;
        moved.Offset(0, dy);
        other.SetRect(moved, radius, radius);
        canvas.ClipRoundRect(other, SKClipOperation.Difference, antialias: true);
        fill.Shader = null;
        fill.Color = color;
        canvas.DrawRect(shape, fill);
        canvas.RestoreToCount(saved);
    }

    // A unit-square shader drawn over `rect` (the gradients scale with the shape, like the canvas's 100 px tiles).
    private void Unit(SKCanvas canvas, SKRect rect, SKShader shader, float alpha)
    {
        if (alpha <= 0) return;
        var saved = canvas.Save();
        canvas.Translate(rect.Left, rect.Top);
        canvas.Scale(rect.Width, rect.Height);
        fill.Shader = shader;
        fill.Color = new SKColor(255, 255, 255, (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)));
        canvas.DrawRect(0, 0, 1, 1, fill);
        fill.Shader = null;
        canvas.RestoreToCount(saved);
    }

    private SKMaskFilter Blur(float sigma)
    {
        var slot = Math.Clamp((int)Math.Round(sigma * 2), 1, blurs.Length - 1);
        return blurs[slot] ??= SKMaskFilter.CreateBlur(SKBlurStyle.Normal, slot / 2f);
    }

    private static SKRect Inset(SKRect rect, float by) => new(rect.Left + by, rect.Top + by, rect.Right - by, rect.Bottom - by);

    private static SKColor Alpha(SKColor color, float alpha) => color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(alpha, 0, 1)));

    public void Dispose()
    {
        fill.Dispose();
        stroke.Dispose();
        shadow.Dispose();
        highlight.Dispose();
        dense.Dispose();
        sheen.Dispose();
        silver.Dispose();
        foreach (var blur in blurs) blur?.Dispose();
        round.Dispose();
        other.Dispose();
    }
}

/// <summary>The parts of a pill the driver moves (ControlPill's), as controls.</summary>
public interface IPillTargets
{
    /// <summary>The stage: the anchor's travel and the scale.</summary>
    Control Stage { get; }

    /// <summary>The content layer over the body (row, group, Stop): Reduce Motion's arrive and leave fade it as one.</summary>
    Control Content { get; }

    /// <summary>The hint and the rows under the pill: they fade with the content.</summary>
    Control Below { get; }

    /// <summary>The row (mark, glyph, text) from the body's left edge.</summary>
    Control Row { get; }

    Control Group { get; }

    /// <summary>Stop's slot: its capsule surface, its content clip and its hit area, from the capsule's left edge.</summary>
    Control StopSlot { get; }

    /// <summary>Stop's content clip (its cap slides out under it).</summary>
    Control StopClip { get; }

    /// <summary>The text column's clip.</summary>
    Control TextClip { get; }

    /// <summary>The live glyph's box (the dot and the pause bars) in the row; it rides into the dot's corner (ED-NEW-pcctl-36). Null: none.</summary>
    Control? Glyph => null;

    PillSurface Body { get; }

    PillSurface StopSurface { get; }

    /// <summary>
    /// The card's clip under the header (ED-NEW-pcctl-26): a fixed layout size; its visible size follows the body below the
    /// header, so the card is revealed by the morph, never faded. Null: no card.
    /// </summary>
    Control? CardClip => null;

    /// <summary>The card inside the clip: it holds its place under the anchor while the clip's edge moves.</summary>
    Control? CardContent => null;

    /// <summary>Lays the controls out at <paramref name="pose"/>'s places (the stage at its anchor), once per change.</summary>
    void Layout(PillPose pose, PillPlaces places);
}

/// <summary>
/// Moves one pill's parts through a motion. <see cref="Run"/> lays out the final places once and starts every part's key
/// frames and both surfaces in the same UI-thread job (one composition commit); <see cref="Hold"/> sets a pose directly.
/// </summary>
public sealed class PillMotionDriver(IPillTargets targets, IPillAnimator animator)
{
    /// <summary>The text column's clip keeps 6 DIP above and below the lines (canvas `inset(-6px ...)`).</summary>
    public const double TextClipBleed = 6;

    /// <summary>Stop's content clip ends this far inside its capsule's right end, so the sliding cap never leaves the capsule.</summary>
    public const double StopClipInset = 5;

    /// <summary>The two lines (18 and 17 DIP) and the bleed above and below them.</summary>
    public const double TextClipHeight = 35 + (2 * TextClipBleed);

    public PillMotion? Current { get; private set; }

    public PillParts Parts { get; private set; }

    /// <summary>Starts <paramref name="motion"/> at <paramref name="now"/>: one layout at the final pose, then key frames and surfaces.</summary>
    public void Run(PillMotion motion, PillParts parts, double now)
    {
        Current = motion;
        Parts = parts;
        var end = motion.Final;
        var places = PillPlaces.Of(end, parts);
        targets.Layout(end, places);
        if (motion.AtRest || motion.Done(now))
        {
            Rest(end, parts, now);
            return;
        }
        var until = motion.DoneAt();
        PillTrack Track(Func<PillPose, double> value) => PillTrack.Sample(motion, value, now, until);

        // The stage: the anchor's travel from the final layout (FLIP), with the scale's offset folded in.
        animator.Translate(targets.Stage,
            Track(p => p.Anchor.X + p.ScaleOffset.X - end.Anchor.X),
            Track(p => p.Anchor.Y + p.ScaleOffset.Y - end.Anchor.Y));
        var scale = Track(p => p.Scale);
        if (scale.Constant) animator.SetScale(targets.Stage, scale.Last);
        else animator.Scale(targets.Stage, scale);
        var opacity = Track(p => p.Opacity);
        if (opacity.Constant)
        {
            animator.SetOpacity(targets.Content, opacity.Last);
            animator.SetOpacity(targets.Below, opacity.Last);
        }
        else
        {
            animator.Fade(targets.Content, opacity);
            animator.Fade(targets.Below, opacity);
        }

        var zero = Track(_ => 0);
        animator.Translate(targets.Row, Track(p => PillPlaces.Of(p, parts).RowLeft - places.RowLeft), Track(p => PillPlaces.Of(p, parts).RowTop - places.RowTop));
        if (targets.Glyph is { } glyph)
            animator.Translate(glyph, Track(p => PillPlaces.Of(p, parts).Glyph.X - places.Glyph.X), Track(p => PillPlaces.Of(p, parts).Glyph.Y - places.Glyph.Y));
        animator.Translate(targets.Group, Track(p => PillPlaces.Of(p, parts).GroupLeft - places.GroupLeft), zero);
        animator.Translate(targets.StopSlot, Track(p => PillPlaces.Of(p, parts).StopLeft - places.StopLeft), zero);
        animator.Resize(targets.StopClip, Track(p => Math.Max(0, PillPlaces.Of(p, parts).StopVisible - StopClipInset)), Track(_ => PillSurfacePainter.StopHeight));
        animator.Resize(targets.TextClip, Track(p => PillPlaces.Of(p, parts).TextClip), Track(_ => TextClipHeight));
        if (targets.CardClip is { } cardClip && targets.CardContent is { } card)
        {
            // the clip's left edge follows the body's; the card counter-moves so it stays under the anchor; on an expand it
            // rises its last 6 DIP as the clip opens (DESIGN.md 10.4's rise, kept without the fade)
            var rising = end.Height > PillLayout.Height + 0.5 && motion.At(now).Height < end.Height - 0.5;
            animator.Translate(cardClip, Track(p => PillPlaces.Of(p, parts).BodyLeft - places.BodyLeft), zero);
            animator.Translate(card,
                Track(p => places.BodyLeft - PillPlaces.Of(p, parts).BodyLeft),
                rising ? Track(p => PillMorph.BodyRise * (1 - Math.Clamp((p.Height - PillLayout.Height) / (end.Height - PillLayout.Height), 0, 1))) : zero);
            animator.Resize(cardClip, Track(p => p.Width), Track(p => Math.Max(0, p.Height - PillLayout.Height)));
        }
        targets.Body.Run(motion, parts, now);
        targets.StopSurface.Run(motion, parts, now);
    }

    /// <summary>Shows <paramref name="pose"/> at once (a drag frame, or the end of a motion).</summary>
    public void Hold(PillPose pose, PillParts parts, double now)
    {
        Current = PillMotion.Still(pose, now);
        Parts = parts;
        targets.Layout(pose, PillPlaces.Of(pose, parts));
        Rest(pose, parts, now);
    }

    private void Rest(PillPose pose, PillParts parts, double now)
    {
        var places = PillPlaces.Of(pose, parts);
        animator.SetTranslation(targets.Stage, pose.ScaleOffset.X, pose.ScaleOffset.Y);
        animator.SetScale(targets.Stage, pose.Scale);
        animator.SetOpacity(targets.Content, pose.Opacity);
        animator.SetOpacity(targets.Below, pose.Opacity);
        animator.SetTranslation(targets.Row, 0, 0);
        if (targets.Glyph is { } glyph) animator.SetTranslation(glyph, 0, 0);
        animator.SetTranslation(targets.Group, 0, 0);
        animator.SetTranslation(targets.StopSlot, 0, 0);
        animator.SetSize(targets.StopClip, Math.Max(0, places.StopVisible - StopClipInset), PillSurfacePainter.StopHeight);
        animator.SetSize(targets.TextClip, places.TextClip, TextClipHeight);
        if (targets.CardClip is { } cardClip && targets.CardContent is { } card)
        {
            animator.SetTranslation(cardClip, 0, 0);
            animator.SetTranslation(card, 0, 0);
            animator.SetSize(cardClip, pose.Width, Math.Max(0, pose.Height - PillLayout.Height));
        }
        targets.Body.Hold(pose, parts, now);
        targets.StopSurface.Hold(pose, parts, now);
    }
}
