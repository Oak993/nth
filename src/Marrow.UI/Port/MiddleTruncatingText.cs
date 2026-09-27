// PORT-ONLY: SwiftUI's `.truncationMode(.middle)` (AttachmentPreview.swift:353, :427) has no Avalonia TextTrimming
// counterpart: 12.1.2's TextTrimming offers only CharacterEllipsis, WordEllipsis, LeadingCharacterEllipsis and
// PrefixCharacterEllipsis (all end- or start-elided), none eliding the centre. This measures the full text against
// the control's own font at its allotted width and, only when it does not fit, elides a run from its centre.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

public sealed class MiddleTruncatingTextBlock : TextBlock
{
    private const string Ellipsis = "…"; // U+2026 HORIZONTAL ELLIPSIS

    // ED-217, TimelineRows.swift:1969-1973. Single-line callers retain the original path below.
    public static readonly StyledProperty<int> LineLimitProperty =
        AvaloniaProperty.Register<MiddleTruncatingTextBlock, int>(nameof(LineLimit), 1, validate: value => value > 0);

    static MiddleTruncatingTextBlock() => AffectsMeasure<MiddleTruncatingTextBlock>(LineLimitProperty);

    public MiddleTruncatingTextBlock()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.Property != LineLimitProperty) return;
            MaxLines = LineLimit;
            TextWrapping = LineLimit > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
        };
    }

    public int LineLimit { get => GetValue(LineLimitProperty); set => SetValue(LineLimitProperty, value); }

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
        if (LineLimit == 1) ApplyTrimming(availableSize.Width);
        else ApplyWrappedTrimming(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    // ED-217: preserve normal wraps through the penultimate line; elide only the final line's remainder.
    // Exact macOS wrapped-middle placement remains UNVERIFIED under OD-0001.
    private void ApplyWrappedTrimming(double maxWidth)
    {
        if (double.IsInfinity(maxWidth) || full.Length == 0) { Text = full; return; }
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        using var layout = new TextLayout(full, typeface, FontSize, Foreground,
            textWrapping: TextWrapping.Wrap, flowDirection: FlowDirection, maxWidth: Math.Max(0, maxWidth));
        if (layout.TextLines.Count <= LineLimit) { Text = full; return; }
        var previous = layout.TextLines[LineLimit - 2];
        var prefixEnd = previous.FirstTextSourceIndex + previous.Length - previous.NewLineLength;
        var remainderStart = layout.TextLines[LineLimit - 1].FirstTextSourceIndex;
        var remainder = full[remainderStart..];
        var characters = SwiftString.Characters(remainder).ToList();
        var lo = 0;
        var hi = characters.Count;
        var best = Ellipsis;
        while (lo <= hi)
        {
            var kept = (lo + hi) / 2;
            var headLength = kept / 2;
            var candidate = string.Concat(characters.Take(headLength)) + Ellipsis
                + string.Concat(characters.Skip(characters.Count - (kept - headLength)));
            if (!candidate.Any(FoundationText.IsNewline) && Measure(candidate) <= maxWidth) { best = candidate; lo = kept + 1; }
            else hi = kept - 1;
        }
        Text = full[..prefixEnd] + "\n" + best;
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
