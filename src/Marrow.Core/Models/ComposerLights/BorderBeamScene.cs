// PORT-ONLY, owner-ruled addition (OD-0089; ED-251): beam.js's stylesheet generator (bbRot, bbLine, bbSpikes, bbFe, bbRe,
// bbPulseInner, bbPulseOutside; border-beam 1.4.1, Copyright (c) 2026 Jakub Antalik, MIT) turned from CSS into numbers a
// shader draws: every radial-gradient of a layer becomes one ellipse primitive (centre, radii, its colour stops,
// premultiplied), in the layer's paint order; the masks, windows and filters become per-slot parameters. A layer the
// CSS blurs keeps its ellipses and widens them by the blur instead (a cone of radius r blurred by s has the second moment
// of a cone of radius sqrt(r^2 + s^2 / .15), its amplitude scaled to keep its volume), so no blur runs at draw time.

namespace Marrow.Core.Models.ComposerLights;

/// <summary>The structural half of a beam (beamCfg): what a slot's drawing is generated from; any change cross-fades.</summary>
public readonly record struct BeamSlotConfig(BeamType Type, BeamPalette Palette, bool Dark, bool Static, bool Fitted, bool Faithful)
{
    public static BeamSlotConfig For(BorderBeamOptions b, bool dark) =>
        new(b.Type, b.Palette, dark, b.StaticColors || b.Palette == BeamPalette.Mono, b.Fitted, b.Faithful);
}

/// <summary>The beam's eased continuous values (beamVars, registered properties that transition): one set per composer.</summary>
public sealed class BeamValues
{
    public double So, Io, Bo, Br = 1.3, Sat = 1.2, Hb, Hr = 30, Blur = 8, Cb = 3, Bb = 22.5, Sx = 1, Sy = 1, Boost = 1, Spk = 1, R = 22;
}

/// <summary>Layers of a slot, the shader's accumulators.</summary>
public static class BeamLayer
{
    public const int Inner = 0, Stroke = 1, Bloom = 2, OuterCore = 3, OuterBloom = 4;
}

/// <summary>The ellipse primitives of one slot: per ellipse 20 floats (see <see cref="Stride"/>), in paint order, bottom first.</summary>
public sealed class EllipseBuffer
{
    /// <summary>cx, cy, rx, ry; s1, s2, end, layer; c0 rgba; c1 rgba; c2 rgba (premultiplied, 0..1).</summary>
    public const int Stride = 20;
    public const int Capacity = 40;
    public float[] Data { get; } = new float[Stride * Capacity];
    public int Count { get; private set; }

    public void Clear() => Count = 0;

    /// <summary>A gradient c0 at 0, c1 at s1, c2 at s2, transparent at end (shares of the ellipse ray).</summary>
    public void Add(int layer, double cx, double cy, double rx, double ry, CssColor c0, double s1, CssColor c1, double s2, CssColor c2, double end, double amplitude = 1)
    {
        if (Count >= Capacity || rx <= 0 || ry <= 0) return;
        var o = Count * Stride;
        Data[o] = (float)cx; Data[o + 1] = (float)cy; Data[o + 2] = (float)rx; Data[o + 3] = (float)ry;
        Data[o + 4] = (float)s1; Data[o + 5] = (float)s2; Data[o + 6] = (float)end; Data[o + 7] = layer;
        Put(o + 8, c0, amplitude); Put(o + 12, c1, amplitude); Put(o + 16, c2, amplitude);
        Count++;
    }

    /// <summary><c>radial-gradient(ellipse rx ry at cx cy, c, transparent)</c>.</summary>
    public void Add(int layer, double cx, double cy, double rx, double ry, CssColor c, double amplitude = 1) =>
        Add(layer, cx, cy, rx, ry, c, 0, c, 0, c, 1, amplitude);

    private void Put(int o, CssColor c, double amplitude)
    {
        var a = Math.Clamp(c.A, 0, 1) * amplitude;
        Data[o] = (float)(c.R / 255 * a); Data[o + 1] = (float)(c.G / 255 * a); Data[o + 2] = (float)(c.B / 255 * a); Data[o + 3] = (float)a;
    }

