using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

// PORT-ONLY: web surfaces W5, a line of text whose words change often and whose size never does (E:/Droppy/work/design/web-surfaces/
// DESIGN.md 3.10 "Live values tick at most 4 times a second and never re-lay out the card (fixed-width numerals)", 12 "Background
// tasks ticking: 4 Hz, no layout pass"; ED-NEW-web-w5-1). No Swift counterpart. The control measures once, from a reserve string
// ("99h 59m") or from the width it is offered, and draws whatever Text says inside that width with tabular digits; setting Text
// only invalidates its drawing, so a tick re-measures and re-arranges nothing.

namespace Marrow.App.Views.Panes;

/// <summary>Text that changes without changing its layout.</summary>
public sealed class LiveText : Control
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<LiveText, string>(nameof(Text), "");

    /// <summary>The widest text the control will show; it measures to this width. Empty means fill the width offered.</summary>
    public static readonly StyledProperty<string> ReserveProperty = AvaloniaProperty.Register<LiveText, string>(nameof(Reserve), "");

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<LiveText>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<LiveText>();
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<LiveText>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<LiveText>();

    private static readonly FontFeatureCollection Tabular = FontFeatureCollection.Parse("tnum");

    private double reserved;
    private double lineHeight;
    private int draws;

    static LiveText()
    {
        AffectsRender<LiveText>(TextProperty, ForegroundProperty);
        AffectsMeasure<LiveText>(ReserveProperty, FontSizeProperty, FontFamilyProperty, FontWeightProperty);
    }

    public LiveText()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? "");
    }

    public string Reserve
    {
        get => GetValue(ReserveProperty);
        set => SetValue(ReserveProperty, value ?? "");
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>How many times the text has been drawn (tests: a tick draws, never measures).</summary>
    public int Draws => draws;

    private Typeface Face => new(FontFamily, FontStyle.Normal, FontWeight);

    private TextLayout Layout(string text, double maxWidth, IBrush? brush) =>
        new(text, Face, FontSize, brush, textAlignment: TextAlignment.Left, textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis, maxWidth: maxWidth, maxHeight: double.PositiveInfinity, fontFeatures: Tabular);

    protected override Size MeasureOverride(Size availableSize)
    {
        using (var probe = Layout("Ag", double.PositiveInfinity, null)) lineHeight = Math.Ceiling(probe.Height);
        if (Reserve.Length > 0)
        {
            using var reserve = Layout(Reserve, double.PositiveInfinity, null);
            reserved = Math.Ceiling(reserve.Width);
            return new Size(reserved, lineHeight);
        }
        reserved = 0;
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, lineHeight);
    }

    public override void Render(DrawingContext context)
    {
        draws++;
        if (Text.Length == 0 || Foreground is not { } brush || Bounds.Width <= 0) return;
        using var layout = Layout(Text, Bounds.Width, brush);
        layout.Draw(context, new Point(0, Math.Max(0, (Bounds.Height - layout.Height) / 2)));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LiveTextPeer(this);

    private sealed class LiveTextPeer(LiveText owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string? GetNameCore() => owner.Text;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
