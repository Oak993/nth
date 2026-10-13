using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Setup.Platform;

namespace Marrow.Setup.Theme;

/// <summary>A CSS box-shadow layer in DIP: offset, blur radius, spread, colour, inset.</summary>
public readonly record struct ShadowLayer(double X, double Y, double Blur, double Spread, Color Color, bool Inset = false)
{
    public BoxShadow ToBoxShadow() => new() { OffsetX = X, OffsetY = Y, Blur = Blur, Spread = Spread, Color = Color, IsInset = Inset };

    public static BoxShadows Many(params ShadowLayer[] layers)
    {
        if (layers.Length == 0) return default;
        var first = layers[0].ToBoxShadow();
        return layers.Length == 1 ? new BoxShadows(first) : new BoxShadows(first, [.. layers.Skip(1).Select(l => l.ToBoxShadow())]);
    }
}

/// <summary>The desktop previews' colours (the canvas's <c>--dk-*</c> tokens; every colour of the previews is one).</summary>
public sealed record DeskTokens(
    Color Wall,
    Color GlowA,
    Color GlowB,
    Color GlowC,
    Color Edge,
    Color TopLight,
    ShadowLayer[] Lift,
    ShadowLayer[] LiftHover,
    Color BinGlass,
    Color BinRim,
    Color BinLine,
    Color Label,
    bool LabelHalo,
    Color Halo,
    Color HaloEdge,
    Color ContactShadow,
    Color Bar,
    Color BarEdge,
    Color Dots,
    bool Outlined);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, the canvas's tokens; unit U2, ED-NEW-inst-u2-1). The screens' colours beyond the
