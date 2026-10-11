// PORT-ONLY: ED-NEW-glasslook-1 (OD-0107 and its amendment; the lead, 2026-10-11: the owner approves the glass look on a design canvas first, so the
// per-surface look is set in one place) and ED-NEW-glasslook-2 (the owner's Glass settings in Customization). The material's look knobs, mapped onto
// the two MIT repos' own knobs, the one table that gives each surface its look, and the user's tuning over it. Every knob is a uniform or a CPU-side
// size: a change here needs no new GPU program (GlassMaterial.WarmupPrograms stays whole), so a slider drags with no recompile.
using Marrow.Core.Port.Settings;

namespace Marrow.UI.Port;

/// <summary>The kinds of glass surface the look table (<see cref="GlassLooks"/>) is set for.</summary>
public enum GlassSurface
{
    /// <summary>A window's own glass over its backdrop or wallpaper (a rectangle with no corner: Chrome's window glass layer, the tour's backdrop).</summary>
    Window,
    /// <summary>A rounded panel or card (the head panel, the usage panel, the floating sidebar, the minimap, sheets, request cards).</summary>
    Panel,
    /// <summary>The composer pill (its corner measured circular in ED-275's band).</summary>
    Composer,
    /// <summary>A capsule or circle that is not a button (a quiet glass shape).</summary>
    Capsule,
    /// <summary>A glass button or toolbar pill: a capsule or circle that responds to the pointer or carries a tint (Chrome's glass buttons).</summary>
    Button,
    /// <summary>ED-NEW-glasslook-3: a glass piece inside another glass piece (a control on the composer, a button on a panel). The owner's principles
    /// (2026-10-11): only the container is glass; what is on it is glyphs and text with quiet hover fills (<see cref="GlassLook.QuietLook"/>), a hero
    /// control excepted.</summary>
    Inner,
}

