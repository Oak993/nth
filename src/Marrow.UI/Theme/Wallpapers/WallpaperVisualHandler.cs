using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>Owns all mutable shader and cache state on the render thread.</summary>
internal sealed class WallpaperVisualHandler(AmbientMotionController? ambient = null) : CompositionCustomVisualHandler, IFrameSource
{
    private double interactionUntil;
    private bool Interaction => Now < interactionUntil || renderer?.HasTransitions == true;
    private double Now => snapshot is { } s ? s.Clock.GetTimestamp() / (double)s.Clock.TimestampFrequency : AmbientWindowState.Now;
    private WallpaperSnapshot? snapshot;
    private WallpaperRenderer? renderer;
    private WallpaperPlayback? playback;
    private Action<TimeSpan>? frame;
    private bool registered, stopped;
    private readonly SKPaint opacityPaint = new();

    public override void OnMessage(object message)
    {
        if (message is WallpaperVisualStop)
        {
            stopped = true; playback?.Dispose(); renderer?.Dispose(); opacityPaint.Dispose(); frame = null; return;
        }
        if (stopped) return;
        if (ReferenceEquals(message, ambient)) { ambient?.Sample(Now, Interaction); if (frame is { } pending) RequestFrame(pending); return; }
        if (message is WallpaperWake) { interactionUntil = Now + .6; playback?.Wake(); return; }
        if (message is not WallpaperSnapshot next) return;
        if (snapshot is not null && (snapshot.Selection != next.Selection || snapshot.Dark != next.Dark)) interactionUntil = Now + .6;
        if (renderer is null)
        {
            renderer = new(next.Selection, next.Dark, next.Environment); playback = new(renderer, this, Invalidate);
        }
        else
        {
            if (next.Environment is { } environment && snapshot?.Environment != environment) renderer.SetEnvironment(environment);
            renderer.Set(next.Selection, next.Dark);
        }
        snapshot = next; renderer.Visible = next.Visible; renderer.ReduceMotion = next.ReduceMotion;
        playback!.Refresh(); Invalidate();
    }

    public void RequestFrame(Action<TimeSpan> callback)
    {
        frame = callback;
        if (registered || stopped) return;
        if (ambient is not null && !ambient.Request(Now, Interaction)) return;
        using var request = AuroraBootTrace.Begin("Wallpaper: frame request");
        registered = true; RegisterForNextAnimationFrameUpdate();
    }
    public void CancelFrame(Action<TimeSpan> callback) { if (ReferenceEquals(frame, callback)) frame = null; }
    public override void OnAnimationFrameUpdate()
    {
        using var work = AuroraBootTrace.Begin("Wallpaper: compositor frame");
        registered = false; var callback = frame; frame = null;
        if (stopped || snapshot is not { } s) return;
        ambient?.Sample(Now, Interaction);
        if (ambient is not null && !ambient.Available) { frame = callback; return; }
        // ED-NEW-frame-pacing: a moving wallpaper advances at the 60 Hz slots of the refreshes, with every other ambient source
        if (ambient is not null && ambient.Pace(CompositionNow.Ticks, Now, Interaction) == AmbientMotionController.Paced.Wait)
        {
            frame = callback; registered = true; RegisterForNextAnimationFrameUpdate(); return;
        }
        callback?.Invoke(TimeSpan.FromSeconds(ambient?.Clock.Time ?? Now));
    }
    public override Rect GetRenderBounds() => snapshot is { } s ? new Rect(s.Size) : default;
    public override void OnRender(ImmediateDrawingContext context)
    {
        using var work = AuroraBootTrace.Begin("Wallpaper: compositor draw");
        if (stopped || renderer is null || snapshot is not { Visible: true } s) return;
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas; var matrix = canvas.TotalMatrix;
        var scale = (float)Math.Max(1, Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY));
        canvas.Save();
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
