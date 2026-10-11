// PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): one composer's light over time, as the playground runs
// one instance (logic.js instance(), beamSlots(), auraVars(), beamVars(), tick(); gen.py's transitions): the eased values,
// the light groups' fades, Border beam's three cross-fading slots and Active, the clock. It produces, per frame, what the
// renderer draws: the Uiverse light's values and up to three beam slots. Every change transitions (OD-0076); under reduce
// motion nothing moves and changes apply at once, as the playground's prefers-reduced-motion rule does.

namespace Marrow.Core.Models.ComposerLights;

/// <summary>The colours the Uiverse light's Maker and Accent schemes read (the chip's maker palette, the accent), #RRGGBB.</summary>
public sealed record ComposerLightPalette(string MakerHeartDark, string MakerSecondDark, string MakerHeartLight, string MakerSecondLight, string AccentDark, string AccentLight)
{
    /// <summary>The playground's defaults: Opus (Anthropic's maker stops, OD-0063) and the Windows accent (OD-0088).</summary>
    public static ComposerLightPalette Default { get; } = new("#CC6B47", "#E0875F", "#B85A38", "#CC6B47", "#4B9FFC", "#0067C0");
}

/// <summary>What the Uiverse light draws in one frame.</summary>
public sealed class AuraFrame
{
    public double W, H, Radius = 22;
    public double GroupAlpha;
    public double Angle = 10, Scale = 1;               // --au-r (degrees), --au-s
    public double HaloAlpha, RimAlpha, InnerAlpha;     // i tm, i ro tm, i ro tm .75 (clamped to 1 as CSS opacity is)
    public double HaloSize, HaloBlur, RimWidth, DotCell, DotStrength;
    public double Sx = 1, Sy = 1, Rsx = 1, Rsy = 1;    // the turning square's stretch for the halo and the rim
    public CssColor C1, C2, K1, K2, N1, N2;
}

/// <summary>One composer's light, frame by frame.</summary>
public sealed class ComposerLightAnimator
{
    // the playground's transition durations
    public const double PropertySeconds = 0.6, IntensitySeconds = 0.9, FadeInSeconds = 0.6, FadeOutSeconds = 0.5, ActiveOffSeconds = 0.5;
    /// <summary>The composer's own corner radius (CR); Border beam's radius Auto.</summary>
    public const double ComposerRadius = 22;

    private ComposerLightSettings settings;
    private ComposerLightPhase phase;
    private bool dark, reduce;
    private ComposerLightPalette palette = ComposerLightPalette.Default;
    private double w, h;
    private double last = double.NaN;
    private readonly ComposerLightClock clock;

    private readonly CssTransition auraGroup, beamGroup, active;
    // beam values
    private readonly CssTransition so, io, bo, br, sat, hb, hr, blur, cb, bb, sx, sy, boost, spk, r;
    // aura values
    private readonly CssTransition ai, aro, ado, atm, asx, asy, arsx, arsy, ahs, ahb, arw, add;
    private readonly CssTransition[] colors = new CssTransition[24];

    // the slots (beamSlots)
    private readonly BeamSlotConfig?[] keys = new BeamSlotConfig?[3];
    private readonly long[] seq = [0, -1, -2];
    private readonly CssTransition[] slotAlpha = [new(1), new(0), new(0)];
    private int on;
    private long counter;

    public BeamValues Values { get; } = new();
    public AuraFrame Aura { get; } = new();
    public BeamFrame[] Slots { get; } = [new(), new(), new()];
    public ComposerLightClock Clock => clock;

    /// <summary>OD-0139: the shader lights (Composer Light v2), their state and frames.</summary>
    public ShaderLightAnimator Shaders { get; }
    public ComposerLightSettings Settings => settings;

    /// <summary>The slots drawn this frame (their index into <see cref="Slots"/>), and how many.</summary>
    public int[] Drawn { get; } = new int[3];
    public int DrawnCount { get; private set; }

