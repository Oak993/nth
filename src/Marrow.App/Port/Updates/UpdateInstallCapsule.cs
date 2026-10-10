// PORT-ONLY: nth motion pack step 10, "Download and update", the About install capsule (OD-0136 amendment 2, ED-NEW-nth-motion). A 168 x 26 capsule (r 13) in the pack's track colour. The fill is a clip window: its
// right end is a round nose at p * W (the window is p - 1 widths to the left of rest, its content p - 1 widths back, so only the window's edge moves) and carries the step 3 option B metal (--ftop to --fbot, lighter on top).
// The label is drawn twice, --text on the track and --on inside the window, so it changes colour exactly where the fill passes under it. The light runs inside the fill only (2.6 s, as step 3). At 100 % the light
// stops and the label cross-fades (0.25 s) to the done label. The value is the step 3 spring. All of it is drawn on the render thread (no UI-thread frame loop).
using Avalonia;
using Marrow.UI.Port.Nth;
using SkiaSharp;

namespace Marrow.App.Port.Updates;

/// <summary>The capsule's state: its value (a spring toward the install's fraction) and, once the install is complete, when (clock seconds).</summary>
public sealed record UpdateInstallCapsuleState(NthSpring Value, double? DoneAt);

/// <summary>Step 10's install capsule. Set <see cref="Value"/> (0..1) while the install runs and <see cref="IsDone"/> when it is complete.</summary>
public sealed class UpdateInstallCapsule : NthMotionControl
{
    public const double CapsuleWidth = 168, CapsuleHeight = 26;

    /// <summary>The label while the install runs: "Downloading" and the percentage (the words the Updates page already uses for a running install).</summary>
    public static string WorkingLabel(int percent) => "Downloading " + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>The label once the install is complete: the standing the pill itself takes next ("Up to date"); this app relaunches by itself, so the percentage only reaches 100 % in the new version.</summary>
    public const string DoneLabel = "Up to date";

    private NthSpring value = NthSpring.At(0);
    private double? doneAt;

    public UpdateInstallCapsule() { Width = CapsuleWidth; Height = CapsuleHeight; }

    /// <summary>The value the fill springs to (0..1).</summary>
    public double Value
    {
        get => value.Target;
        set
        {
            var target = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (target == this.value.Target) return;
            this.value = this.value.Retarget(target, Now, Calm);
            Send();
        }
    }

    /// <summary>Show <paramref name="target"/> at once (a capsule shown while the install is already part way).</summary>
    public void Jump(double target)
    {
        value = NthSpring.At(Math.Clamp(target, 0, 1));
        Send();
    }

    /// <summary>The fill as drawn now (tests).</summary>
    public double Shown => value.Value(Now);

    /// <summary>The install is complete: the light stops and the label cross-fades to <see cref="DoneLabel"/>.</summary>
    public bool IsDone
    {
        get => doneAt is not null;
        set { if (value == IsDone) return; doneAt = value ? Now : null; Send(); }
    }

    /// <summary>Done, with the done label already in place (a capsule shown after the install finished).</summary>
    public void ShowDone() { doneAt = double.NegativeInfinity; Send(); }

    protected override object? State => new UpdateInstallCapsuleState(value, doneAt);

    protected override NthPainter CreatePainter() => new UpdateInstallCapsulePainter();
}

/// <summary>Draws step 10's install capsule.</summary>
public sealed class UpdateInstallCapsulePainter : NthSkiaPainter
{
    private const double LabelSize = 11.5, FadeSeconds = .25;

    private readonly SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath capsule = new(), window = new();
    private readonly SKFont font = new(TaskbarBadge.Typeface, (float)LabelSize) { Edging = SKFontEdging.Antialias, Subpixel = true, Hinting = SKFontHinting.None };
    private SKShader? fill, light;
    private (double Height, bool Dark) fillFor = (-1, false);
    private (double Half, bool Dark) lightFor = (-1, false);
    private int labelPercent = -1;
    private string working = "";
    private float baseline, workingWidth;

    public override bool Moving(NthFrame frame, double now)
    {
        if (frame.State is not UpdateInstallCapsuleState state) return false;
        if (state.DoneAt is { } done) return now - done < FadeSeconds + .05 || !state.Value.Settled(now);
        if (!state.Value.Settled(now)) return true;
        var v = state.Value.Value(now);
        return !frame.Calm && v > 0 && v < .999; // the light keeps passing inside a partial fill
    }

