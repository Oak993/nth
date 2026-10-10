// PORT-ONLY: OD-0136 amendment 2, step 1 option B "Tile trace" (ED-NEW-nth-motion). The one spinner for every small wait
// (12 to 20 px): a short line runs round the nth tile shape (superellipse n = 4), stretching on the long sides and easing
// through the corners, over the tile drawn in --track. Solid colour, no bead, no glow. Replaces SystemSpinner, WorkingSpinner,
// VoiceSpinner and the sidebar's MiniSpinner (not Hydra's).
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>Step 1 option B. Size it with Width/Height (12 to 20); 16 when the site gives none.</summary>
public sealed class NthSpinner : NthMotionControl
{
    /// <summary>The spinner's colour pair: <see cref="NthSpinnerInk.Mark"/> (--mark on --track) or the image puck's white (step 13).</summary>
    public NthSpinnerInk Ink { get => ink; set { ink = value; Send(); } }
    private NthSpinnerInk ink;

    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);

    protected override object? State => ink;

    protected override NthPainter CreatePainter() => new NthSpinnerPainter();
}

public enum NthSpinnerInk { Mark, Puck }

/// <summary>Draws step 1 option B. The tile path and its measure are made once per size; the moving line reuses one path.</summary>
public sealed class NthSpinnerPainter : NthSkiaPainter
{
    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath tile = new();
    private readonly SKPath line = new();
    private SKPathMeasure? measure;
    private double builtFor = -1;
    private float length;

    /// <summary>The tile track's length for the size last drawn (tests).</summary>
    public double TrackLength => length;

    public override bool Moving(NthFrame frame, double now) => true; // it turns (or, calm, breathes) for as long as it is shown

    private void Build(double size)
    {
        if (size == builtFor) return;
        builtFor = size;
        var half = NthMotion.TileHalf(size);
        tile.Rewind();
        for (var i = 0; i < NthMotion.TilePoints; i++)
        {
            var p = NthMotion.TilePoint(half, i);
            if (i == 0) tile.MoveTo(p); else tile.LineTo(p);
        }
        tile.Close();
        measure?.Dispose();
        measure = new SKPathMeasure(tile, true);
        length = measure.Length;
    }

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        var size = Math.Min(frame.Size.Width, frame.Size.Height);
        if (size <= 0) return;
        canvas.Save();
        canvas.Translate((float)(frame.Size.Width / 2), (float)(frame.Size.Height / 2));
        DrawCentred(canvas, size, frame.Palette, frame.Calm, now - frame.Started, 1, frame.State is NthSpinnerInk.Puck);
        canvas.Restore();
    }

    /// <summary>The spinner of <paramref name="size"/> centred on the canvas origin, <paramref name="seconds"/> after it started.</summary>
    public void DrawCentred(SKCanvas canvas, double size, NthPalette palette, bool calm, double seconds, double opacity, bool puck = false)
    {
        if (size <= 0 || opacity <= 0) return;
        Build(size);
        var trace = NthMotion.Spinner(seconds, length, calm);
        paint.StrokeWidth = (float)NthMotion.SpinnerStroke(size);
        // the page: the whole spinner fades in (track too); calm, only the line breathes
        var track = puck ? NthPalette.PuckTrack : palette.Track;
        paint.Color = track.WithAlpha((byte)Math.Round(track.Alpha * opacity * (calm ? 1 : NthEase.Clamp01(trace.Alpha))));
        canvas.DrawPath(tile, paint);
        line.Rewind();
        var end = trace.Start + trace.Length;
        measure!.GetSegment((float)trace.Start, (float)Math.Min(end, length), line, true);
        if (end > length) measure.GetSegment(0, (float)(end - length), line, false);
        var mark = puck ? NthPalette.PuckMark : palette.Mark;
        paint.Color = mark.WithAlpha((byte)Math.Round(mark.Alpha * opacity * NthEase.Clamp01(trace.Alpha)));
        canvas.DrawPath(line, paint);
    }

    public override void Dispose()
    {
        paint.Dispose(); tile.Dispose(); line.Dispose(); measure?.Dispose();
        base.Dispose();
    }
}