    /// <summary>The ellipse at an index, for tests: centre, radii, stops, layer and its first colour (premultiplied).</summary>
    public (double Cx, double Cy, double Rx, double Ry, double S1, double S2, double End, int Layer, double R, double G, double B, double A) At(int i)
    {
        var o = i * Stride;
        return (Data[o], Data[o + 1], Data[o + 2], Data[o + 3], Data[o + 4], Data[o + 5], Data[o + 6], (int)Data[o + 7], Data[o + 8], Data[o + 9], Data[o + 10], Data[o + 11]);
    }
}

/// <summary>Everything one beam slot draws with in one frame (filled by <see cref="BorderBeamScene.Build"/>).</summary>
public sealed class BeamFrame
{
    public readonly EllipseBuffer Ellipses = new();
    public double W, H, ClipRadius, WindowRadius, Scale = 1;
    /// <summary>0 rotate (Large, Small), 1 Line, 2 Pulse Inner, 3 Pulse Outside.</summary>
    public int Mode;
    public bool Fitted, Dark, Small;
    /// <summary>The rotate types' window phase (the beam angle over 360).</summary>
    public double Phase;
    /// <summary>The inner window is w2 (Small) rather than w1.</summary>
    public bool InnerWindowW2;
    /// <summary>The inner edge bands: on, and their depth across (dv) and along (dh), px.</summary>
    public bool Bands; public double BandV, BandH;
    /// <summary>The layers' opacities after CSS's clamp to 0..1.</summary>
    public double InnerOpacity, StrokeOpacity, BloomOpacity, OuterCoreOpacity, OuterBloomOpacity;
    /// <summary>The ring the stroke and bloom are masked to: its background clip radius (r - 1 for rotate and Line, r for the pulses).</summary>
    public double RingRadius;
    /// <summary>The inset shadow (box-shadow inset 0 0 B 1px C): colour and blur sigma (B / 2); alpha 0 when there is none.</summary>
    public CssColor Shadow; public double ShadowSigma;
    /// <summary>Hue rotation (degrees) of the stroke and inner layers, of the bloom, and whether each layer is filtered at all.</summary>
    public double HueStroke, HueBloom; public bool FilterStroke, FilterBloom, HueOnBloom;
    public double Brightness = 1, Saturation = 1;
    /// <summary>The rotate bloom's blur sigma (--bb-blur), the colour of its highlight and of the stroke's highlight.</summary>
    public double BloomSigma; public CssColor HighlightColor;
    /// <summary>Line: the stroke-and-inner mask (MS) and the bloom mask (MB), ellipses at the travelling point on the bottom edge.</summary>
    public double MaskX, MaskSrx, MaskSry, MaskBrx, MaskBry;
    /// <summary>Pulse Outside: the haloes' boxes (inset -10 and -30, scaled .95 x .9 about the centre) and their blur sigmas.</summary>
    public double CoreSigma, OuterBloomSigma;
    /// <summary>The slot's opacity (its cross-fade times the group's fade and Active).</summary>
    public double Alpha;
}

/// <summary>beam.js's generator, as numbers.</summary>
public static class BorderBeamScene
{
    private static BorderBeamTables T => BorderBeamTables.Shared;

    /// <summary>Widens an ellipse by a Gaussian blur (see the file note): new radii and the amplitude that keeps its volume.</summary>
    public static (double Rx, double Ry, double Amplitude) Blurred(double rx, double ry, double sigma)
    {
        if (sigma <= 0) return (rx, ry, 1);
        var bx = Math.Sqrt(rx * rx + sigma * sigma / 0.15);
        var by = Math.Sqrt(ry * ry + sigma * sigma / 0.15);
        return (bx, by, rx * ry / (bx * by));
    }

    // A double-comma colour voids its whole declaration in the faithful 1.4.1 sheet (loops, not LINQ: this runs every frame).
    private static bool AnyBad(IReadOnlyList<BeamBlob> list)
    {
        for (var i = 0; i < list.Count; i++) if (list[i].Color.Bad) return true;
        return false;
    }

    private static bool AnyBad(IReadOnlyList<LineBlob> list)
    {
        for (var i = 0; i < list.Count; i++) if (list[i].Color.Bad) return true;
        return false;
    }

