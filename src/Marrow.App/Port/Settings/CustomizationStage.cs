// PORT-ONLY CZ-3 (customization-SPEC CZ-3.3 to CZ-3.4, CZ-4.1, CZ-4.2, CZ-4.4; OWNER OD-0089 amendment, layout B "Stage"): the Customization page's pinned block. The heading
// "Composer light", then a card holding the stage (a small chat window, 496 x 212, radius 10: the window's own backdrop, the end of a conversation and the preview composer) and
// its foot (the preview model switch at the left, the caption of what the light is at the right). It is 372 high at rest: the page's 60 top inset, the heading's 18 and 10,
// the card's 12 + 212 + 46, and 14 below it; the 2 points to the Style section are the page's (GapBelow). CZ-6 makes it sticky (StickyStageController): the page moves it with PinTo (a transform, no layout),
// folds the stage with Compact (the stage 212 down to 120, the conversation text fading and lifting; the slot the block asks of the page stays 372, so no section moves) and fades its Hairline.
// The settings it wears are read through the two functions it is given inside tracked reads, so they follow the app's settings live (CZ-13.1) whatever holds them.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.Core.Port.Voice;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CustomizationStage : Decorator
{
    /// <summary>CZ-3.4: 60 + 18 + 10 + 12 + 212 + 46 + 14.</summary>
    internal const double RestHeight = 372, TopInset = 60, HeadingHeight = 18, HeadingGap = 10, CardPadding = 12, StageWidth = 496, StageHeight = 212, FootMinHeight = 46, BottomPadding = 14;

    /// <summary>CZ-3.4: the gap between this block and the Style section, which the page keeps.</summary>
    internal const double GapBelow = 2;

    /// <summary>CZ-4.1: the stage's corner.</summary>
    internal const double StageRadius = 10;

    /// <summary>CZ-4.2: the conversation text's inset from the stage's left and right, and from its top.</summary>
    internal const double ChatInset = 24, ChatTop = 18;

    private readonly PreviewSession session;
    private readonly WindowBackdrop backdrop;
    private readonly Func<ComposerLightSettings> settings;

    internal TextBlock Heading { get; }
    internal Border HeadingBox { get; }
    internal Border Surface { get; }
    internal ComposerPreview Preview { get; }
    internal Control Chat { get; }
    internal TextBlock ChatHeading { get; }
    internal TextBlock ChatBody { get; }
    internal Grid Foot { get; }
    internal PreviewModelSwitch Switch { get; }
    internal CzCrossfadeText Caption { get; }

    /// <summary>CZ-3.5.2: how far the stage has folded (0 to 92).</summary>
    internal double Compaction { get; private set; }

    /// <summary>CZ-3.5.5: the 1 point line along the block's bottom edge, in the row divider's colour; it fades in and out with the page's scroll.</summary>
    internal Border Hairline { get; }

    private readonly CzNumber hairlineOpacity;
    private readonly TranslateTransform pin = new(), lift = new();

    /// <param name="settings">The composer light's settings as the app holds them; read inside a tracked read.</param>
    /// <param name="backdropOpacity">The app's backdrop opacity, the stage's own backdrop wearing it; read inside a tracked read.</param>
    /// <param name="session">The preview's maker and state (CZ-4.5.3, CZ-4.6).</param>
    /// <param name="workingDots">CZ-5: the working dots' setting as the app holds it, read inside a tracked read; null leaves the dots in the accent.</param>
    internal CustomizationStage(Func<ComposerLightSettings> settings, Func<double> backdropOpacity, PreviewSession session, Func<WorkingDotsSettings>? workingDots = null,
        Func<VoiceOrbSettings>? voiceOrb = null, Func<VoiceTypingSettings>? voiceTyping = null)
    {
        this.settings = settings;
        this.session = session;

        // the heading, in ChromeSection's heading style (a semibold body in the secondary colour), in its 18 high line
        Heading = new TextBlock { Text = "Composer light", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(Heading);
        Heading.Foreground = Chrome.SecondaryText.Brush(Heading);
        HeadingBox = new Border { Height = HeadingHeight, Margin = new Thickness(0, TopInset, 0, HeadingGap), Child = Heading };

        // the stage: the window's backdrop, the conversation, the composer, and an inset hairline in the divider colour
        backdrop = new WindowBackdrop(backdropOpacity()) { Margin = new Thickness(-1) }; // its own hairline falls outside the clip; the stage draws the one CZ-4.1 asks for
        (Chat, ChatHeading, ChatBody) = MakeChat();
        // the composer keeps its 12 above the stage's bottom edge whatever the stage's height (CZ-3.5.2): it is as tall as the stage at rest and sits at the bottom, the stage clipping what the compaction takes off its top
        Preview = new ComposerPreview(session) { Settings = settings(), Height = StageHeight, VerticalAlignment = VerticalAlignment.Bottom };
        Chat.RenderTransform = lift;
        var hairline = new Border { IsHitTestVisible = false, BorderThickness = new Thickness(1) }.RoundedRectangle(StageRadius, RoundedCornerStyle.Circular);
        var divider = new AnimatedColor(hairline);
        divider.Set(Chrome.Overlay(0.12), null);
        hairline.BorderBrush = divider.Brush;
        Surface = new Border { Width = StageWidth, Height = StageHeight, ClipToBounds = true, Margin = new Thickness(CardPadding, CardPadding, CardPadding, 0), Child = new Grid { Children = { backdrop, Chat, Preview, hairline } } }
            .RoundedRectangle(StageRadius, RoundedCornerStyle.Circular);
        Avalonia.Automation.AutomationProperties.SetName(Surface, "Preview of the composer light");

        // the foot: the model switch at the left, the caption at the right
        Switch = new PreviewModelSwitch(session) { VerticalAlignment = VerticalAlignment.Center };
        Caption = new CzCrossfadeText(Describe()) { FontSize = 12, Secondary = true, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Foot = new Grid { MinHeight = FootMinHeight, Margin = new Thickness(CardPadding, 0, CardPadding, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(Caption, 1);
        Foot.Children.Add(Switch);
        Foot.Children.Add(Caption);

        var card = new ChromeCard(() => new StackPanel { Orientation = Orientation.Vertical, Children = { Surface, Foot } });
        var content = new Border { Padding = new Thickness(0, 0, 0, BottomPadding), Child = new StackPanel { Orientation = Orientation.Vertical, Children = { HeadingBox, card } } };

        // CZ-3.5.5: the hairline on the block's bottom edge, which is the bottom of what is laid out (the content is 372 less the compaction high), in ChromeRowDivider's colour (Overlay 0.12, following the appearance)
        Hairline = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Opacity = 0 };
        var ink = new AnimatedColor(Hairline);
        ink.Set(Chrome.Overlay(0.12), null);
        Hairline.Background = ink.Brush;
        hairlineOpacity = new CzNumber(new CzMotion(this), 0, value => Hairline.Opacity = value);
        Child = new Grid { Children = { content, Hairline } };
        RenderTransform = pin;

        session.Changed += OnSession;
        DetachedFromVisualTree += (_, _) => session.Changed -= OnSession;
        AttachedToVisualTree += (_, _) => { session.Changed -= OnSession; session.Changed += OnSession; };
        this.Track(settings, value => { Preview.Settings = value; Caption.SetText(Describe()); });
        this.Track(backdropOpacity, opacity => backdrop.Update(opacity, null));
        if (workingDots is not null) this.Track(workingDots, value => Preview.WorkingDots = value);
        if (voiceOrb is not null)
        {
            Preview.EnableVoice(voiceOrb, voiceTyping ?? (() => VoiceTypingSettings.Default));
            FadeCrossedLines();
        }
    }

    // CZ-9.7: only text crossed by the floating pill fades. Compaction still owns Chat's overall opacity/translation.
    private void FadeCrossedLines()
    {
        var heading = new CrossedLines(ChatHeading, Surface);
        var body = new CrossedLines(ChatBody, Surface);
        void Place() { heading.Place(Preview.VoicePill!); body.Place(Preview.VoicePill!); }
        Preview.VoicePill!.FloatTargetChanged += Place;
        Preview.Pill.LayoutUpdated += (_, _) => Place();
        Chat.LayoutUpdated += (_, _) => Place();
    }

    // Mask the individual shaped lines, keeping the paragraph's original inlines, glyphs and line breaking.
    // The chat's outer opacity remains the compaction opacity; a crossed line never hides its neighbours.
    private sealed class CrossedLines
    {
        private readonly TextBlock text;
        private readonly Control surface;
        private readonly LookMotion motion;
        private readonly LinearGradientBrush mask = new() { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        private readonly List<LookNumber> levels = [];
        private readonly List<(double Top, double Height)> bands = [];

        public CrossedLines(TextBlock text, Control surface)
        {
            this.text = text;
            this.surface = surface;
            motion = new LookMotion(text);
            motion.Frame += Apply;
            text.OpacityMask = mask;
        }

        public void Place(Voice.VoicePillSlot pill)
        {
            var lines = text.TextLayout.TextLines;
            if (levels.Count != lines.Count)
            {
                levels.Clear(); bands.Clear(); mask.GradientStops.Clear();
                for (var i = 0; i < lines.Count; i++)
                {
                    levels.Add(new LookNumber(motion, 1)); bands.Add(default);
                    mask.GradientStops.Add(new GradientStop(Colors.White, 0));
                    mask.GradientStops.Add(new GradientStop(Colors.White, 1));
                }
            }
            Rect? crossed = pill.FloatTarget is { } floating && pill.TranslatePoint(floating.TopLeft, surface) is { } p ? new Rect(p, floating.Size) : null;
            var origin = text.TranslatePoint(default, surface);
            double y = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                bands[i] = (y, line.Height);
                var hit = crossed is { } rect && origin is { } at && rect.Intersects(new Rect(at.X + line.Start, at.Y + y, line.Width, line.Height));
                levels[i].To(hit ? 0 : 1, LookCurve.Ease(hit ? .16 : .25), hit ? 0 : .05);
                y += line.Height;
            }
            Apply();
        }

        private void Apply()
        {
            var height = Math.Max(1, text.Bounds.Height);
            for (var i = 0; i < levels.Count; i++)
            {
                var color = Color.FromArgb((byte)Math.Round(levels[i].Value * 255), 255, 255, 255);
                mask.GradientStops[2 * i].Offset = bands[i].Top / height;
                mask.GradientStops[2 * i + 1].Offset = (bands[i].Top + bands[i].Height) / height;
                mask.GradientStops[2 * i].Color = mask.GradientStops[2 * i + 1].Color = color;
            }
        }
    }

    /// <summary>CZ-3.5.1: puts the block <paramref name="y"/> below where it lies at rest: a transform only, nothing is laid out again.</summary>
    internal void PinTo(double y)
    {
        if (pin.Y != y) pin.Y = y;
    }

    /// <summary>CZ-3.5.2: folds the stage by <paramref name="compaction"/> (0 to 92): the stage is 212 less that high, the conversation text above the composer fades with `1 - 1.8 k` and lifts `14 k` (k = compaction / 92), and
    /// the composer stays 12 above the stage's bottom. The slot the block asks of the page is its rest height, whatever the compaction (so the sections below never move faster than the scroll); only the stage's own layout changes.</summary>
    internal void Compact(double compaction)
    {
        if (compaction == Compaction) return;
        Compaction = compaction;
        Surface.Height = StickyStage.StageHeight(StageHeight, compaction);
        Chat.Opacity = StickyStage.ChatOpacity(compaction);
        lift.Y = StickyStage.ChatOffset(compaction);
        InvalidateArrange(); // the child is arranged at the rest height less the compaction (below)
    }

    /// <summary>CZ-3.5.5: the hairline fades in (shown) or out over 0.25 s ease; at once under reduce motion.</summary>
    internal void ShowHairline(bool shown) => hairlineOpacity.To(shown ? 1 : 0, CzMotion.Ease(StickyStage.HairlineSeconds));

    // the slot asks for its rest height: what the content needs plus what the compaction has taken off it (so it is 372 at every compaction and the page does not lay anything else out)
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return new Size(size.Width, size.Height + Compaction);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(new Size(finalSize.Width, Math.Max(0, finalSize.Height - Compaction)));
        return finalSize;
    }

    private string Describe() => PreviewCaption.Text(settings(), PreviewMakers.For(session.Maker).Name);

    private void OnSession() => Caption.SetText(Describe());

    // CZ-4.2: a heading (14 / 20, semibold) and a paragraph (13 / 19) of the window's text colour, 24 in on both sides and 18 down
    private static (Control Chat, TextBlock Heading, TextBlock Body) MakeChat()
    {
        var heading = new TextBlock { Text = "Dark mode toggle", LineHeight = 20, Margin = new Thickness(0, 0, 0, 4) };
        SwiftFont.System(14, SwiftFont.Weight.Semibold).Apply(heading);
        heading.Foreground = Chrome.PrimaryText.Brush(heading);
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        SwiftFont.System(13, SwiftFont.Weight.Regular).Apply(body);
        body.Foreground = Chrome.PrimaryText.Brush(body);
        var code = SwiftFont.System(12, SwiftFont.Weight.Regular, SwiftFont.Design.Monospaced).Resolve();
        body.Inlines!.Add(new Run("I added a "));
        body.Inlines.Add(new Run("Theme") { FontWeight = FontWeight.Bold });
        body.Inlines.Add(new Run(" switch to the settings page. It reuses the existing "));
        body.Inlines.Add(new Run("Switch") { FontFamily = code.FontFamily, FontSize = code.FontSize });
        body.Inlines.Add(new Run(" row, so it picks up the same spacing and focus ring as the other options."));
        var chat = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(ChatInset, ChatTop, ChatInset, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Children = { heading, body } };
        return (chat, heading, body);
    }
}