/// <summary>
/// ED-NEW-glasslook-1: the material's look. Each knob scales the calibrated material (ED-356, ED-358); 1 (0 for <see cref="Clarity"/>) is the look the
/// release drew. The repos' names for each are in brackets.
/// </summary>
/// <param name="Clarity">How clear the lens rim is, 0 to 1: the share of the unblurred backdrop the rim band bends (rizzytoday's displacement of
/// the backdrop itself, its filter without blur), times the bend's own fall-off; 0 bends the frost only, as the release did.</param>
/// <param name="Bend">The bend at the edge and the rim ring, times the piece's own (rizzytoday's scale; dashersw's rimIntensity), held so that
/// bend and ring together read at most 60 pt past the edge (inside the 72 pt the material reads around itself).</param>
/// <param name="Band">The width of the bend's fall-off, times the piece's own (rizzytoday's blur of its neutral centre), 0.5 to 1.25.</param>
/// <param name="Fringe">The red, green and blue split, times rizzytoday's aberration (0 / 10 / 20 on 180), 0 to 3.</param>
/// <param name="Rim">The specular rim and the light inner edge, times the Mac-calibrated light, 0 to 2.</param>
/// <param name="Blur">The backdrop blur's sigma, times the piece's own (dashersw's blurRadius), at most 12 pt.</param>
/// <param name="Tint">The film's opacity, times the calibrated film (dashersw's tintOpacity), 0 to 2.</param>
/// <param name="Saturation">The saturation lift of what the glass shows, times the calibrated lift (rizzytoday's saturate()), 0 to 2.</param>
/// <param name="Shadow">The contact line just outside the edge, times the calibrated line, 0 to 2.</param>
/// <param name="Profile">ED-NEW-glasslook-3: the bezel's lens, 0 (the release's logistic fall-off with dashersw's rim ring) to 1 (a convex squircle
/// refracting by Snell's law: the bend smooth from the edge to an undistorted centre).</param>
/// <param name="Glow">The light inner edge (an inner bevel), times the calibrated one, 0 to 2; 0 draws none.</param>
/// <param name="Hairline">The rim, 0 (the release's line at the top and the bottom) to 1 (a specular hairline lit from the top left, fading around the
/// shape, with a faint counter-highlight).</param>
/// <param name="FringeCap">The most the red and blue reads part, in pt (0: uncapped, as the release); a cap of about 1 keeps the fringe a hint.</param>
/// <param name="Lift">How much brighter the glass shows what lies behind it, toward white, 0 to 0.25 (no fill: the backdrop's own colour, lifted).</param>
/// <param name="Depth">A soft, wide, faint shadow under the glass (its darkest share just outside the edge), 0 to 0.4; the tight contact line is
/// <paramref name="Shadow"/>'s.</param>
public sealed record GlassLook(double Clarity = 0, double Bend = 1, double Band = 1, double Fringe = 1, double Rim = 1, double Blur = 1, double Tint = 1,
    double Saturation = 1, double Shadow = 1, double Profile = 0, double Glow = 1, double Hairline = 0, double FringeCap = 0, double Lift = 0, double Depth = 0)
{
    /// <summary>The look the release drew: the bend and fringe through the frost (ED-356).</summary>
    public static GlassLook Release { get; } = new();

    /// <summary>The repos' clear lens rim over the Mac-calibrated body (ED-NEW-glasslook-1's measured proposal, for the design canvas).</summary>
    public static GlassLook Liquid { get; } = new(Clarity: 0.9, Bend: 2, Fringe: 1.5, Rim: 1.3);

    /// <summary>ED-NEW-glasslook-3: the owner's premium principles (2026-10-11, relayed by the lead: "plasticky, not premium"), a candidate until the
    /// design canvas reports its numbers: glass from what it does to the backdrop (a clear squircle bezel, a slight lift, a little frost, a small
    /// tint), a hairline specular rim with a counter-highlight and no inner bevel, a fringe of at most 1 pt, a soft wide faint shadow and the tight
    /// contact line.</summary>
    public static GlassLook Premium { get; } = new(Clarity: 0.85, Bend: 1.2, Fringe: 1, Rim: 1, Blur: 0.6, Tint: 0.25, Saturation: 1.15, Shadow: 0.8,
        Profile: 1, Glow: 0, Hairline: 1, FringeCap: 1, Lift: 0.05, Depth: 0.12);

    /// <summary>ED-NEW-glasslook-3: the candidate for Apple's Tinted Liquid Glass (iOS 27, the owner's target), until the canvas reports its numbers:
    /// the premium finish with a more opaque, frosted body for contrast.</summary>
    public static GlassLook TintedCandidate { get; } = new(Clarity: 0.5, Bend: 1, Fringe: 1, Rim: 1, Blur: 1, Tint: 1.8, Saturation: 1, Shadow: 0.8,
        Profile: 1, Glow: 0, Hairline: 1, FringeCap: 1, Lift: 0, Depth: 0.14);

    /// <summary>The look <paramref name="t"/> of the way from this one to <paramref name="to"/> (a Look's crossfade): every knob in between; at 1 exactly <paramref name="to"/>.</summary>
    public GlassLook Toward(GlassLook to, double t)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (t >= 1) return to;
        if (t <= 0) return this;
        double M(double a, double b) => a + (b - a) * t;
        return new GlassLook(M(Clarity, to.Clarity), M(Bend, to.Bend), M(Band, to.Band), M(Fringe, to.Fringe), M(Rim, to.Rim), M(Blur, to.Blur), M(Tint, to.Tint),
            M(Saturation, to.Saturation), M(Shadow, to.Shadow), M(Profile, to.Profile), M(Glow, to.Glow), M(Hairline, to.Hairline), M(FringeCap, to.FringeCap),
            M(Lift, to.Lift), M(Depth, to.Depth)) { Quiet = Quiet && to.Quiet };
    }

    /// <summary>The soft shadow's blur (pt) under a piece whose smaller side is <paramref name="smaller"/> pt.</summary>
    internal static double DepthRadius(double smaller) => Math.Clamp(0.25 * smaller, 6, 20);

    /// <summary>How far the soft shadow drops (pt).</summary>
    internal static double DepthOffset(double smaller) => 0.3 * DepthRadius(smaller);

    internal double ClarityValue => Math.Clamp(Clarity, 0, 1);
    internal double BendValue => Math.Clamp(Bend, 0, 4);
    internal double BandValue => Math.Clamp(Band, 0.5, 1.25);
    internal double FringeValue => Math.Clamp(Fringe, 0, 3);
    internal double RimValue => Math.Clamp(Rim, 0, 2);
    internal double BlurValue => Math.Clamp(Blur, 0, 4);
    internal double TintValue => Math.Clamp(Tint, 0, 2);
    internal double SaturationValue => Math.Clamp(Saturation, 0, 2);
    internal double ShadowValue => Math.Clamp(Shadow, 0, 2);
    /// <summary>ED-NEW-glasslook-3: a quiet look draws no glass at all (what lies on another glass is glyphs and text, not glass on glass).</summary>
    public bool Quiet { get; init; }

    /// <summary>The look that draws no glass.</summary>
    public static GlassLook QuietLook { get; } = new() { Quiet = true };

    internal double ProfileValue => Math.Clamp(Profile, 0, 1);
    internal double GlowValue => Math.Clamp(Glow, 0, 2);
    internal double HairlineValue => Math.Clamp(Hairline, 0, 1);
    internal double FringeCapValue => Math.Clamp(FringeCap, 0, 4);
    internal double LiftValue => Math.Clamp(Lift, 0, 0.25);
    internal double DepthValue => Math.Clamp(Depth, 0, 0.4);

    /// <summary>This look under the user's <paramref name="tuning"/> (Settings > Customization > Glass): each knob times the tuning's, clarity the
    /// tuning's own when it names one. A rim made clearer than the look's own shows its lens sharp, and a lens seen sharp is the convex squircle
    /// (ED-NEW-glasslook-3, the owner's principle 3: the centre undistorted, the bend at the edge): the profile turns to it over the first quarter
    /// of the extra clarity (smoothly: no pop as the slider leaves Auto). The logistic profile's wide fall-off, seen sharp, carried magnified bright
    /// lines deep under the text (GlassTuningTests' contrast).</summary>
    public GlassLook Under(GlassSettings tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var t = tuning.Held();
        if (t with { Look = GlassVariant.Regular } == GlassSettings.Default) return this;
        var clarity = t.Clarity ?? Clarity;
        var extra = clarity > ClarityValue ? (clarity - ClarityValue) / Math.Max(1 - ClarityValue, 1e-9) : 0;
        return this with
        {
            Clarity = clarity, Profile = Math.Max(Profile, Math.Min(1, 4 * extra)), Bend = Bend * t.Refraction, Band = Band * t.Bezel, Fringe = Fringe * t.Fringe, Rim = Rim * t.Rim, Blur = Blur * t.Frost,
            Tint = Tint * t.Tint, Saturation = Saturation * t.Saturation, Shadow = Shadow * t.Shadow, Depth = Depth * t.Shadow,
        };
    }
}