    // per-frame scratch for Line's spikes (the render thread draws every composer, so one set per thread)
    [ThreadStatic] private static (double Wd, double Ht, double Xs, double Lift, CssColor C1, CssColor C2, double Mid, double End)[]? spikeScratch;

    /// <summary>
    /// Fills <paramref name="f"/> for one slot at one frame: the type's layers from the tables, mapped to the composer
    /// (w x h) when fitted, at the clock's phases and the eased values.
    /// </summary>
    public static void Build(BeamFrame f, BeamSlotConfig c, BeamValues v, ComposerLightClock clock, double w, double h)
    {
        f.Ellipses.Clear();
        f.W = w; f.H = h;
        f.ClipRadius = BorderBeamMath.ClampRadius(w, h, v.R);
        f.WindowRadius = double.IsNaN(clock.WindowRadius) ? v.R : clock.WindowRadius;
        f.Fitted = c.Fitted; f.Dark = c.Dark; f.Small = c.Type == BeamType.Small;
        f.Brightness = v.Br; f.Saturation = v.Sat;
        var hueSwing = v.Hb + v.Hr * clock.HueSwing;
        f.HueStroke = hueSwing; f.FilterStroke = !c.Static; f.FilterBloom = true; f.HueOnBloom = false; f.HueBloom = 0;
        f.Bands = false; f.Shadow = CssColor.Transparent; f.ShadowSigma = 0;
        f.OuterCoreOpacity = f.OuterBloomOpacity = 0;
        f.BloomSigma = v.Blur;
        switch (c.Type)
        {
            case BeamType.Line: Line(f, c, v, clock, w, h); break;
            case BeamType.PulseInner: PulseInner(f, c, v, clock, w, h); break;
            case BeamType.PulseOutside: PulseOutside(f, c, v, clock, w, h); break;
            default: Rotate(f, c, v, clock, w, h); break;
        }
    }

    // ---------- md (bo) and sm (co) ----------
    private static void Rotate(BeamFrame f, BeamSlotConfig c, BeamValues v, ComposerLightClock clock, double w, double h)
    {
        var small = c.Type == BeamType.Small;
        var pal = ComposerLightSettings.PaletteName(c.Palette);
        var mono = c.Palette == BeamPalette.Mono;
        f.Mode = 0;
        f.Phase = clock.BeamAngle / 360;
        f.InnerWindowW2 = small;
        f.RingRadius = Math.Max(0, f.ClipRadius - 1);
        f.StrokeOpacity = Math.Clamp(v.So, 0, 1); f.InnerOpacity = Math.Clamp(v.Io, 0, 1); f.BloomOpacity = Math.Clamp(v.Bo, 0, 1);
        f.HighlightColor = c.Dark ? CssColor.White : new CssColor(0, 0, 0, 1);
        f.FilterBloom = true; f.HueOnBloom = false;   // b3: blur, brightness and saturation, no hue
        var k = c.Fitted ? BorderBeamMath.FitK(small ? BeamType.Small : BeamType.Large, w, h) : default;
        var border = small ? T.SmallBorder[pal] : T.LargeBlobs[pal];
        var preset = T.Presets[small ? "sm" : "md"][c.Dark ? "dark" : "light"];
        // the inner blobs (Te / _e): Small's own, or Large's border blobs at .9 with alpha .45 (.225 for Mono)
        var ia = mono ? 0.225 : 0.45;
        var inner = small ? T.SmallInner[pal] : border;
        var innerScale = small ? 1 : 0.9;
        var innerBad = c.Faithful && small && AnyBad(inner);
        if (!innerBad)
            for (var i = inner.Count - 1; i >= 0; i--)
            {
                var b = inner[i];
                var color = small ? b.Color : b.Color.WithAlpha(ia);
                AddBlob(f.Ellipses, BeamLayer.Inner, b, innerScale, color, c.Fitted, k, w, h);
            }
        var strokeBad = c.Faithful && AnyBad(border);
        if (!strokeBad)
            for (var i = border.Count - 1; i >= 0; i--) AddBlob(f.Ellipses, BeamLayer.Stroke, border[i], 1, border[i].Color, c.Fitted, k, w, h);
        f.Shadow = preset.InnerShadow; f.ShadowSigma = (small ? 5 : 9) / 2.0;
        if (!small)
        {
            f.Bands = true;
            f.BandV = c.Fitted ? BorderBeamMath.Fixed2(28 * k.Kd) : 28;
            f.BandH = c.Fitted ? BorderBeamMath.Fixed2(28 * k.Ke) : 28;
        }
    }