    /// <summary>True when the Uiverse light draws this frame.</summary>
    public bool AuraVisible => Aura.GroupAlpha > 0.0005;

    public ComposerLightAnimator(ComposerLightSettings settings, ComposerLightPhase phase, bool dark, double width, double height, bool reduceMotion, ComposerLightPalette? palette = null)
    {
        this.settings = settings.Clamped(); this.phase = phase; this.dark = dark; w = width; h = height; reduce = reduceMotion;
        if (palette is not null) this.palette = palette;
        var b = this.settings.Beam;
        clock = new ComposerLightClock(this.settings.Aura.Breathing, !BeamDark(b));
        auraGroup = new CssTransition(this.settings.Style == ComposerLightStyle.Aura ? 1 : 0);
        beamGroup = new CssTransition(this.settings.Style == ComposerLightStyle.Beam ? 1 : 0);
        active = new CssTransition(b.Active ? 1 : 0);
        var e = Effective();
        so = new(e.StrokeOpacity); io = new(e.InnerOpacity); bo = new(e.BloomOpacity); br = new(e.Brightness); sat = new(e.Saturation);
        hb = new(e.HueBase); hr = new(e.HueRange); blur = new(e.Blur); cb = new(e.CoreBlur); bb = new(e.BloomBlur);
        sx = new(e.GlowX); sy = new(e.GlowY); boost = new(e.Boost); spk = new(e.Spike); r = new(e.Radius);
        var a = AuraTargets();
        ai = new(a[0]); aro = new(a[1]); ado = new(a[2]); atm = new(a[3]); asx = new(a[4]); asy = new(a[5]); arsx = new(a[6]); arsy = new(a[7]);
        ahs = new(a[8]); ahb = new(a[9]); arw = new(a[10]); add = new(a[11]);
        var cs = AuraColors();
        for (var i = 0; i < 24; i++) colors[i] = new CssTransition(cs[i]);
        keys[0] = BeamSlotConfig.For(b, BeamDark(b));
        Shaders = new ShaderLightAnimator(this.settings, phase, dark, reduce, this.palette);
        Produce(0);
    }

    private bool BeamDark(BorderBeamOptions b) => b.Theme == BeamTheme.Auto ? dark : b.Theme == BeamTheme.Dark;

    private BorderBeamResolved Effective() => BorderBeamResolved.For(settings.Beam, phase, dark, w, h, ComposerRadius);

    // auraVars(): i, ro, do, tm, sx, sy, rsx, rsy, hs, hb, rw, dd
    private double[] AuraTargets()
    {
        var a = settings.Aura;
        double hs = a.HaloSize, rw = a.RimWidth;
        return
        [
            a.IntensityFor(phase) / 100, a.Rim ? 1 : 0, a.Dots ? a.DotStrength / 100 : 0, dark ? 1 : 1.2,
            4 * (w + 2 * hs) / 400, 4 * (h + 2 * hs) / 400, 4 * (w + 2 * rw) / 400, 4 * (h + 2 * rw) / 400,
            hs, a.HaloBlur, rw, a.DotCell,
        ];
    }

