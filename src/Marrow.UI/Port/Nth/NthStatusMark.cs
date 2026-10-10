// PORT-ONLY: OD-0136 amendment 2, steps 8, 11 and 15 (ED-NEW-nth-motion). The status slot of a row that waits, works and is
// done: waiting is an 8 px hollow ring (1.5 px --text3), working is the step 1 spinner, done is either the tool row's tick
// (step 8: drawn in over 0.32 s, 1.8 px --text2, round caps) or the step 4 disc with its tick. Changes cross-fade (0.2 s; the
// spinner leaving a tool row fades in 0.18 s). One visual, so a row never stacks three controls to swap them.
using Avalonia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

public enum NthStatus { None, Waiting, Working, Done }

/// <summary>How "done" looks: the tool row's tick (step 8) or the ring's disc and tick (steps 4, 6, 15).</summary>
public enum NthDoneMark { Tick, Disc }

public sealed record NthStatusState(NthStatus Status, NthStatus Previous, double ChangedAt, double WorkingSince, NthDoneMark DoneMark, double MarkSize);

/// <summary>A 16 px status slot (steps 8, 11, 15). Set <see cref="Status"/>; the change animates.</summary>
public sealed class NthStatusMark : NthMotionControl
{
    private NthStatus status = NthStatus.None, previous = NthStatus.None;
    private double changedAt = double.NegativeInfinity, workingSince = double.NegativeInfinity;
    private NthDoneMark doneMark;
    private double markSize = 14;

    public NthStatusMark() { Width = 16; Height = 16; }

    public NthStatus Status
    {
        get => status;
        set
        {
            if (value == status) return;
            // a slot shown for the first time shows its state in place (a done row that scrolls in does not redraw its tick)
            previous = status; status = value; changedAt = IsAttachedToVisualTree() ? Now : double.NegativeInfinity;
            if (value == NthStatus.Working) workingSince = changedAt;
            Send();
        }
    }

    private bool IsAttachedToVisualTree() => VisualRoot is not null;

    /// <summary>The done look (the tool row's tick by default).</summary>
    public NthDoneMark DoneMark { get => doneMark; set { doneMark = value; Send(); } }

    /// <summary>The spinner's size inside the slot: 14 (tool rows, first run) or 12 (MCP rows).</summary>
    public double MarkSize { get => markSize; set { markSize = value; Send(); } }

    protected override object? State => new NthStatusState(status, previous, changedAt, workingSince, doneMark, markSize);

    protected override NthPainter CreatePainter() => new NthStatusPainter();
}

public sealed class NthStatusPainter : NthSkiaPainter
{
    private readonly NthSpinnerPainter spinner = new();
    private readonly NthRingPainter ring = new();
    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath path = new();

    public override bool Moving(NthFrame frame, double now) =>
        frame.State is NthStatusState s && (s.Status == NthStatus.Working || now - s.ChangedAt < .5);

    public override Rect Bounds(NthFrame frame) => new Rect(frame.Size).Inflate(1);

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthStatusState s) return;
        var since = now - s.ChangedAt;
        canvas.Save();
        canvas.Translate((float)(frame.Size.Width / 2), (float)(frame.Size.Height / 2));
        if (s.Previous != NthStatus.None && since < .2)
        {
            var leaving = s.Previous == NthStatus.Working && s.Status == NthStatus.Done && s.DoneMark == NthDoneMark.Tick
                ? NthMotion.FadeOut(since, .18) : NthMotion.FadeOut(since);
            DrawState(canvas, frame, s, s.Previous, leaving, double.PositiveInfinity, now);
        }
        var entering = s.Status == NthStatus.Done && s.DoneMark == NthDoneMark.Tick ? 1 : NthMotion.FadeIn(since);
        DrawState(canvas, frame, s, s.Status, entering, since, now);
        canvas.Restore();
    }

    private void DrawState(SKCanvas canvas, NthFrame frame, NthStatusState s, NthStatus status, double opacity, double since, double now)
    {
        if (opacity <= 0) return;
        var palette = frame.Palette;
        switch (status)
        {
            case NthStatus.Waiting:
                paint.StrokeWidth = 1.5f;
                paint.Color = palette.Text3.WithAlpha((byte)Math.Round(255 * opacity));
                canvas.DrawCircle(0, 0, 3.25f, paint); // an 8 px ring: the 1.5 px border inside it (CSS inset box-shadow)
                break;
            case NthStatus.Working:
                // the spinner keeps its own clock from when work began (its enter fade), and keeps turning while it fades out;
                // one shown already working counts from when the slot was first shown
                var began = double.IsNegativeInfinity(s.WorkingSince) ? frame.Started : s.WorkingSince;
                spinner.DrawCentred(canvas, s.MarkSize, palette, frame.Calm, now - began, opacity);
                break;
            case NthStatus.Done when s.DoneMark == NthDoneMark.Disc:
                ring.DrawCentred(canvas, 14, palette, frame.Calm, 1, double.IsPositiveInfinity(since) ? 10 : since, opacity);
                break;
            case NthStatus.Done:
                var (drawn, alpha) = NthMotion.RowTickIn(double.IsPositiveInfinity(since) ? 10 : since, frame.Calm);
                paint.StrokeWidth = (float)NthMotion.RowTickStroke;
                paint.Color = palette.Text2.WithAlpha((byte)Math.Round(255 * alpha * opacity));
                NthTicks.Partial(path, NthMotion.RowTick, 1, -7, -7, drawn); // the 14 px box, centred
                canvas.DrawPath(path, paint);
                break;
        }
    }

    public override void Dispose()
    {
        spinner.Dispose(); ring.Dispose(); paint.Dispose(); path.Dispose();
        base.Dispose();
    }
}
