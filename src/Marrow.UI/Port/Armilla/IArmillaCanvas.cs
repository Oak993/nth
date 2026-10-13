// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5; ED-NEW-dextra-as8-1). No Swift twin.
// The drawing calls Armilla makes, in the slot's own coordinates: the origin is the slot's centre, y points down, angles are in
// degrees from twelve o'clock, clockwise, as the canvas's 2D context draws them. The Skia canvas draws them on the compositor's
// render thread; tests draw into a null canvas or a bitmap and read the engine's measured extent.
namespace Marrow.UI.Port.Armilla;

internal interface IArmillaCanvas
{
    void Save();

    void Restore();

    void Translate(double dx, double dy);

    /// <summary>Turns what follows by <paramref name="degrees"/>, clockwise.</summary>
    void Rotate(double degrees);

    /// <summary>A stroked arc of radius <paramref name="r"/> from <paramref name="fromDeg"/> to <paramref name="toDeg"/> (clockwise from twelve).</summary>
    void StrokeArc(double r, double fromDeg, double toDeg, double width, ArmillaRgb color, double alpha, bool roundCap);

    void StrokeLine(double x0, double y0, double x1, double y1, double width, ArmillaRgb color, double alpha, bool roundCap);

    void FillCircle(double x, double y, double r, ArmillaRgb color, double alpha);

    /// <summary>The core haze: a radial gradient from <paramref name="centreAlpha"/> at the centre, 0.55 of it halfway, to nothing at <paramref name="r"/>.</summary>
    void FillHaze(double r, ArmillaRgb color, double centreAlpha);

    /// <summary>An Astur's node shape centred on (x, y), drawn with one radius for all six shapes.</summary>
    void FillShape(ArmillaShape shape, double x, double y, double r, ArmillaRgb color, double alpha);

    /// <summary>A centre glyph of side <paramref name="unit"/> (a check, a hollow square or a filled square).</summary>
    void Glyph(ArmillaGlyph glyph, double unit, double lineWidth, ArmillaRgb color, double alpha);
}

/// <summary>Draws nothing: for measuring a pose's extent and for ticking a mark that is not on screen.</summary>
internal sealed class NullArmillaCanvas : IArmillaCanvas
{
    public static readonly NullArmillaCanvas Instance = new();

    public void Save() { }

    public void Restore() { }

    public void Translate(double dx, double dy) { }

    public void Rotate(double degrees) { }

    public void StrokeArc(double r, double fromDeg, double toDeg, double width, ArmillaRgb color, double alpha, bool roundCap) { }

    public void StrokeLine(double x0, double y0, double x1, double y1, double width, ArmillaRgb color, double alpha, bool roundCap) { }

    public void FillCircle(double x, double y, double r, ArmillaRgb color, double alpha) { }

    public void FillHaze(double r, ArmillaRgb color, double centreAlpha) { }

    public void FillShape(ArmillaShape shape, double x, double y, double r, ArmillaRgb color, double alpha) { }

    public void Glyph(ArmillaGlyph glyph, double unit, double lineWidth, ArmillaRgb color, double alpha) { }
}