    // auraColors(): c1, c2, k1, k2, n1, n2, premultiplied rgba each
    private double[] AuraColors()
    {
        var a = settings.Aura;
        string c1, c2, k1, k2, n1, n2;
        double al1 = 1, al2 = 1; // OD-0099 (Q-CZ-3 B): a Custom colour's alpha belongs to the halo's two colours; k and n mix from the opaque hex
        if (a.Scheme == AuraScheme.Original) { c1 = "#ff5500"; c2 = "#0055ff"; k1 = "#ff9955"; k2 = "#5599ff"; n1 = "#ffcc99"; n2 = "#99ccff"; }
        else
        {
            if (a.Scheme == AuraScheme.Maker) { c1 = dark ? palette.MakerHeartDark : palette.MakerHeartLight; c2 = dark ? palette.MakerSecondDark : palette.MakerSecondLight; }
            else if (a.Scheme == AuraScheme.Accent) { c1 = dark ? palette.AccentDark : palette.AccentLight; c2 = BorderBeamMath.MixHex(c1, "#ffffff", 0.45); }
            else
            {
                c1 = UiverseAuraOptions.OpaqueHex(a.Color1); c2 = UiverseAuraOptions.OpaqueHex(a.Color2);
                al1 = UiverseAuraOptions.AlphaOf(a.Color1); al2 = UiverseAuraOptions.AlphaOf(a.Color2);
            }
            k1 = BorderBeamMath.MixHex(c1, "#ffffff", 0.35); k2 = BorderBeamMath.MixHex(c2, "#ffffff", 0.35);
            n1 = BorderBeamMath.MixHex(c1, "#ffffff", 0.6); n2 = BorderBeamMath.MixHex(c2, "#ffffff", 0.6);
        }
        var o = new double[24];
        var i = 0;
        foreach (var hex in new[] { c1, c2, k1, k2, n1, n2 })
        {
            var c = CssColor.Parse(hex);
            o[i++] = c.R / 255; o[i++] = c.G / 255; o[i++] = c.B / 255; o[i++] = 1;
        }
        o[3] = al1; o[7] = al2;
        return o;
    }

    private double Seconds(double s) => reduce ? 0 : s;

    /// <summary>Applies new settings at <paramref name="now"/> (seconds): every value transitions, a structural change cross-fades.</summary>
    public void SetSettings(ComposerLightSettings next, double now)
    {
        next = next.Clamped();
        if (next == settings) return;
        settings = next;
        Retarget(now);
    }

    public void SetPhase(ComposerLightPhase next, double now)
    {
        if (next == phase) return;
        phase = next;
        Retarget(now);
    }

    public void SetDark(bool next, double now)
    {
        if (next == dark) return;
        dark = next;
        Retarget(now);
    }

    public void SetPalette(ComposerLightPalette next, double now)
    {
        if (next == palette) return;
        palette = next;
        Retarget(now);
    }

    /// <summary>The composer's size (layout, no transition).</summary>
    public void SetSize(double width, double height, double now)
    {
        if (width == w && height == h) return;
        w = width; h = height;
        Retarget(now);
        // the values that only follow the composer's size (the halo's and rim's stretch, Pulse Outside's glow scale)
        // follow it at once: a growing composer is layout, not a change of setting
        foreach (var t in new[] { asx, asy, arsx, arsy, sx, sy }) t.Snap(t.Target);
    }

    public void SetReduceMotion(bool next, double now)
    {
        if (next == reduce) return;
        reduce = next;
        Retarget(now);
    }

