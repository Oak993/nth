// PORT-ONLY: web surfaces W16c (DESIGN.md 7.5 "monochrome line art with the accent only for the current value and focus ... light and
// dark themes come free. Never another product's look"; 10 text 4.5:1; ED-NEW-web-w16c-1). No Swift counterpart. The colours
// Mermaid draws a diagram in: its base theme with variables taken from nth's own tokens (a node is a raised chip on the well, a
// line is the secondary text colour, text is the primary), resolved to opaque colours because Mermaid does colour maths on them.
// A tone (info, success, warning, danger) tints a few categories (pie slices, notes) and the accent is never used.
using Avalonia.Styling;
using Marrow.Core.Web;

namespace Marrow.UI.NthUi.Diagram;

public static class NthUiDiagramTheme
{
    /// <summary>The face Mermaid measures and draws labels in. The hidden page cannot load nth's embedded faces, so the system's
    /// own is used for both the measuring and the drawing, which keeps every label inside its box.</summary>
    public const string FontFamily = "Segoe UI Variable, Segoe UI, sans-serif";

    public const double FontSize = 14;

    /// <summary>The theme for the thread's light or dark appearance.</summary>
    public static DiagramTheme For(ThemeVariant? variant)
    {
        var theme = variant ?? ThemeVariant.Light;
        var dark = theme == ThemeVariant.Dark;
        var thread = NthUiTokens.Backdrop.Resolve(theme);
        var well = NthUiTokens.Over(NthUiTokens.Well.Resolve(theme), thread);
        string Hex(Avalonia.Media.Color c) => NthSvgColor.ToText(NthUiTokens.Over(c, well));
        string HexOn(Avalonia.Media.Color c, Avalonia.Media.Color under) => NthSvgColor.ToText(NthUiTokens.Over(c, under));

        var chip = NthUiTokens.Over(NthUiTokens.Chosen.Resolve(theme), well);
        var text = Hex(NthUiTokens.Text1.Resolve(theme));
        var line = Hex(NthUiTokens.Text2.Resolve(theme));
        var border = Hex(NthUiTokens.Text3.Resolve(theme));
        var cluster = Hex(NthUiTokens.ControlFill.Resolve(theme));
        var rim = Hex(NthUiTokens.ControlRim.Resolve(theme));
        var noteFill = Hex(NthUiTokens.ToneFill("warning").Resolve(theme));
        var noteBorder = Hex(NthUiTokens.Tone("warning").Resolve(theme));
        var chipHex = NthSvgColor.ToText(chip);
        var chipText = HexOn(NthUiTokens.Text1.Resolve(theme), chip);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["background"] = NthSvgColor.ToText(well),
            ["fontFamily"] = FontFamily,
            ["fontSize"] = FontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "px",
            ["primaryColor"] = chipHex,
            ["primaryTextColor"] = chipText,
            ["primaryBorderColor"] = border,
            ["secondaryColor"] = Hex(NthUiTokens.ToneFill("info").Resolve(theme)),
            ["secondaryTextColor"] = text,
            ["secondaryBorderColor"] = border,
            ["tertiaryColor"] = cluster,
            ["tertiaryTextColor"] = text,
            ["tertiaryBorderColor"] = rim,
            ["mainBkg"] = chipHex,
            ["nodeBkg"] = chipHex,
            ["nodeBorder"] = border,
            ["nodeTextColor"] = chipText,
            ["textColor"] = text,
            ["titleColor"] = text,
            ["lineColor"] = line,
            ["arrowheadColor"] = line,
            ["defaultLinkColor"] = line,
            ["edgeLabelBackground"] = NthSvgColor.ToText(well),
            ["clusterBkg"] = cluster,
            ["clusterBorder"] = rim,
            ["noteBkgColor"] = noteFill,
            ["noteTextColor"] = text,
            ["noteBorderColor"] = noteBorder,
            ["actorBkg"] = chipHex,
            ["actorBorder"] = border,
            ["actorTextColor"] = chipText,
            ["actorLineColor"] = rim,
            ["signalColor"] = line,
            ["signalTextColor"] = text,
            ["labelBoxBkgColor"] = cluster,
            ["labelBoxBorderColor"] = rim,
            ["labelTextColor"] = text,
            ["loopTextColor"] = text,
            ["activationBkgColor"] = Hex(NthUiTokens.ControlHover.Resolve(theme)),
            ["activationBorderColor"] = border,
            ["sequenceNumberColor"] = NthSvgColor.ToText(NthUiTokens.Over(NthUiTokens.OnStrong.Resolve(theme), well)),
            ["altBackground"] = cluster,
            ["stateBkg"] = chipHex,
            ["stateLabelColor"] = chipText,
            ["compositeBackground"] = cluster,
            ["compositeTitleBackground"] = chipHex,
            ["compositeBorder"] = rim,
            ["transitionColor"] = line,
            ["transitionLabelColor"] = text,
            ["classText"] = chipText,
            ["gridColor"] = rim,
            ["taskBkgColor"] = Hex(NthUiTokens.ToneFill("info").Resolve(theme)),
            ["taskBorderColor"] = Hex(NthUiTokens.Tone("info").Resolve(theme)),
            ["taskTextColor"] = text,
            ["taskTextDarkColor"] = text,
            ["taskTextLightColor"] = text,
            ["taskTextOutsideColor"] = text,
            ["activeTaskBkgColor"] = Hex(NthUiTokens.ToneFill("success").Resolve(theme)),
            ["activeTaskBorderColor"] = Hex(NthUiTokens.Tone("success").Resolve(theme)),
            ["doneTaskBkgColor"] = cluster,
            ["doneTaskBorderColor"] = border,
            ["critBkgColor"] = Hex(NthUiTokens.ToneFill("danger").Resolve(theme)),
            ["critBorderColor"] = Hex(NthUiTokens.Tone("danger").Resolve(theme)),
            ["todayLineColor"] = Hex(NthUiTokens.Tone("danger").Resolve(theme)),
            ["pieTitleTextColor"] = text,
            ["pieSectionTextColor"] = text,
            ["pieLegendTextColor"] = text,
            ["pieStrokeColor"] = NthSvgColor.ToText(well),
            ["pieOuterStrokeColor"] = NthSvgColor.ToText(well),
        };

