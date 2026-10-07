// PORT-ONLY: multi-account step 3a (OD-0115 composer=A). The capsule's colour tokens in both themes, read from the design page's own :root and dark blocks (multi-account.html "the capsule", --tx --tx2 --tx3 --trk --fill --hot --rim --pgHi --capSh --capS --glowIn --plateRing --pacRing).
using Avalonia.Media;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal sealed record CapsuleTokens(Color Tx, Color Tx2, Color Tx3, Color Track, Color Fill, Color Hot, Color Rim, Color Highlight, Color Shadow, Color Card, Color GlowInner, Color GlowShadow, Color PlateRing, Color FocusRing, Color Surface, Color SurfaceHover)
{
    private static Color Rgba(byte r, byte g, byte b, double a) => Color.FromArgb((byte)Math.Round(a * 255), r, g, b);

    public static CapsuleTokens Light { get; } = new(
        Tx: Rgba(0x1C, 0x1C, 0x1E, 1), Tx2: Rgba(28, 28, 30, .66), Tx3: Rgba(28, 28, 30, .42), Track: Rgba(0, 0, 0, .10), Fill: Rgba(28, 28, 30, .62), Hot: Rgba(0xB2, 0x5E, 0x00, 1),
        Rim: Rgba(0, 0, 0, .22), Highlight: Rgba(255, 255, 255, .85), Shadow: Rgba(30, 40, 60, .10), Card: Rgba(0xF3, 0xF4, 0xF6, 1), GlowInner: Rgba(248, 248, 250, .96), GlowShadow: Rgba(20, 30, 50, .20),
        PlateRing: Rgba(255, 255, 255, .9), FocusRing: Rgba(0, 103, 192, .22), Surface: Rgba(255, 255, 255, .55), SurfaceHover: Rgba(255, 255, 255, .82));

    public static CapsuleTokens Dark { get; } = new(
        Tx: Rgba(0xEC, 0xEC, 0xEC, 1), Tx2: Rgba(236, 236, 236, .66), Tx3: Rgba(236, 236, 236, .42), Track: Rgba(255, 255, 255, .12), Fill: Rgba(236, 236, 236, .66), Hot: Rgba(0xF2, 0xB0, 0x4A, 1),
        Rim: Rgba(255, 255, 255, .22), Highlight: Rgba(255, 255, 255, .12), Shadow: Rgba(0, 0, 0, .30), Card: Rgba(0x2C, 0x2F, 0x35, 1), GlowInner: Rgba(0x0B, 0x0C, 0x10, 1), GlowShadow: Rgba(0, 0, 0, .70),
        PlateRing: Rgba(20, 24, 30, .9), FocusRing: Rgba(76, 194, 255, .24), Surface: Rgba(0, 0, 0, .20), SurfaceHover: Rgba(0, 0, 0, .30));

    public static CapsuleTokens For(bool dark) => dark ? Dark : Light;

    public static Color With(Color color, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    public static Color Scaled(Color color, double opacity) => Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);

    public static Color Of((byte R, byte G, byte B) rgb, double alpha = 1) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), rgb.R, rgb.G, rgb.B);
}