    private static void AddBlob(EllipseBuffer e, int layer, BeamBlob b, double scale, CssColor color, bool fitted, FitScale k, double w, double h)
    {
        if (!fitted)
        {
            // the package's blob(): sizes rounded to whole px when scaled (Math.round)
            double rx = scale == 1 ? b.W : Math.Floor(b.W * scale + 0.5), ry = scale == 1 ? b.H : Math.Floor(b.H * scale + 0.5);
            e.Add(layer, b.X / 100 * w, b.Y / 100 * h, rx, ry, color);
            return;
        }
        var fb = BorderBeamMath.FitBlob(b.X, b.Y, b.W, b.H, k, w, h);
        e.Add(layer, fb.X(w), fb.Y(h), BorderBeamMath.Fixed2(fb.W * scale), BorderBeamMath.Fixed2(fb.H * scale), color);
    }

    // ---------- line (fo, De, Ke, Ze) ----------
    private static void Line(BeamFrame f, BeamSlotConfig c, BeamValues v, ComposerLightClock clock, double w, double h)
    {
        var dark = c.Dark; var pal = ComposerLightSettings.PaletteName(c.Palette); var mono = c.Palette == BeamPalette.Mono;
        var s = c.Fitted ? h / 42 : 1;
        double P(double x) => BorderBeamMath.Fixed2(x * s);
        double X = clock.LineX, Wv = clock.LineW, Hv = clock.LineH, SP = clock.LineSpike1, SP2 = clock.LineSpike2, MUL = v.Spk;
        var le = clock.LineEdge;
        var px = X * w;
        f.Mode = 1;
        f.RingRadius = Math.Max(0, f.ClipRadius - 1);
        f.StrokeOpacity = Math.Clamp(le * v.So, 0, 1); f.InnerOpacity = Math.Clamp(le * v.Io, 0, 1); f.BloomOpacity = Math.Clamp(le * v.Bo, 0, 1);
        // MS (the stroke and inner mask) and MB (the bloom mask), at the travelling point on the bottom edge
        f.MaskX = px; f.MaskSrx = P(78) * Wv; f.MaskSry = P(60) * Hv; f.MaskBrx = P(84) * Wv; f.MaskBry = P(110) * Hv;
        f.Bands = true; f.BandV = f.BandH = P(28);
        f.Shadow = T.Presets["line"][dark ? "dark" : "light"].InnerShadow; f.ShadowSigma = 4.5;
        // b1: Ue (first listed on top, so added bottom first)
        var ue = T.LineInner[pal];
        if (!(c.Faithful && AnyBad(ue)))
            for (var i = ue.Count - 1; i >= 0; i--)
            {
                var r = ue[i];
                f.Ellipses.Add(BeamLayer.Inner, px + BorderBeamMath.Offset(r.OffsetX * s), h + BorderBeamMath.Offset(-Math.Abs(r.OffsetY) * s), P(r.SizeW) * Wv, P(r.SizeH) * Hv, r.Color);
            }
        // b2: w over v (Ve)
        var ve = T.LineStroke[pal][dark ? "dark" : "light"];
        if (!(c.Faithful && AnyBad(ve)))
            for (var i = ve.Count - 1; i >= 0; i--)
            {
                var t = ve[i];
                f.Ellipses.Add(BeamLayer.Stroke, px + BorderBeamMath.Offset(t.OffsetX * s), h + BorderBeamMath.Offset(t.OffsetY * s), P(t.SizeW) * Wv, P(t.SizeH) * Hv, t.Color);
            }
        if (dark) f.Ellipses.Add(BeamLayer.Stroke, px, h + P(2), P(24) * Wv, P(28) * Hv, new CssColor(255, 255, 255, 0.38), 0.30, new CssColor(255, 255, 255, 0.12), 0.30, new CssColor(255, 255, 255, 0.12), 0.65);
        else f.Ellipses.Add(BeamLayer.Stroke, px, h + P(2), P(35) * Wv, P(28) * Hv, new CssColor(0, 0, 0, 0.6), 0.35, new CssColor(0, 0, 0, 0.25), 0.35, new CssColor(0, 0, 0, 0.25), 0.70);
        // b3: the spikes and glows (Ze); its filter: Mono blur 6 only, static none, else blur, hue (hw8), brightness, saturation
        f.FilterStroke = !c.Static; f.HueStroke = v.Hb + v.Hr * clock.HueSwing;
        double sigma;
        if (mono) { sigma = 6; f.FilterBloom = false; f.HueOnBloom = false; }
        else if (c.Static) { sigma = 0; f.FilterBloom = false; f.HueOnBloom = false; }
        else { sigma = v.Blur; f.FilterBloom = true; f.HueOnBloom = true; f.HueBloom = v.Hb + (v.Hr + 10) * clock.HueSwing8; }
        f.BloomSigma = sigma;   // the layer box's blurred edge
        if (!(c.Faithful && SpikesBad(c))) Spikes(f.Ellipses, c, s, w, h, X, Wv, Hv, SP, SP2, MUL, sigma);
    }

