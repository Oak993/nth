// PORT-ONLY CZ-3 (customization-SPEC CZ-3.3 to CZ-3.4, CZ-4.1, CZ-4.2, CZ-4.4; OWNER OD-0089 amendment, layout B "Stage"): the Customization page's pinned block. The heading
// "Composer light", then a card holding the stage (a small chat window, 496 x 212, radius 10: the window's own backdrop, the end of a conversation and the preview composer) and
// its foot (the preview model switch at the left, the caption of what the light is at the right). It is 372 high at rest: the page's 60 top inset, the heading's 18 and 10,
// the card's 12 + 212 + 46, and 14 below it; the 2 points to the Style section are the page's (GapBelow). It scrolls with the page for now: CZ-6 pins and compacts it.
// The settings it wears are read through the two functions it is given inside tracked reads, so they follow the app's settings live (CZ-13.1) whatever holds them.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
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

    // CZ-6 STUB (test-first red step): the compaction and the hairline, not yet driven
    internal double Compaction => 0;
    internal Border Hairline { get; } = new();

    /// <param name="settings">The composer light's settings as the app holds them; read inside a tracked read.</param>
    /// <param name="backdropOpacity">The app's backdrop opacity, the stage's own backdrop wearing it; read inside a tracked read.</param>
    /// <param name="session">The preview's maker and state (CZ-4.5.3, CZ-4.6).</param>
    internal CustomizationStage(Func<ComposerLightSettings> settings, Func<double> backdropOpacity, PreviewSession session)
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
        Preview = new ComposerPreview(session) { Settings = settings() };
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
        Child = new Border { Padding = new Thickness(0, 0, 0, BottomPadding), Child = new StackPanel { Orientation = Orientation.Vertical, Children = { HeadingBox, card } } };

        session.Changed += OnSession;
        DetachedFromVisualTree += (_, _) => session.Changed -= OnSession;
        AttachedToVisualTree += (_, _) => { session.Changed -= OnSession; session.Changed += OnSession; };
        this.Track(settings, value => { Preview.Settings = value; Caption.SetText(Describe()); });
        this.Track(backdropOpacity, opacity => backdrop.Update(opacity, null));
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