        // Pie slices and other categories: a neutral step, the four tones, then the same hues a step lighter or darker, so a
        // diagram with many categories stays readable in the thread's palette.
        var tones = new[] { "info", "success", "warning", "danger" };
        var slices = new List<string> { line };
        foreach (var tone in tones) slices.Add(NthSvgColor.ToText(NthUiTokens.Over(NthUiTokens.Tone(tone).Resolve(theme), well)));
        foreach (var tone in tones) slices.Add(NthSvgColor.ToText(NthUiTokens.Over(NthUiTokens.Tone(tone).Resolve(theme).WithAlpha(.55), well)));
        slices.Add(border);
        slices.Add(NthSvgColor.ToText(NthUiTokens.Over(NthUiTokens.Text1.Resolve(theme).WithAlpha(.4), well)));
        slices.Add(Hex(NthUiTokens.ControlRim.Resolve(theme)));
        slices.Add(Hex(NthUiTokens.ControlHover.Resolve(theme)));
        for (var i = 0; i < slices.Count && i < 12; i++) variables["pie" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)] = slices[i];
        return new DiagramTheme(dark, FontFamily, FontSize, variables);
    }
}

internal static class NthUiColorExtensions
{
    /// <summary>The colour with its alpha scaled (an opaque tone at 55 % becomes a see-through tone for compositing).</summary>
    public static Avalonia.Media.Color WithAlpha(this Avalonia.Media.Color color, double factor) =>
        Avalonia.Media.Color.FromArgb((byte)Math.Round(Math.Clamp(color.A * factor, 0, 255)), color.R, color.G, color.B);
}