/// three inks the shell owns (<see cref="SetupPalette"/>): the glass rows, the liquid-glass buttons, bar B and the getting-
/// ready rows, the mark's lift, the details log and the desktop previews, for dark, light and a Windows contrast theme
/// (system colours: no glass, no shadows, the highlight for the bar and the primary button, outlined icons). They live in
/// the window's resources under <see cref="Key"/>, so a control redraws when the theme, the contrast or the colours change.
/// </summary>
public sealed record ScreenTokens(
    string Name,
    bool Contrast,
    Color Card,
    Color CardEdge,
    Color Separator,
    Color PrimaryTop,
    Color PrimaryBottom,
    Color PrimaryInk,
    Color PrimaryRim,
    double PrimaryGloss,
    ShadowLayer[] PrimaryShadow,
    Color SecondaryFill,
    Color SecondaryHover,
    Color SecondaryInk,
    Color SecondaryHoverInk,
    Color SecondaryRim,
    Color DestructiveRim,
    Color Track,
    Color TrackEdge,
    Color FillTop,
    Color FillBottom,
    Color Shine,
    Color SpinnerHead,
    Color Strong,
    Color On,
    Color LogFill,
    ShadowLayer[] MarkLift,
    Color SwitchAccent,
    Color SwitchOnFill,
    Color SwitchKnob,
    Color SwitchKnobOn,
    Color SwitchOutline,
    DeskTokens Desk)
{
    public const string Key = "setup.tokens";

    private static Color W(double a) => SetupPalette.Rgba(255, 255, 255, a);

    private static Color B(double a) => SetupPalette.Rgba(0, 0, 0, a);

    /// <summary>The canvas's dark tokens.</summary>
    public static ScreenTokens Dark { get; } = new(
        "dark", false,
        W(.045), W(.07), W(.06),
        SetupPalette.Hex(0xF6F7F9), SetupPalette.Hex(0xC9CED8), SetupPalette.Hex(0x14161C), W(.75), .55,
        [new(0, 1, 0, 0, W(.25), true), new(0, 8, 20, -8, B(.7)), new(0, 0, 0, .5, B(.4))],
        W(.07), W(.11), SetupPalette.Hex(0xEEF0F4), SetupPalette.Hex(0xEEF0F4), W(.10), SetupPalette.Rgba(255, 157, 133, .38),
        W(.10), Colors.Transparent, SetupPalette.Hex(0xEEF1F5), SetupPalette.Hex(0x9EA6B4), W(.85),
        SetupPalette.Hex(0xD3D8E0), SetupPalette.Hex(0xF2F4F8), SetupPalette.Hex(0x15171D),
        SetupPalette.Rgba(18, 19, 23, .61),
        [new(0, 16, 30, -10, SetupPalette.Rgba(6, 8, 12, .75)), new(0, 4, 10, 0, SetupPalette.Rgba(6, 8, 12, .35))],
        SetupPalette.Hex(0xC7C7C7), Colors.Transparent, Colors.White, Colors.White, Colors.Transparent,
        new DeskTokens(
            SetupPalette.Hex(0x121A31), SetupPalette.Rgba(98, 132, 232, .78), SetupPalette.Rgba(150, 94, 214, .62), SetupPalette.Rgba(36, 170, 182, .5),
            W(.2), W(.16),
            [new(0, 0, 0, 1, B(.6)), new(0, 2, 4, 0, B(.35)), new(0, 12, 26, -10, B(.75))],
            [new(0, 0, 0, 1, B(.6)), new(0, 4, 8, 0, B(.35)), new(0, 18, 32, -12, B(.8))],
            SetupPalette.Rgba(214, 226, 255, .16), SetupPalette.Rgba(236, 242, 255, .6), SetupPalette.Rgba(236, 242, 255, .85),
            Colors.White, false, W(.34), W(.08), SetupPalette.Rgba(4, 6, 10, .55),
            SetupPalette.Rgba(22, 24, 29, .72), W(.11), W(.55), false));

    /// <summary>The canvas's light tokens.</summary>
    public static ScreenTokens Light { get; } = new(
        "light", false,
        W(.62), SetupPalette.Rgba(20, 28, 48, .08), SetupPalette.Rgba(20, 28, 48, .07),
        SetupPalette.Hex(0x3A404D), SetupPalette.Hex(0x1A1D24), Colors.White, W(.22), .18,
        [new(0, 1, 0, 0, W(.18), true), new(0, 8, 18, -8, SetupPalette.Rgba(20, 28, 48, .55)), new(0, 0, 0, .5, B(.5))],
        W(.7), W(.95), SetupPalette.Hex(0x1D2027), SetupPalette.Hex(0x1D2027), SetupPalette.Rgba(20, 28, 48, .12), SetupPalette.Rgba(163, 54, 31, .38),
        SetupPalette.Rgba(20, 28, 48, .10), Colors.Transparent, SetupPalette.Hex(0x5A6273), SetupPalette.Hex(0x20242D), W(.5),
        SetupPalette.Hex(0x343A47), SetupPalette.Hex(0x15171D), Colors.White,
        W(.55),
        [new(0, 16, 30, -10, SetupPalette.Rgba(24, 30, 48, .45)), new(0, 4, 10, 0, SetupPalette.Rgba(24, 30, 48, .18))],
        SetupPalette.Hex(0xC7C7C7), Colors.Transparent, Colors.White, Colors.White, Colors.Transparent,
        new DeskTokens(
            SetupPalette.Hex(0xB4C4E6), SetupPalette.Rgba(98, 132, 228, .62), SetupPalette.Rgba(178, 132, 228, .5), SetupPalette.Rgba(84, 192, 200, .48),
            SetupPalette.Rgba(20, 28, 48, .18), W(.75),
            [new(0, 0, 0, 1, SetupPalette.Rgba(20, 28, 48, .08)), new(0, 2, 4, 0, SetupPalette.Rgba(24, 30, 48, .12)), new(0, 12, 24, -10, SetupPalette.Rgba(24, 30, 48, .38))],
            [new(0, 0, 0, 1, SetupPalette.Rgba(20, 28, 48, .08)), new(0, 4, 8, 0, SetupPalette.Rgba(24, 30, 48, .14)), new(0, 18, 30, -12, SetupPalette.Rgba(24, 30, 48, .45))],
            W(.55), SetupPalette.Rgba(28, 40, 72, .5), SetupPalette.Rgba(28, 40, 72, .75),
            SetupPalette.Hex(0x141C2C), true, SetupPalette.Rgba(52, 86, 186, .30), SetupPalette.Rgba(52, 86, 186, .08), SetupPalette.Rgba(24, 34, 64, .42),
            SetupPalette.Rgba(243, 245, 249, .88), SetupPalette.Rgba(20, 28, 48, .12), SetupPalette.Rgba(20, 28, 48, .5), false));

    /// <summary>
    /// A Windows contrast theme: the canvas's Contrast › High mapped onto the system colours. Rows and buttons are the window
    /// colour with a window-text edge; the primary button, the bar and a switch that is on use the highlight; links, the
    /// destructive label and its edge use the hyperlink colour; there is no glass, no gloss and no shadow; the previews are
    /// the window colour with a window-text edge and outlined icons.
    /// </summary>
    public static ScreenTokens HighContrast(SystemColorSet system) => new(
        "contrast", true,
        system.Window, system.WindowText, system.WindowText,
        system.Highlight, system.Highlight, system.HighlightText, system.Highlight, 0,
        [],
        system.Window, system.Highlight, system.WindowText, system.HighlightText, system.WindowText, system.HotLight,
        system.Window, system.WindowText, system.Highlight, system.Highlight, Colors.Transparent,
        system.Highlight, system.WindowText, system.Window,
        system.Window,
        [],
        system.WindowText, system.Highlight, system.WindowText, system.Window, system.WindowText,
        new DeskTokens(
            system.Window, Colors.Transparent, Colors.Transparent, Colors.Transparent,
            system.WindowText, system.WindowText, [], [],
            Colors.Transparent, system.WindowText, system.WindowText,
            system.WindowText, false, Colors.Transparent, Colors.Transparent, Colors.Transparent,
            system.Window, system.WindowText, system.WindowText, true));

    /// <summary>The tokens for what Windows is set to now.</summary>
    public static ScreenTokens For(SystemLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        return look.HighContrast ? HighContrast(look.Colors) : look.Dark ? Dark : Light;
    }

    /// <summary>Writes the tokens into <paramref name="resources"/> (the window's), where every screen control reads them.</summary>
    public static void Apply(IResourceDictionary resources, ScreenTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resources[Key] = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    /// <summary>The tokens a control sees now (dark until it is in a window that has them).</summary>
    public static ScreenTokens Of(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.TryFindResource(Key, out var value) && value is ScreenTokens tokens ? tokens : Dark;
    }

    /// <summary>Calls <paramref name="changed"/> whenever the window's tokens change (and once with the current ones).</summary>
    public static IDisposable Watch(Control control, Action<ScreenTokens> changed)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(changed);
        return control.GetResourceObservable(Key).Subscribe(new Observer(value => changed(value as ScreenTokens ?? Dark)));
    }

    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(object? value) => next(value);
    }
}