    /// <summary>The window's shape: a capsule of the capsule's height between <paramref name="x0"/> and <paramref name="x1"/> (its right end is the fill's round nose).</summary>
    private static void Capsule(SKPath path, double x0, double x1, double h)
    {
        path.Rewind();
        var r = (float)(h / 2);
        path.AddRoundRect(new SKRect((float)x0, 0, (float)Math.Max(x1, x0 + h), (float)h), r, r);
    }

    private SKShader FillShader(double h, NthPalette palette, bool dark)
    {
        if (fill is null || fillFor != (h, dark))
        {
            fill?.Dispose();
            fill = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, (float)h), [palette.FillTop, palette.FillBottom], null, SKShaderTileMode.Clamp);
            fillFor = (h, dark);
        }
        return fill;
    }

    private SKShader LightShader(double half, NthPalette palette, bool dark)
    {
        if (light is null || lightFor != (half, dark))
        {
            light?.Dispose();
            var c = palette.FillLight;
            light = SKShader.CreateLinearGradient(new SKPoint((float)-half, 0), new SKPoint((float)half, 0),
                [c.WithAlpha(0), c, c.WithAlpha(0)], [0, .5f, 1], SKShaderTileMode.Clamp);
            lightFor = (half, dark);
        }
        return light;
    }

    // the working label for this percentage, measured once per change
    private string Working(int percent, double h)
    {
        if (percent != labelPercent)
        {
            labelPercent = percent;
            working = UpdateInstallCapsule.WorkingLabel(percent);
            workingWidth = font.MeasureText(working);
            font.GetFontMetrics(out var metrics);
            baseline = (float)(h / 2 - (metrics.Ascent + metrics.Descent) / 2);
        }
        return working;
    }

    private void Label(SKCanvas canvas, string text, double centre, SKColor ink, double alpha)
    {
        if (alpha <= 0) return;
        paint.Shader = null;
        paint.Color = ink.WithAlpha((byte)Math.Round(ink.Alpha * NthEase.Clamp01(alpha)));
        canvas.DrawText(text, (float)centre, baseline, SKTextAlign.Center, font, paint);
    }

    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not UpdateInstallCapsuleState state) return;
        double w = frame.Size.Width, h = frame.Size.Height;
        if (w <= 0 || h <= 0) return;
        var palette = frame.Palette;
        var t = now - frame.Started;
        var p = Math.Min(1, state.Value.Value(now));
        var percent = (int)Math.Round(p * 100, MidpointRounding.AwayFromZero);
        var doneSeconds = state.DoneAt is { } at ? now - at : (double?)null;
        // .capw .lab>span: opacity .25s (CSS ease); done shows the last span and hides the first
        var doneMix = doneSeconds is { } d ? NthEase.Css.Ease(d / FadeSeconds) : 0;
        var text = Working(percent, h);
        var centre = w / 2;

        paint.Shader = null; paint.Color = palette.Track;
        Capsule(capsule, 0, w, h);
        canvas.DrawPath(capsule, paint);
        canvas.Save();
        canvas.ClipPath(capsule, SKClipOperation.Intersect, true);
        Label(canvas, text, centre, palette.Text, 1 - doneMix);
        Label(canvas, UpdateInstallCapsule.DoneLabel, centre, palette.Text, doneMix);

        // the clip window: x = (p - 1) W .. p W, r = h / 2; its content is the fill and the label in --on, which stays where the label is
        var right = p * w;
        if (right > .3)
        {
            Capsule(window, right - w, right, h);
            canvas.Save();
            canvas.ClipPath(window, SKClipOperation.Intersect, true);
            paint.Shader = FillShader(h, palette, frame.Dark);
            paint.Color = SKColors.White;
            canvas.DrawRect(new SKRect(0, 0, (float)w, (float)h), paint);
            if (doneSeconds is null && NthMotion.BarLight(t, right, w, p, frame.Calm) is { } lightCentre)
            {
                var half = NthMotion.BarLightHalf(w);
                canvas.Save();
                canvas.Translate((float)lightCentre, 0);
                paint.Shader = LightShader(half, palette, frame.Dark);
                canvas.DrawRect(new SKRect((float)-half, 0, (float)half, (float)h), paint);
                canvas.Restore();
            }
            paint.Shader = null;
            Label(canvas, text, centre, palette.On, 1 - doneMix);
            Label(canvas, UpdateInstallCapsule.DoneLabel, centre, palette.On, doneMix);
            canvas.Restore();
        }
        paint.Shader = null;
        canvas.Restore();
    }

    public override void Dispose()
    {
        paint.Dispose(); capsule.Dispose(); window.Dispose(); font.Dispose(); fill?.Dispose(); light?.Dispose();
        base.Dispose();
    }
}