    private void Retarget(double now)
    {
        var b = settings.Beam;
        var e = Effective();
        auraGroup.Set(settings.Style == ComposerLightStyle.Aura ? 1 : 0, now, Seconds(settings.Style == ComposerLightStyle.Aura ? FadeInSeconds : FadeOutSeconds));
        beamGroup.Set(settings.Style == ComposerLightStyle.Beam ? 1 : 0, now, Seconds(settings.Style == ComposerLightStyle.Beam ? FadeInSeconds : FadeOutSeconds));
        active.Set(b.Active ? 1 : 0, now, Seconds(b.Active && settings.Style == ComposerLightStyle.Beam ? FadeInSeconds : ActiveOffSeconds));
        var p = Seconds(PropertySeconds);
        so.Set(e.StrokeOpacity, now, p); io.Set(e.InnerOpacity, now, p); bo.Set(e.BloomOpacity, now, p); br.Set(e.Brightness, now, p); sat.Set(e.Saturation, now, p);
        hb.Set(e.HueBase, now, p); hr.Set(e.HueRange, now, p); blur.Set(e.Blur, now, p); cb.Set(e.CoreBlur, now, p); bb.Set(e.BloomBlur, now, p);
        sx.Set(e.GlowX, now, p); sy.Set(e.GlowY, now, p); boost.Set(e.Boost, now, p); spk.Set(e.Spike, now, p); r.Set(e.Radius, now, p);
        var a = AuraTargets();
        ai.Set(a[0], now, Seconds(IntensitySeconds));
        CssTransition[] rest = [aro, ado, atm, asx, asy, arsx, arsy, ahs, ahb, arw, add];
        for (var i = 0; i < rest.Length; i++) rest[i].Set(a[i + 1], now, p);
        var cs = AuraColors();
        for (var i = 0; i < 24; i++) colors[i].Set(cs[i], now, p);
        // beamSlots(): a structural change goes into the slot faded out longest (or back into one still fading with that key)
        var key = BeamSlotConfig.For(b, BeamDark(b));
        if (keys[on] != key)
        {
            var j = -1;
            for (var i = 0; i < 3; i++) if (i != on && keys[i] == key) j = i;
            if (j < 0) { j = on == 0 ? 1 : 0; for (var i = 0; i < 3; i++) if (i != on && seq[i] < seq[j]) j = i; }
            keys[j] = key; seq[j] = ++counter; on = j;
            for (var i = 0; i < 3; i++) slotAlpha[i].Set(i == on ? 1 : 0, now, p);
        }
        Shaders.Retarget(settings, phase, dark, palette, reduce, now);
        if (reduce) SnapAll();
    }

    /// <summary>OD-0139: a keystroke in the composer at <paramref name="caretX"/> (px from the composer's left): the shader lights' lift.</summary>
    public void Keystroke(double caretX) => Shaders.Keystroke(caretX);

    /// <summary>OD-0139: where the composer's caret is (px from its left).</summary>
    public void SetCaret(double caretX) => Shaders.SetCaret(caretX);

    // reduce motion: the playground's `transition: none` (every value at its target at once)
    private void SnapAll()
    {
        foreach (var t in new[] { auraGroup, beamGroup, active, so, io, bo, br, sat, hb, hr, blur, cb, bb, sx, sy, boost, spk, r, ai, aro, ado, atm, asx, asy, arsx, arsy, ahs, ahb, arw, add })
            t.Snap(t.Target);
        foreach (var t in colors) t.Snap(t.Target);
        foreach (var t in slotAlpha) t.Snap(t.Target);
    }

    /// <summary>
    /// Moves the light to <paramref name="now"/> (seconds, monotonic) and fills <see cref="Aura"/> and the drawn
    /// <see cref="Slots"/>. Returns true while anything moves (the next frame is wanted).
    /// </summary>
    public bool Advance(double now, double motionScale = 1)
    {
        var dt = double.IsNaN(last) ? 0 : now - last;
        last = now;
        var b = settings.Beam;
        var beamDark = BeamDark(b);
        clock.Advance(dt * motionScale, reduce, settings.Aura.Speed, settings.Aura.Breathing, BorderBeamMath.Duration(b, w, h, ComposerRadius),
            b.StaticColors || b.Palette == BeamPalette.Mono, !beamDark, b.Radius ?? ComposerRadius);
        Shaders.Advance(dt, motionScale, now);
        Produce(now);
        if (reduce) return false;
        var lit = AuraVisible || DrawnCount > 0 || Shaders.DrawnCount > 0;
        if (lit) return true;
        // nothing drawn: frames only while a fade is still under way
        return auraGroup.IsRunning(now) || beamGroup.IsRunning(now) || active.IsRunning(now) || Shaders.Fading(now);
    }

    /// <summary>The playground's absolute-time hook for stills and tests (window.__bbT): the beam at T seconds.</summary>
    public void SetBeamTime(double seconds, double now)
    {
        var b = settings.Beam;
        clock.SetBeamTime(seconds, seconds, BorderBeamMath.Duration(b, w, h, ComposerRadius), b.StaticColors || b.Palette == BeamPalette.Mono, !BeamDark(b));
        Produce(now);
    }