/// <summary>
/// ED-NEW-glasslook-1: THE ONE PLACE each surface's glass look is set, for each Look of the user's (Clear, Regular, Tinted: Apple's Liquid Glass
/// setting, iOS 27 the owner's target). Regular is the release look on every surface until the owner approves the design canvas
/// (E:/Droppy/work/design/glass-ui); Clear and Tinted are candidates until the canvas reports their numbers; the approved values go here, surface by
/// surface and Look by Look. A piece whose effect names its own <see cref="GlassEffect.Look"/> takes that instead. Over either, the user's
/// <see cref="Tuning"/> (ED-NEW-glasslook-2).
/// </summary>
public static class GlassLooks
{
    /// <summary>The look of <paramref name="surface"/> in the Regular Look.</summary>
    public static GlassLook For(GlassSurface surface) => For(surface, GlassVariant.Regular);

    /// <summary>The look of <paramref name="surface"/> in <paramref name="variant"/>.</summary>
    public static GlassLook For(GlassSurface surface, GlassVariant variant) => (variant, surface) switch
    {
        (GlassVariant.Clear, _) => GlassLook.Premium,
        (GlassVariant.Tinted, _) => GlassLook.TintedCandidate,
        (_, GlassSurface.Window) => GlassLook.Release,
        (_, GlassSurface.Panel) => GlassLook.Release,
        (_, GlassSurface.Composer) => GlassLook.Release,
        (_, GlassSurface.Capsule) => GlassLook.Release,
        (_, GlassSurface.Button) => GlassLook.Release,
        (_, GlassSurface.Inner) => GlassLook.Release,
        _ => GlassLook.Release,
    };

