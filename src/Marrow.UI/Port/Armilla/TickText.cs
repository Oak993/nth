// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 3.3 "Live values update at most 4 times a second and never re-lay out a card, because
// numerals have fixed widths"; 2.5 "A working card's shimmer counts as one and runs on the same clock"; the canvas's .shim, sheen()
// and budget(), work/design/assistant/index.html lines 248-251, 1765-1790, 3062-3095; ED-NEW-dextra-as8-5). No Swift twin.
// One line of live text drawn by the control itself. Its size never depends on its text: it measures to one line at its font and
// takes the width its slot gives it, so a new value only redraws it and never asks for a layout pass. Digits are tabular. Text
// that does not fit ends in an ellipsis. Its shimmer, when on, is a band drawn on the render thread over the text's own extent on
// Armilla's shared clock (3.6 s, in step with every other shimmer and mark), so the UI thread does no per-frame work; with Reduce
// motion, when its list's budget calms it, off screen or in a sleeping window it does not move.
using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Marrow.Core.Port;
using Avalonia.Animation.Easings;
using SkiaSharp;

namespace Marrow.UI.Port.Armilla;

public sealed class TickText : Control
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<TickText, string>(nameof(Text), "");
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<TickText, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<TickText, double>(nameof(FontSize), 12);
    public static readonly StyledProperty<FontWeight> FontWeightProperty = AvaloniaProperty.Register<TickText, FontWeight>(nameof(FontWeight), FontWeight.Normal);
    public static readonly StyledProperty<bool> SheenProperty = AvaloniaProperty.Register<TickText, bool>(nameof(Sheen));
    public static readonly StyledProperty<bool> CalmProperty = AvaloniaProperty.Register<TickText, bool>(nameof(Calm));

    /// <summary>One shimmer pass (the canvas's SHEEN).</summary>
    public const double SheenPeriod = 3.6;

    static TickText()
    {
        AffectsRender<TickText>(TextProperty, InkProperty);
        AffectsMeasure<TickText>(FontSizeProperty, FontWeightProperty);
    }

    private SheenHandler? handler;
    private CompositionCustomVisual? visual;
    private ArmillaWindowWatch? watch;
    private DextraViewWatch? availability;
    private TextLayout? layout;
    private (string Text, double Width, IBrush? Ink, double Size, FontWeight Weight, FlowDirection Flow) layoutKey;
    private (double X, double Width, double Height, bool On, bool Dark)? sentBand;
    private bool? sentSleep;

    public TickText() => ActualThemeVariantChanged += (_, _) => { layout?.Dispose(); layout = null; InvalidateVisual(); SendBand(); };

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value ?? ""); }

    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }

    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    /// <summary>Whether the line shimmers while it may move (a working card's activity line).</summary>
    public bool Sheen { get => GetValue(SheenProperty); set => SetValue(SheenProperty, value); }

    /// <summary>Set by the motion budget when the line's list goes still.</summary>
    public bool Calm { get => GetValue(CalmProperty); set => SetValue(CalmProperty, value); }

    /// <summary>Whether the shimmer moves now, before the window's sleep and visibility (what the motion budget counts).</summary>
    public bool Moves => Sheen && !Calm && !Reduce;

    /// <summary>The width the drawn text covers (tests, and the shimmer's extent).</summary>
    public double InkWidth { get; private set; }

    /// <summary>Frames the shimmer drew (tests).</summary>
    internal int SheenFrames => handler?.Frames ?? 0;

    /// <summary>Whether the shimmer still asks for frames (tests).</summary>
    internal bool SheenTicking => handler?.Ticking ?? false;

    /// <summary>The digits stay tabular; the typeface is the app's own system face.</summary>
    private static readonly FontFeatureCollection Tabular = [new FontFeature { Tag = "tnum", Value = 1 }];

    private bool Reduce => EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    private Typeface Face => new(SwiftFont.System(FontSize).Resolve().FontFamily, FontStyle.Normal, FontWeight);

    protected override bool BypassFlowDirectionPolicies => true;

    protected override Size MeasureOverride(Size availableSize)
    {
        using var probe = new TextLayout("0", Face, FontSize, null, fontFeatures: Tabular);
        return new Size(0, Math.Ceiling(probe.Height));
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var key = (Text, width, Ink, FontSize, FontWeight, FlowDirection);
        if (layout is null || key != layoutKey)
        {
            layout?.Dispose();
            layoutKey = key;
            layout = new TextLayout(Text, Face, FontSize, Ink ?? Brushes.Black, FlowDirection == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left,
                TextWrapping.NoWrap, TextTrimming.CharacterEllipsis, null, FlowDirection, Math.Max(0, width), fontFeatures: Tabular);
            InkWidth = Math.Min(width, layout.WidthIncludingTrailingWhitespace);
        }
        layout.Draw(context, new Point(0, Math.Max(0, (Bounds.Height - layout.Height) / 2)));
        SendBand();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SheenProperty || change.Property == CalmProperty || change.Property == EnvironmentValues.AccessibilityReduceMotionProperty
            || change.Property == BoundsProperty || change.Property == IsVisibleProperty)
        {
            if (Moves && visual is null && VisualRoot is not null) Install();
            SendBand();
        }
        if (change.Property == TextProperty && ControlAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(Avalonia.Automation.AutomationElementIdentifiers.NameProperty, change.OldValue as string, change.NewValue as string);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Moves) Install();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (availability is not null) { availability.Changed -= OnWatch; availability.Dispose(); availability = null; }
        if (watch is not null) { watch.Changed -= OnWatch; watch.Release(); }
        watch = null;
        visual?.SendHandlerMessage(SheenHandler.Stop.Instance);
        ElementComposition.SetElementChildVisual(this, null);
        visual = null; handler = null; sentBand = null; sentSleep = null;
        layout?.Dispose();
        layout = null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TickPeer(this);

    private void Install()
    {
        if (visual is not null || ElementComposition.GetElementVisual(this) is not { } host) return;
        availability = new DextraViewWatch(this);
        availability.Changed += OnWatch;
        watch = TopLevel.GetTopLevel(this) is { } top ? ArmillaWindowWatch.Acquire(top) : null;
        if (watch is not null) watch.Changed += OnWatch;
        handler = new SheenHandler(ArmillaHub.For(host.Compositor), AmbientRefreshPacer.For(host.Compositor), SwiftClock.Provider.Current);
        visual = host.Compositor.CreateCustomVisual(handler);
        ElementComposition.SetElementChildVisual(this, visual);
        sentBand = null; sentSleep = null;
        SendBand();
        OnWatch();
    }

    private void OnWatch()
    {
        var sleep = (watch?.Sleeping ?? false) || availability?.Available == false;
        if (visual is null || sentSleep == sleep) return;
        sentSleep = sleep;
        visual.SendHandlerMessage(sleep ? SheenHandler.Sleep.On : SheenHandler.Sleep.Off);
    }

    private void SendBand()
    {
        if (visual is null) return;
        var flip = FlowDirection == FlowDirection.RightToLeft;
        var band = (X: flip ? Bounds.Width - InkWidth : 0, Width: InkWidth, Height: Bounds.Height, On: Moves && IsVisible && InkWidth > 0,
            Dark: ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
        if (band == sentBand) return;
        sentBand = band;
        visual.Size = new Vector(Bounds.Width, Bounds.Height);
        visual.SendHandlerMessage(new SheenHandler.Band(band.X, band.Width, band.Height, band.On, band.Dark));
    }

    private sealed class TickPeer(TickText owner) : ControlAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.Text;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}

