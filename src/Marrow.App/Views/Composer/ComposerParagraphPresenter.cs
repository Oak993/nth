// PORT-ONLY: OD-0091, voice-SPEC V-8.3/V-8.9. Keep TextPresenter's real editor interaction, selection, IME and caret on the same paragraph layout as dictation.
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace Marrow.App.Views.Composer;

internal sealed class ComposerParagraphPresenter : TextPresenter
{
    protected override TextLayout CreateTextLayout()
    {
        // Use the native layout's exact private measure/arrange constraints (including its ceil and zero-to-infinity rules).
        using var native = base.CreateTextLayout();
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
            native.MaxWidth, native.MaxHeight, LineHeight, LetterSpacing, FontFeatures, spans);
    }
}
