// PORT-ONLY: SwiftUI's `.truncationMode(.middle)` (AttachmentPreview.swift:353, :427) has no Avalonia TextTrimming
// counterpart: 12.1.2's TextTrimming offers only CharacterEllipsis, WordEllipsis, LeadingCharacterEllipsis and
// PrefixCharacterEllipsis (all end- or start-elided), none eliding the centre. This measures the full text against
// the control's own font at its allotted width and, only when it does not fit, elides a run from its centre.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.UI.Port;

public sealed class MiddleTruncatingTextBlock : TextBlock
{
    private const string Ellipsis = "…"; // U+2026 HORIZONTAL ELLIPSIS

    private string full = "";

    /// <summary>The untruncated text; <see cref="TextBlock.Text"/> is derived from it and the measured width.</summary>
    public string FullText
    {
        get => full;
        set
        {
            if (full == value) return;
            full = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ApplyTrimming(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    private void ApplyTrimming(double maxWidth)
    {
        if (double.IsInfinity(maxWidth) || full.Length == 0 || Measure(full) <= maxWidth)
        {
            Text = full;
            return;
        }
        // Binary search the longest kept length (split evenly between head and tail) that still fits.
        var lo = 0;
        var hi = full.Length;
        var best = Ellipsis;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var candidate = Truncated(mid);
            if (Measure(candidate) <= maxWidth)
            {
                best = candidate;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        Text = best;
    }

    private string Truncated(int kept)
    {
        if (kept >= full.Length) return full;
        var headLength = kept / 2;
        var tailLength = kept - headLength;
        return string.Concat(full.AsSpan(0, headLength), Ellipsis, full.AsSpan(full.Length - tailLength));
    }

    private double Measure(string text)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, null);
        return formatted.Width;
    }
}