    private static bool SpikesBad(BeamSlotConfig c)
    {
        var pal = ComposerLightSettings.PaletteName(c.Palette);
        var r = c.Dark ? T.Spike[pal] : T.SpikeLight[pal];
        var t = T.LineSpikes[pal][c.Dark ? "dark" : "light"];
        if (r.Primary.Bad || r.Secondary.Bad) return true;
        for (var i = 0; i < t.Count; i++) if (t[i].C1.Bad || t[i].C2.Bad) return true;
        return false;
    }

    // bbQ: an rgba's alpha times e (to two decimals), an rgb's alpha set to e
    private static CssColor Q(CssColor c, double e) => c.WithAlpha(BorderBeamMath.Fixed2(c.A * e));

    private static void Spikes(EllipseBuffer e, BeamSlotConfig c, double sc, double w, double h, double X, double WW, double HH, double SP, double SP2, double MUL, double sigma)
    {
        var pal = ComposerLightSettings.PaletteName(c.Palette); var dark = c.Dark; var s = c.Palette == BeamPalette.Mono;
        var r = dark ? T.Spike[pal] : T.SpikeLight[pal];
        var t = T.LineSpikes[pal][dark ? "dark" : "light"];
        var i = s ? 0.14 : 1;
        CssColor cc = s ? Q(r.Primary, 0.14) : r.Primary, p = s ? Q(r.Primary, 0.09) : r.Primary, b = s ? Q(r.Secondary, 0.12) : r.Secondary;
        var g = r.Secondary.WithAlpha(s ? 0.06 : 0.49);                                      // bbJ
        (CssColor C1, CssColor C2) N(int j) => s ? (Q(t[j].C1, i), Q(t[j].C2, i * 0.7)) : (t[j].C1, t[j].C2);
        double px(double v) => BorderBeamMath.Fixed2(v * sc);
        double l = s ? 12 : 0.8, ff = s ? 14 : 2, W = s ? 12 : 1.2, y = s ? 10 : 0.6, d = s ? 42 : 92, mm = s ? 38 : 72, H = s ? 40 : 85, XX = s ? 32 : 60, O = s ? 12 : 1;
        var kc = dark ? p : s ? Q(r.Primary, 0.11) : r.Primary.WithAlpha(0.85);
        var zc = dark ? g : s ? Q(r.Secondary, 0.09) : r.Secondary.WithAlpha(0.7);
        // [width, height, x share, y lift, c1, c2, mid, end]
        var sp = spikeScratch ??= new (double, double, double, double, CssColor, CssColor, double, double)[7];
        sp[0] = (px(l) * SP * MUL, d, 8, 2, cc, kc, 30, 88);
        sp[1] = (px(10) * SP2 * MUL, 35, 22, 4, b, zc, 50, 95);
        sp[2] = (px(ff) * (2 - SP) * MUL, mm, 36, 3, N(0).C1, N(0).C2, 40, 90);
        sp[3] = (px(14) * SP2 * MUL, 28, 50, 2, N(1).C1, N(1).C2, 55, 96);
        sp[4] = (px(W) * (2 - SP2) * MUL, H, 64, 4, N(2).C1, N(2).C2, 35, 89);
        sp[5] = (px(7) * SP * MUL, 45, 78, 2, N(3).C1, N(3).C2, 48, 94);
        sp[6] = (px(dark ? y : O) * (2 - SP) * MUL, XX, 92, 3, N(4).C1, N(4).C2, 42, 91);
        var px0 = X * w;
        void Glow(double cx, double cy, double rx, double ry, CssColor c0, double s1, CssColor c1, double s2, CssColor c2, double end)
        {
            var (bx, by, amp) = Blurred(rx, ry, sigma);
            e.Add(BeamLayer.Bloom, cx, cy, bx, by, c0, s1, c1, s2, c2, end, amp);
        }
        // the glows sit under the spikes (listed last), so they go in first
        if (dark)
        {
            Glow(px0, h, px(42) * WW, px(40) * HH, new CssColor(255, 255, 255, s ? 0.15 : 0.3), 0.25, new CssColor(255, 255, 255, s ? 0.06 : 0.12), 0.55, new CssColor(255, 255, 255, s ? 0.015 : 0.03), 0.80);
            Glow(px0, h + px(1), px(21) * SP, px(15) * SP2, new CssColor(255, 255, 255, s ? 0.5 : 1), 0.20, new CssColor(255, 255, 255, s ? 0.45 : 0.9), 0.50, new CssColor(255, 255, 255, s ? 0.25 : 0.5), 1);
        }
        else Glow(px0, h, px(50) * WW, px(32) * HH, new CssColor(0, 0, 0, 0.5), 0.30, new CssColor(0, 0, 0, 0.18), 0.60, new CssColor(0, 0, 0, 0.03), 0.85);
        void One(int q, double xs)
        {
            var z = sp[q];
            Glow(xs, h - px(z.Lift), z.Wd, px(z.Ht) * HH * MUL, z.C1, z.Mid / 100, z.C2, z.Mid / 100, z.C2, z.End / 100);
        }
        if (!c.Fitted)
            for (var j = sp.Length - 1; j >= 0; j--) One(j, sp[j].Xs / 100 * w);
        else
        {
            double R = 366, step = 0.14 * R * sc, x0 = 0.08 * R * sc;
            var count = 0;
            while (x0 + count * step <= w - x0 + 0.5) count++;
            for (var j = count - 1; j >= 0; j--) One(j % 7, BorderBeamMath.Fixed2(x0 + j * step));
        }
    }

