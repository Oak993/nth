// PORT-ONLY, owner-ruled addition (OD-0097 D; first-release voice typing, VT-12, voice-SPEC V-12.11): one answer of the language question.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// An answer capsule (voice-options.html <c>.vask .ach button</c>): 26 high, padding 0 11, radius 13, text 12.5 in <c>pgLabHi</c> on <c>pgXBg</c> with a 1 px <c>pgXRim</c> ring; hover brightens it by 10 %
/// (the design's <c>filter: brightness(1.1)</c>, eased over 0.15 s ease, nothing cuts: OD-0076); the picked one is the user's accent with white text and no ring (<c>.on</c>), its colours eased over 0.15 s ease.
/// </summary>
internal sealed class VoiceAnswerButton : Button
{
    internal const double CapsuleHeight = 26;
    internal const double CapsulePadding = 11;
    internal const double AnswerFontSize = 12.5;

    private static readonly FuncControlTemplate<Button> PlainTemplate = new((owner, scope) => new Border { Background = Brushes.Transparent });

    private readonly LookMotion motion;
    private readonly LookNumber hover;
    private readonly LookNumber picked;

    public VoiceAnswerButton(string code, string text)
    {
        Code = code;
        Text = text;
        motion = new LookMotion(this);
        hover = new LookNumber(motion, 0);
        picked = new LookNumber(motion, 0);
        Template = PlainTemplate;
        Background = Brushes.Transparent;
        BorderThickness = default;
        Padding = default;
        Focusable = true;
        IsTabStop = true;
        Height = CapsuleHeight;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        AutomationProperties.SetName(this, text);
        motion.Frame += InvalidateVisual;
        PointerEntered += (_, _) => hover.To(1, Animation.TimingCurve(0.25, 0.1, 0.25, 1, 0.15));
        PointerExited += (_, _) => hover.To(0, Animation.TimingCurve(0.25, 0.1, 0.25, 1, 0.15));
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The code it stores: en, hi, ur or auto.</summary>
    public string Code { get; }

    public string Text { get; }

    /// <summary>How far hover has come (0 to 1).</summary>
    internal double HoverAmount => hover.Value;

    /// <summary>How picked it is (0 to 1; the accent fill eases in over 0.15 s).</summary>
    internal double PickedAmount => picked.Value;

    /// <summary>The picked answer lights first (V-12.11): its fill goes to the accent and its text to white.</summary>
    public void Pick() => picked.To(1, Animation.TimingCurve(0.25, 0.1, 0.25, 1, 0.15));

    /// <summary>Back to unpicked, at once (the card opens again).</summary>
    public void Reset() => picked.Jump(0);

    protected override Size MeasureOverride(Size availableSize) => new(Math.Ceiling(TextWidth()) + 2 * CapsulePadding, CapsuleHeight);

    private double TextWidth()
    {
        using var layout = Layout(null);
        return layout.WidthIncludingTrailingWhitespace;
    }

    private Avalonia.Media.TextFormatting.TextLayout Layout(IBrush? brush)
    {
        var font = SwiftFont.System(AnswerFontSize).Resolve();
        return new Avalonia.Media.TextFormatting.TextLayout(Text, new Typeface(font.FontFamily, font.FontStyle, font.FontWeight), AnswerFontSize, brush);
    }

    public override void Render(DrawingContext context)
    {
        var tokens = VoicePillTokens.For(ActualThemeVariant);
        var bounds = new Rect(Bounds.Size);
        var round = new RoundedRect(bounds, CapsuleHeight / 2);
        var accent = Chrome.Accent.Resolve(ActualThemeVariant ?? Avalonia.Styling.ThemeVariant.Light);
        var pick = picked.Value;
        var lift = 1 + 0.1 * hover.Value; // filter: brightness(1.1)
        var fill = Brighten(SwiftColor.PremultipliedMix(tokens.PgXBg, accent, pick), lift);
        context.DrawRectangle(new SolidColorBrush(fill), null, round);
        if (pick < 1)
        {
            var ring = Color.FromArgb((byte)Math.Round(tokens.PgXRim.A * (1 - pick)), tokens.PgXRim.R, tokens.PgXRim.G, tokens.PgXRim.B);
            context.DrawRectangle(null, null, round, new BoxShadows(new BoxShadow { IsInset = true, Spread = 1, Color = ring }));
        }
        var ink = Brighten(SwiftColor.PremultipliedMix(tokens.PgLabHi, Colors.White, pick), lift);
        using var layout = Layout(new ImmutableSolidColorBrush(ink));
        layout.Draw(context, new Point(CapsulePadding, (CapsuleHeight - layout.Height) / 2));
    }

    private static Color Brighten(Color color, double factor) => Color.FromArgb(color.A,
        (byte)Math.Min(255, Math.Round(color.R * factor)), (byte)Math.Min(255, Math.Round(color.G * factor)), (byte)Math.Min(255, Math.Round(color.B * factor)));
}