    // A Look's crossfade (Blend): from which Look, and how far toward the tuning's.
    private static GlassVariant? blendFrom;
    private static double blendProgress = 1;

    /// <summary>ED-NEW-glasslook-2: a change of Look crossfades: every surface's look is <paramref name="progress"/> of the way from its look in
    /// <paramref name="from"/> to its look in the tuning's Look (each frame of the page's glide), with no pop; 1 ends the crossfade. Uniforms only.</summary>
    public static void Blend(GlassVariant from, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        (GlassVariant?, double) next = t >= 1 ? (null, 1) : (from, t);
        if (next == (blendFrom, blendProgress)) return;
        (blendFrom, blendProgress) = next;
        GlassMaterial.LookChanged();
    }

    private static GlassSettings tuning = GlassSettings.Default;

    /// <summary>
    /// ED-NEW-glasslook-2: the user's Glass settings, over every surface's look (the app mirrors AppSettings.Glass here, as it mirrors the theme into
    /// ThemeManager). A change redraws every glass piece of every window with new uniforms on the next composition update: no layout pass, no new
    /// program (queued on the UI thread when set from another).
    /// </summary>
    public static GlassSettings Tuning
    {
        get => tuning;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var held = value.Held();
            if (held == tuning) return;
            tuning = held;
            Changed?.Invoke();
            GlassMaterial.LookChanged();
        }
    }

    /// <summary>Raised after <see cref="Tuning"/> changes, on the thread that changed it.</summary>
    public static event Action? Changed;

    /// <summary>The surface a glass is on: its effect's own <see cref="GlassEffect.Surface"/>, else told from its shape: a rectangle with no corner
    /// is a window's glass, the composer's measured-circular corner the composer, a capsule or circle a button when it responds to the pointer or
    /// carries a tint and a quiet capsule otherwise, any other rounded rectangle a panel.</summary>
    public static GlassSurface SurfaceOf(GlassEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (effect.Surface is { } surface) return surface;
        var shape = effect.Shape;
        if (shape.Kind == GlassShapeKind.RoundedRectangle)
            return shape.CircularInBand ? GlassSurface.Composer : (shape.CornerRadius ?? 0) <= 0 ? GlassSurface.Window : GlassSurface.Panel;
        return effect.IsInteractive || effect.TintColor is not null ? GlassSurface.Button : GlassSurface.Capsule;
    }

    /// <summary>The look a glass is drawn with: its own, else its surface's, under the user's tuning.</summary>
    public static GlassLook Of(GlassEffect effect) => Of(effect, nested: false);

    /// <summary>The look a glass is drawn with, <paramref name="nested"/> inside another glass piece: its own, else the Inner surface's when it is
    /// nested and names no surface itself, else its surface's; under the user's tuning (a quiet look stays quiet).</summary>
    public static GlassLook Of(GlassEffect effect, bool nested)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var surface = nested && effect.Surface is null ? GlassSurface.Inner : SurfaceOf(effect);
        var look = effect.Look ?? (blendFrom is { } from && from != tuning.Look
            ? For(surface, from).Toward(For(surface, tuning.Look), blendProgress)
            : For(surface, tuning.Look));
        return look.Quiet ? look : look.Under(tuning);
    }
}
