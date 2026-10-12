// PORT-ONLY: OD-0091, voice-SPEC V-8.3/V-8.9. Keep TextPresenter's real editor interaction, selection, IME and caret on the same paragraph layout as dictation.
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace Marrow.App.Views.Composer;

internal sealed class ComposerParagraphPresenter : TextPresenter
{
    protected override TextLayout CreateTextLayout()
    {
        // Use the native layout's exact private measure/arrange constraint (including its ceil and zero-to-infinity rules), read
        // directly instead of from a native layout built and discarded at every rebuild (ED-NEW-lean-20).
        var constraint = NativeConstraint(this);
        var text = Text ?? "";
        var caret = Math.Clamp(CaretIndex, 0, text.Length);
        var preedit = PreeditText ?? "";
        if (preedit.Length > 0) text = text.Insert(caret, preedit);
        var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        IReadOnlyList<ValueSpan<TextRunProperties>>? spans = null;
        if (preedit.Length > 0)
            spans = [new(caret, preedit.Length, new GenericTextRunProperties(face, FontSize, TextDecorations.Underline, Foreground, fontFeatures: FontFeatures))];
        else if (ShowSelectionHighlight && SelectionStart != SelectionEnd && SelectionForegroundBrush is { } selection)
            spans = [new(Math.Min(SelectionStart, SelectionEnd), Math.Abs(SelectionEnd - SelectionStart),
                new GenericTextRunProperties(face, FontSize, foregroundBrush: selection, fontFeatures: FontFeatures))];
        if (PasswordChar != default && !RevealPassword) text = new string(PasswordChar, text.Length);
        return ComposerParagraphLayout.Create(text, face, FontSize, Foreground, TextAlignment, TextWrapping, FlowDirection,
            Unbounded(constraint.Width), Unbounded(constraint.Height), LineHeight, LetterSpacing, FontFeatures, spans);
    }

    // Avalonia 12.1.2 TextPresenter._constraint: set by MeasureOverride, and by ArrangeOverride to (ceil(width), infinity) when the
    // arranged width differs. Version-pinned like the formatter adapter: a renamed field fails visibly (MissingFieldException).
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_constraint")]
    private static extern ref Size NativeConstraint(TextPresenter presenter);

    // TextPresenter.CreateTextLayoutInternal: a zero bound (MathUtilities.IsZero) lays out unbounded.
    private static double Unbounded(double bound) => Math.Abs(bound) < 2.220446049250313E-15 ? double.PositiveInfinity : bound;
}