/// <summary>The render-thread side of a shimmer: a soft band over the text's extent, on the shared clock.</summary>
internal sealed class SheenHandler(ArmillaHub hub, AmbientRefreshPacer? pacer, TimeProvider clock) : CompositionCustomVisualHandler
{
    internal sealed record Band(double X, double Width, double Height, bool On, bool Dark);

    internal sealed class Sleep
    {
        internal static readonly Sleep On = new(), Off = new();
    }

    internal sealed class Stop
    {
        internal static readonly Stop Instance = new();
    }

    private static readonly SplineEasing Ease = new(.45, 0, .55, 1);
    private Band? band;
    private bool sleeping, stopped, drawn;
    private int requested;
    private int frames;
    private double time;

    internal int Frames => Volatile.Read(ref frames);

    internal bool Ticking => Volatile.Read(ref requested) == 1;

    private bool Live => band is { On: true } && !sleeping && !stopped;

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case Band next:
                band = next;
                Invalidate();
                Request();
                break;
            case Sleep sleep:
                sleeping = sleep == Sleep.On;
                Request();
                break;
            case Stop:
                stopped = true;
                break;
        }
    }

    public override Rect GetRenderBounds() => band is { } b ? new Rect(b.X, 0, b.Width, b.Height) : default;

    public override void OnAnimationFrameUpdate()
    {
        Volatile.Write(ref requested, 0);
        if (!Live) return;
        var now = clock.GetTimestamp() / (double)clock.TimestampFrequency;
        if (pacer is not null && !pacer.Beat(CompositionNow.Ticks, now)) { Register(); return; }
        time = hub.Time(CompositionNow.Ticks, now);
        Invalidate();
        if (drawn) Request();
    }

    private void Request()
    {
        if (!Live || Volatile.Read(ref requested) == 1) return;
        drawn = false;
        Register();
    }

    private void Register()
    {
        Volatile.Write(ref requested, 1);
        RegisterForNextAnimationFrameUpdate();
    }

    /// <summary>Where the band's left edge sits, as a share of the extent from -1 to 1, at shared time <paramref name="t"/>.</summary>
    internal static double Offset(double t)
    {
        var phase = (t % TickText.SheenPeriod + TickText.SheenPeriod) % TickText.SheenPeriod / TickText.SheenPeriod;
        return -1 + 2 * Ease.Ease(phase);
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        drawn = true;
        if (!Live || band is not { } b || b.Width <= 0) return;
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        var sk = lease.SkCanvas;
        var saved = sk.Save();
        try
        {
            var w = (float)b.Width;
            var h = (float)b.Height;
            sk.ClipRect(new SKRect((float)b.X, 0, (float)b.X + w, h));
            var left = (float)(b.X + Offset(time) * b.Width);
            // the canvas's linear-gradient(100deg, transparent 30%, var(--ghi) 50%, transparent 70%) in overlay
            var hi = b.Dark ? new SKColor(255, 255, 255, 102) : new SKColor(255, 255, 255, 250);
            var tilt = (float)(Math.Tan(10 * Math.PI / 180) * h / 2);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(left - tilt, h), new SKPoint(left + w + tilt, 0),
                [SKColors.Transparent, SKColors.Transparent, hi, SKColors.Transparent, SKColors.Transparent], [0, .3f, .5f, .7f, 1], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Overlay, IsAntialias = true };
            var alpha = Math.Clamp(lease.CurrentOpacity, 0, 1);
            if (alpha < 1) paint.Color = new SKColor(255, 255, 255, (byte)Math.Round(alpha * 255));
            sk.DrawRect(new SKRect(left, 0, left + w, h), paint);
        }
        finally { sk.RestoreToCount(saved); }
        Interlocked.Increment(ref frames);
        if (Live) Request();
    }
}