    // ---------- the pulses (fe, Re, ge) ----------
    private static readonly string[] Quads = ["tl", "tr", "bl", "br"];
    private static int QuadIndex(string q) => Array.IndexOf(Quads, q);

    /// <summary>bbFe's blob at the oscillators: radii times bw/bh (and bgh vertically), centre plus the drift (times the fit's axis scale).</summary>
    private static void PulseBlob(EllipseBuffer e, int layer, double[] o, CssColor color, double w0, double h0, int region, string quad, double xp, double yp,
        bool glow, BeamValues v, bool fitted, FitScale k, double w, double h, double boxW, double boxH, double offX, double offY,
        double scaleX, double scaleY, double sigma)
    {
        double X, Y, ax = 1, ay = 1, ww = w0, hh = h0;
        if (fitted)
        {
            var f = BorderBeamMath.FitBlob(xp, yp, w0, h0, k, w, h);
            X = f.X(boxW); Y = f.Y(boxH); ax = BorderBeamMath.Fixed2(f.Ax); ay = BorderBeamMath.Fixed2(f.Ay); ww = f.W; hh = f.H;
        }
        else { X = xp / 100 * boxW; Y = yp / 100 * boxH; }
        var r0 = (region - 1) * 4;
        var rx = BorderBeamMath.Fixed2(ww) * o[r0] * (glow && !fitted ? v.Sx * v.Boost : 1);
        var ry = BorderBeamMath.Fixed2(hh) * o[r0 + 1] * o[12] * (glow && !fitted ? v.Sy * v.Boost : 1);
        var cx = X + o[r0 + 2] * ax; var cy = Y + o[r0 + 3] * ay;
        var alpha = o[13 + QuadIndex(quad)];
        // the layer box's origin, then its transform about the composer's centre
        cx = w / 2 + (cx + offX - w / 2) * scaleX; cy = h / 2 + (cy + offY - h / 2) * scaleY;
        var (bx, by, amp) = Blurred(rx * scaleX, ry * scaleY, sigma);
        e.Add(layer, cx, cy, bx, by, color.WithAlpha(alpha), amp);
    }

