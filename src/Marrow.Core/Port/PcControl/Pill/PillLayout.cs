// PORT-ONLY: Tactus pill v2, the capsule's width (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, canvas v4). Left to right: 8 DIP,
// the mark's 32 DIP slot with the live glyph (50 DIP in all), 8 DIP, the two lines of text, 8 DIP, the state's button group
// and 6 DIP, Stop in its own slot and 6 DIP, then 6 DIP. The title always fits; the second line fits too, except the step
// line while working (it changes with every step and would make the pill breathe). When the text would not fit beside the
// full Stop, Stop folds its key cap away; an ask always folds it. The width is 200 to 440 DIP.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>What the pill's width depends on, in DIP (text widths as measured, before the 2 DIP of slack).</summary>
public sealed record PillLayoutInput(
    double TitleWidth,
    double SecondLineWidth,
    double GroupWidth,
    double StopWidth,
    double KeyCapWidth,
    bool ShowsStop,
    bool Working,
    bool Ask);

/// <summary>The width, whether Stop's key cap is folded, and the text column's width.</summary>
public sealed record PillLayoutResult(double Width, bool CapFolded, double TextWidth);

public static class PillLayout
{
    public const double Height = 48;
    public const double Radius = 24;
    public const double MinWidth = 200;
    public const double MaxWidth = 440;

    /// <summary>The full-screen dot (DESIGN.md 10.3 "Auto-dodge" row 4; canvas `.sdot`): 32 DIP across, a circle.</summary>
    public const double DotSize = 32;

    /// <summary>The left padding, the mark and glyph, and the text margins and right padding around the text column.</summary>
    public const double Chrome = 8 + 50 + 8 + 8 + 6;

    /// <summary>The gap after the button group and after Stop.</summary>
    public const double Gap = 6;

    /// <summary>The text column's width beside buttons, at least.</summary>
    public const double MinTextBesideButtons = 195;

    /// <summary>The text column's width with no buttons, at least.</summary>
    public const double MinTextAlone = 120;

    /// <summary>Text is measured with this much slack, so a rounding never clips a glyph.</summary>
    public const double TextSlack = 2;

    /// <summary>The fixed part of the width: everything but the text column.</summary>
    public static double Fixed(PillLayoutInput input, bool capFolded)
    {
        var stop = input.ShowsStop && input.StopWidth > 0 ? input.StopWidth - (capFolded ? input.KeyCapWidth : 0) : 0;
        return Chrome + (input.GroupWidth > 0 ? input.GroupWidth + Gap : 0) + (stop > 0 ? stop + Gap : 0);
    }

    public static PillLayoutResult Compute(PillLayoutInput input)
    {
        var title = Math.Ceiling(input.TitleWidth) + TextSlack;
        var second = Math.Ceiling(input.SecondLineWidth) + TextSlack;
        // While working, the step line is left out of the fit (it truncates); an ask always counts it.
        var need = !input.Working || input.Ask ? Math.Max(title, second) : title;
        var folded = input.Ask || Fixed(input, capFolded: false) + need > MaxWidth;
        var fixedWidth = Fixed(input, folded);
        var buttons = input.GroupWidth > 0 || (input.ShowsStop && input.StopWidth > 0);
        var text = buttons ? Math.Max(MinTextBesideButtons, need) : Math.Max(MinTextAlone, Math.Max(title, second));
        var width = Math.Clamp(fixedWidth + text, MinWidth, MaxWidth);
        return new PillLayoutResult(width, folded, width - fixedWidth);
    }
}