    private void Produce(double now)
    {
        // the eased beam values
        var v = Values;
        v.So = so.Value(now); v.Io = io.Value(now); v.Bo = bo.Value(now); v.Br = br.Value(now); v.Sat = sat.Value(now);
        v.Hb = hb.Value(now); v.Hr = hr.Value(now); v.Blur = blur.Value(now); v.Cb = cb.Value(now); v.Bb = bb.Value(now);
        v.Sx = sx.Value(now); v.Sy = sy.Value(now); v.Boost = boost.Value(now); v.Spk = spk.Value(now); v.R = r.Value(now);
        var beam = beamGroup.Value(now) * active.Value(now);
        DrawnCount = 0;
        for (var i = 0; i < 3; i++)
        {
            if (keys[i] is not { } key) continue;
            var alpha = slotAlpha[i].Value(now) * beam;
            if (alpha <= 0.0005) continue;
            var f = Slots[i];
            BorderBeamScene.Build(f, key, v, clock, w, h);
            f.Alpha = alpha;
            Drawn[DrawnCount++] = i;
        }
        // the aura
        var a = Aura;
        a.W = w; a.H = h;
        a.GroupAlpha = auraGroup.Value(now);
        a.Angle = clock.AuraAngle; a.Scale = clock.AuraScale;
        double i0 = ai.Value(now), ro = aro.Value(now), tm = atm.Value(now);
        a.HaloAlpha = Math.Clamp(i0 * tm, 0, 1);
        a.RimAlpha = Math.Clamp(i0 * ro * tm, 0, 1);
        a.InnerAlpha = Math.Clamp(i0 * ro * tm * 0.75, 0, 1);
        a.DotStrength = ado.Value(now);
        a.Sx = asx.Value(now); a.Sy = asy.Value(now); a.Rsx = arsx.Value(now); a.Rsy = arsy.Value(now);
        a.HaloSize = ahs.Value(now); a.HaloBlur = ahb.Value(now); a.RimWidth = arw.Value(now); a.DotCell = add.Value(now);
        CssColor C(int k)
        {
            double pa = colors[k * 4 + 3].Value(now);
            if (pa <= 0) return CssColor.Transparent;
            return new CssColor(colors[k * 4].Value(now) * 255, colors[k * 4 + 1].Value(now) * 255, colors[k * 4 + 2].Value(now) * 255, pa);
        }
        a.C1 = C(0); a.C2 = C(1); a.K1 = C(2); a.K2 = C(3); a.N1 = C(4); a.N2 = C(5);
        Shaders?.Produce(now, w, h, ComposerRadius);
    }

    /// <summary>
    /// A still at given phases (the playground's __bbT hook and its paused aura clock): the beam at
    /// <paramref name="beamSeconds"/> (its hue swing at <paramref name="hueSeconds"/>), the aura <paramref name="auraTurns"/>
    /// turns in with its breathing at <paramref name="breathPhase"/>.
    /// </summary>
    public void Still(double beamSeconds, double hueSeconds, double auraTurns, double breathPhase, double now)
    {
        var b = settings.Beam;
        clock.SetBeamTime(beamSeconds, hueSeconds, BorderBeamMath.Duration(b, w, h, ComposerRadius), b.StaticColors || b.Palette == BeamPalette.Mono, !BeamDark(b));
        clock.SetAuraPhase(auraTurns, breathPhase, settings.Aura.Breathing);
        Produce(now);
    }

    /// <summary>The opacity of slot <paramref name="i"/>'s cross-fade at a time (tests).</summary>
    public double SlotOpacity(int i, double now) => slotAlpha[i].Value(now);

    /// <summary>The slot that holds the current structure.</summary>
    public int CurrentSlot => on;

    /// <summary>The structure each slot holds.</summary>
    public BeamSlotConfig? SlotKey(int i) => keys[i];
}