    /// <summary>bbRe's static bloom blob at a fixed alpha.</summary>
    private static void BloomBlob(EllipseBuffer e, int layer, PulseBlob s, IReadOnlyList<BeamBlob> g, double alpha, bool glow, BeamValues v, bool fitted, FitScale k,
        double w, double h, double boxW, double boxH, double offX, double offY, double scaleX, double scaleY, double sigma)
    {
        var i = g[s.ColorIndex];
        double bxp = s.X ?? i.X, byp = s.Y ?? i.Y;
        double cx, cy, rx, ry;
        if (fitted)
        {
            var f = BorderBeamMath.FitBlob(bxp, byp, s.W, s.H, k, w, h);
            cx = f.X(boxW); cy = f.Y(boxH); rx = BorderBeamMath.Fixed2(f.W); ry = BorderBeamMath.Fixed2(f.H);
        }
        else
        {
            cx = bxp / 100 * boxW; cy = byp / 100 * boxH;
            rx = s.W * (glow ? v.Sx * v.Boost : 1); ry = s.H * (glow ? v.Sy * v.Boost : 1);
        }
        cx = w / 2 + (cx + offX - w / 2) * scaleX; cy = h / 2 + (cy + offY - h / 2) * scaleY;
        var (bx, by, amp) = Blurred(rx * scaleX, ry * scaleY, sigma);
        e.Add(layer, cx, cy, bx, by, i.Color.WithAlpha(alpha), amp);
    }

    private static void PulseInner(BeamFrame f, BeamSlotConfig c, BeamValues v, ComposerLightClock clock, double w, double h)
    {
        var pal = ComposerLightSettings.PaletteName(c.Palette);
        var g = T.LargeBlobs[pal];
        var k = c.Fitted ? BorderBeamMath.FitK(BeamType.PulseInner, w, h) : default;
        var o = clock.PulseInner;
        f.Mode = 2;
        f.RingRadius = f.ClipRadius;
        f.StrokeOpacity = Math.Clamp(v.So, 0, 1); f.InnerOpacity = Math.Clamp(v.Io, 0, 1); f.BloomOpacity = Math.Clamp(v.Bo, 0, 1);
        f.FilterStroke = true; f.HueStroke = c.Static ? 0 : v.Hb + clock.PulseHueInner;
        f.FilterBloom = true; f.HueOnBloom = !c.Static; f.HueBloom = f.HueStroke;
        f.Bands = true; f.BandV = c.Fitted ? BorderBeamMath.Fixed2(28 * k.Kd) : 28; f.BandH = c.Fitted ? BorderBeamMath.Fixed2(28 * k.Ke) : 28;
        // b1: the t blobs (Je sizes), then the four corner glows under them
        var dark = c.Dark;
        var sc = dark ? new CssColor(255, 255, 255, 1) : new CssColor(0, 0, 0, 1);
        var ia = dark ? 0.18 : 0.08;
        double cgx = c.Fitted ? BorderBeamMath.Fixed2(60 * k.Ke) : 60, cgy = c.Fitted ? BorderBeamMath.Fixed2(60 * k.Kd) : 60;
        for (var q = 0; q < 4; q++)
        {
            // bottom first: br, bl, tr, tl (bop-br is oscillator 16, bop-tl 13)
            var a = ia * o[16 - q];
            double cx = q % 2 == 0 ? w : 0, cy = q < 2 ? h : 0;
            f.Ellipses.Add(BeamLayer.Inner, cx, cy, cgx, cgy, sc.WithAlpha(a), 0, sc.WithAlpha(a), 0, sc.WithAlpha(a), 0.7);
        }
        for (var r = g.Count - 1; r >= 0; r--)
        {
            var slot = T.PulseSlots[r]; var (sw, sh) = T.PulseInnerSizes[r];
            PulseBlob(f.Ellipses, BeamLayer.Inner, o, g[r].Color, sw, sh, slot.Region, slot.Quad, g[r].X, g[r].Y, false, v, c.Fitted, k, w, h, w, h, 0, 0, 1, 1, 0);
        }
        // b2: the u blobs on the ring
        for (var r = g.Count - 1; r >= 0; r--)
        {
            var slot = T.PulseSlots[r];
            PulseBlob(f.Ellipses, BeamLayer.Stroke, o, g[r].Color, g[r].W, g[r].H, slot.Region, slot.Quad, g[r].X, g[r].Y, false, v, c.Fitted, k, w, h, w, h, 0, 0, 1, 1, 0);
        }
        // b3: the static bloom blobs, blurred, on the ring
        var alpha = BorderBeamMath.ToFixed(1 - BorderBeamMath.PulseParameters(BeamType.PulseInner, dark).Op * 0.5, 3);
        var eo = T.PulseInnerBloom;
        for (var j = eo.Count - 1; j >= 0; j--) BloomBlob(f.Ellipses, BeamLayer.Bloom, eo[j], g, alpha, false, v, c.Fitted, k, w, h, w, h, 0, 0, 1, 1, v.Blur);
        f.BloomSigma = v.Blur; // the ring's edge falloff (the blobs themselves are widened)
    }

    private static void PulseOutside(BeamFrame f, BeamSlotConfig c, BeamValues v, ComposerLightClock clock, double w, double h)
    {
        var pal = ComposerLightSettings.PaletteName(c.Palette);
        var g = T.LargeBlobs[pal];
        var k = c.Fitted ? BorderBeamMath.FitK(BeamType.PulseOutside, w, h) : default;
        var o = clock.PulseOutside;
        f.Mode = 3;
        f.RingRadius = f.ClipRadius;
        f.StrokeOpacity = Math.Clamp(v.So, 0, 1); f.OuterCoreOpacity = Math.Clamp(v.Io, 0, 1); f.OuterBloomOpacity = Math.Clamp(v.Bo, 0, 1);
        f.InnerOpacity = 0; f.BloomOpacity = 0;
        f.FilterStroke = true; f.HueStroke = c.Static ? 0 : v.Hb + clock.PulseHueOutside;
        f.FilterBloom = true; f.HueOnBloom = !c.Static; f.HueBloom = f.HueStroke;
        f.CoreSigma = v.Cb; f.OuterBloomSigma = v.Bb;
        var ye = T.PulseOutsideCore;
        // b5 (inset -30, scaled .95 x .9, blur bb): the oo blobs at 1 - op / 2; painted over b4
        var alpha = BorderBeamMath.ToFixed(1 - BorderBeamMath.PulseParameters(BeamType.PulseOutside, c.Dark).Op * 0.5, 3);
        var oo = T.PulseOutsideBloom;
        for (var j = oo.Count - 1; j >= 0; j--) BloomBlob(f.Ellipses, BeamLayer.OuterBloom, oo[j], g, alpha, true, v, c.Fitted, k, w, h, w + 60, h + 60, -30, -30, 0.95, 0.9, v.Bb);
        // b4 (inset -10, scaled .95 x .9, blur cb): T
        for (var j = ye.Count - 1; j >= 0; j--)
        {
            var t = ye[j]; var s = g[t.ColorIndex];
            PulseBlob(f.Ellipses, BeamLayer.OuterCore, o, s.Color, t.W, t.H, t.Region, t.Quad, t.X ?? s.X, t.Y ?? s.Y, true, v, c.Fitted, k, w, h, w + 20, h + 20, -10, -10, 0.95, 0.9, v.Cb);
        }
        // b2: T on the ring
        for (var j = ye.Count - 1; j >= 0; j--)
        {
            var t = ye[j]; var s = g[t.ColorIndex];
            PulseBlob(f.Ellipses, BeamLayer.Stroke, o, s.Color, t.W, t.H, t.Region, t.Quad, t.X ?? s.X, t.Y ?? s.Y, true, v, c.Fitted, k, w, h, w, h, 0, 0, 1, 1, 0);
        }
        f.BloomSigma = 0;
    }
}
