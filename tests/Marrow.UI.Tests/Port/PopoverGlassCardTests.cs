using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-glass-g2 (OD-0107 and its amendment, OD-0005). The owner: "when any popup opens, anything in the composer, I see a
// black, dark background, not liquid glass". On the Mac every popover is an NSPopover, the system's Liquid Glass: the scene
// behind it seen blurred through it, a near-black hairline round it, a light rim inside that, round corners of about 20 pt,
// the tip part of the same glass (PopoverCard's file note has the measurements). The card the presenter hangs a popover on
// no longer paints ED-238's opaque kit panel (#1E1E1E / #FFFFFF): it paints a translucent tint, the hairline and the rim over
// the see-through glass its popup window shows (PopoverGlass, proven on a real window in PopoverGlassNativeTests), and the
// opaque panel only where no glass can be had. Headless can render the card but not a popup window's composition, so these
// prove the card's own drawing and the numbers; the glass itself is proven natively.
[Collection("Headless")]
public sealed class PopoverGlassCardTests(HeadlessSession headless)
{
    private static ThemeVariant Theme(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    // A card as the presenter dresses it (PopoverHost.Wrap): the tint, the hairline, the rim and the fallback panel.
    private static PopoverCard Dressed(ThemeVariant appearance, Control? child = null, PopoverTipEdge edge = PopoverTipEdge.Bottom)
    {
        var card = new PopoverCard
        {
            Child = child ?? new Border { Width = 200, Height = 100 },
            TipEdge = edge,
            Background = new SolidColorBrush(PopoverHost.Tint.Resolve(appearance)),
            BorderBrush = new SolidColorBrush(PopoverHost.Edge.Resolve(appearance)),
            RimBrush = new SolidColorBrush(PopoverHost.Rim.Resolve(appearance)),
            FallbackBrush = new SolidColorBrush(PopoverHost.Fill.Resolve(appearance)),
        };
        card.Measure(Size.Infinity);
        card.Arrange(new Rect(card.DesiredSize));
        return card;
    }

    private static SkiaSharp.SKBitmap Render(Visual visual, Size size)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
        bitmap.Render(visual);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return SkiaSharp.SKBitmap.Decode(stream);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-glass-g2")] // OD-0107, OD-0005
    [InlineData("Dark")]
    [InlineData("Light")]
    public void A_presented_popover_card_paints_a_translucent_tint_not_an_opaque_panel(string name) => headless.OnUiThread(() =>
    {
        // Through the real presenter: the card the popover hangs on, wearing the app's resources.
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var appearance = Theme(name);
        var application = Application.Current!;
        var before = application.RequestedThemeVariant;
        application.RequestedThemeVariant = appearance;
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(new ThemeVariantScope { RequestedThemeVariant = appearance, Child = anchor });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, ShouldUseOverlayLayer = true, Child = new Border { Width = 200, Height = 100 } };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            var card = Assert.IsType<PopoverCard>(popup.Child);
            popup.Child = null;
            using var root = HeadlessPopupHost.Open(popup, window, card);
            Dispatcher.UIThread.RunJobs();
            card.Material = PopoverMaterial.Glass; // a native popup window's (the overlay host here has no glass)
            card.Opacity = 1;
            card.RenderTransform = null;
            using var image = Render(card, card.Bounds.Size);
            var middle = image.GetPixel((int)(card.Bounds.Width / 2), 50);
            // Before: #1E1E1E / #FFFFFF at alpha 255. Now the tint alone: white at 0.10 (Dark) or 0.50 (Light).
            Assert.True(middle.Alpha <= 140, $"the card's middle is {middle}: an opaque panel, not glass");
            var tint = PopoverHost.Tint.Resolve(appearance);
            Assert.InRange(middle.Alpha, tint.A - 2, tint.A + 2);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
            application.RequestedThemeVariant = before;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g2")]
    public void The_tint_hairline_and_rim_are_the_measured_values()
    {
        // Fitted to the reference frames (PopoverCard's file note): the glass lighter than what is round it (Dark +20, Light
        // +25 over the blur, capped in Dark), a near-black hairline (Dark 0-28 between 35-60 and 60-90: about 0.8 black;
        // Light 146 between 194 and 215: about 0.28), a rim a point inside brighter than the glass (Dark +25 to +47 over 60-90,
        // Light +8 over 215: white at about 0.2).
        static (byte A, byte W) Of(Color c) => (c.A, c.R);
        Assert.Equal(((byte)26, (byte)255), Of(PopoverHost.Tint.Resolve(ThemeVariant.Dark)));    // white 0.10
        Assert.Equal(((byte)128, (byte)255), Of(PopoverHost.Tint.Resolve(ThemeVariant.Light)));  // white 0.50
        Assert.Equal(((byte)204, (byte)0), Of(PopoverHost.Edge.Resolve(ThemeVariant.Dark)));     // black 0.80
        Assert.Equal(((byte)71, (byte)0), Of(PopoverHost.Edge.Resolve(ThemeVariant.Light)));     // black 0.28
        Assert.Equal(((byte)51, (byte)255), Of(PopoverHost.Rim.Resolve(ThemeVariant.Dark)));     // white 0.20
        Assert.Equal(((byte)51, (byte)255), Of(PopoverHost.Rim.Resolve(ThemeVariant.Light)));
        Assert.Equal(20, PopoverCard.BodyRadius); // round corners of about 20 pt (20.5 fitted)
        Assert.Equal(0x56, PopoverGlass.DarkCap);
    }

    // WCAG relative luminance and contrast ratio.
    private static double Luminance(double v)
    {
        var c = v / 255;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Contrast(double a, double b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-glass-g2")] // requirement: text readable over any backdrop
    [InlineData(0.0)]
    [InlineData(58.0)]   // the Mac frame's backdrop under the effort popover
    [InlineData(128.0)]
    [InlineData(255.0)]
    public void The_glass_keeps_its_text_readable_over_any_backdrop(double backdrop)
    {
        // The app's own text colours, composited over the glass: the label (SwiftColor.LabelColor, white or black at 0.85) at
        // 4.5:1 or more, the secondary text (Chrome.SecondaryText, 0.7 of the label) at 3:1 or more (review of glass-g2: the first
        // version modelled the Dark label as opaque white, 4.6:1 where the real one was 3.91:1).
        static double Over(Color text, double glass) => text.R * (text.A / 255.0) + glass * (1 - text.A / 255.0);
        foreach (var appearance in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var t = PopoverHost.Tint.Resolve(appearance).A / 255.0;
            var under = appearance == ThemeVariant.Dark ? Math.Min(backdrop, PopoverGlass.DarkCap) : backdrop; // the cap is Dark's only
            var glass = under * (1 - t) + 255 * t;
            var label = Over(SwiftColor.LabelColor.Resolve(appearance), glass);
            var secondary = Over(Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(appearance), glass);
            Assert.True(Contrast(label, glass) >= 4.5, $"{appearance} over {backdrop}: glass {glass:F1}, label {label:F1}, {Contrast(label, glass):F2}:1");
            Assert.True(Contrast(secondary, glass) >= 3, $"{appearance} over {backdrop}: glass {glass:F1}, secondary {secondary:F1}, {Contrast(secondary, glass):F2}:1");
        }
    }

    [Theory]
    [Trait("Decision", "ED-NEW-glass-g2")]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Where_no_glass_can_be_had_the_card_paints_the_opaque_panel_under_its_tint(string name) => headless.OnUiThread(() =>
    {
        var appearance = Theme(name);
        var card = Dressed(appearance);
        card.Shown(new Border()); // a host that is no window of its own (the overlay's): no glass
        Assert.Equal(PopoverMaterial.Fallback, card.Material);
        Assert.Null(card.Glass);
        using var image = Render(card, card.Bounds.Size);
        var middle = image.GetPixel(100, 50);
        Assert.Equal(255, middle.Alpha); // legible over anything
        var fill = PopoverHost.Fill.Resolve(appearance);
        var tint = PopoverHost.Tint.Resolve(appearance);
        var expected = fill.R + (255 - fill.R) * tint.A / 255.0;
        Assert.InRange(middle.Red, expected - 3, expected + 3);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-glass-g2")]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void The_glass_has_round_corners_a_dark_hairline_and_a_light_rim_inside_it(string name) => headless.OnUiThread(() =>
    {
        var appearance = Theme(name);
        var card = Dressed(appearance);
        card.Material = PopoverMaterial.Fallback; // opaque, so each layer's colour reads plainly
        using var image = Render(card, card.Bounds.Size);
        // The corner: outside the 20 pt round corner (the curve crosses the diagonal 20 (1 - 1/sqrt 2) = 5.9 pt in) only the
        // soft shadow (at most 0.22 black), solid glass inside it.
        Assert.True(image.GetPixel(2, 2).Alpha <= 0.22 * 255 + 2, $"(2, 2): {image.GetPixel(2, 2)}");
        Assert.True(image.GetPixel(4, 4).Alpha <= 0.22 * 255 + 2, $"(4, 4): {image.GetPixel(4, 4)}");
        Assert.Equal(255, image.GetPixel(9, 9).Alpha);
        Assert.Equal(255, image.GetPixel(0, 50).Alpha); // the straight side runs to the bounds
        // Across the left side at mid height: the hairline (darker than the glass), then the rim (lighter), then the glass.
        var hairline = image.GetPixel(0, 50);
        var rim = image.GetPixel(1, 50);
        var glass = image.GetPixel(10, 50);
        Assert.True(hairline.Red < glass.Red - 15, $"hairline {hairline} against glass {glass}");
        Assert.True(rim.Red > glass.Red + (name == "Dark" ? 15 : 5) || (name == "Light" && rim.Red >= glass.Red), $"rim {rim} against glass {glass}");
        Assert.Equal(glass, image.GetPixel(4, 50)); // no shadow inside the glass
    });

    [Theory]
    [Trait("Decision", "ED-NEW-glass-g2")]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void The_shadow_lies_outside_the_glass_only(string name) => headless.OnUiThread(() =>
    {
        // Under a translucent card a shadow drawn under the outline would darken the glass from inside.
        var appearance = Theme(name);
        var card = Dressed(appearance);
        card.ShadowRoom = true;
        card.Material = PopoverMaterial.Glass;
        var host = new Border { Child = card };
        host.Measure(Size.Infinity);
        host.Arrange(new Rect(host.DesiredSize));
        using var image = Render(host, host.Bounds.Size);
        const int margin = 11;
        var tint = PopoverHost.Tint.Resolve(appearance);
        for (var d = 3; d <= 12; d++)
        {
            var inside = image.GetPixel(margin + d, margin + 50);
            Assert.True(Math.Abs(inside.Alpha - tint.A) <= 2 && inside.Red >= 250, $"{d} pt inside the left side: {inside} (the tint is {tint})");
        }
        Assert.True(image.GetPixel(margin - 1, margin + 50).Alpha >= 40, "the shadow outside the outline is gone");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g2")]
    public void The_content_is_clipped_to_the_round_corners() => headless.OnUiThread(() =>
    {
        var card = Dressed(ThemeVariant.Dark, new Border { Width = 200, Height = 100, Background = Brushes.Red });
        card.Material = PopoverMaterial.Glass;
        using var image = Render(card, card.Bounds.Size);
        var corner = image.GetPixel(3, 3);
        Assert.True(corner.Red < 40 || corner.Alpha < 40, $"the content shows past the round corner: {corner}");
        Assert.True(image.GetPixel(100, 50).Red > 200, "the content itself is drawn");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g2")] // OD-0095's tip, now beside a round corner
    public void The_tip_stays_on_the_straight_part_of_the_edge_past_the_round_corner()
    {
        var card = new Rect(100, 100, 300, 120);
        var nearCorner = PopoverCard.Aim(card, new Rect(95, 230, 10, 10), PopoverTipEdge.Bottom).Offset;
        Assert.Equal(PopoverCard.BodyRadius + PopoverCard.TipHalfWidth + PopoverCard.TipShoulderRadius, nearCorner, 3);
        var farCorner = PopoverCard.Aim(card, new Rect(395, 230, 10, 10), PopoverTipEdge.Bottom).Offset;
        Assert.Equal(300 - (PopoverCard.BodyRadius + PopoverCard.TipHalfWidth + PopoverCard.TipShoulderRadius), farCorner, 3);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-glass-g2")]
    public void The_window_glass_follows_the_cards_outline_its_grow_in_and_its_fade() => headless.OnUiThread(() =>
    {
        // Where the window's glass goes is read off the card as it is drawn: its place, the grow-in's scale about the tip and
        // the fade, in the window's pixels.
        var card = Dressed(ThemeVariant.Dark);
        var window = new Window { Width = 400, Height = 300, Content = new Canvas { Children = { card } } };
        Canvas.SetLeft(card, 50);
        Canvas.SetTop(card, 40);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var full = card.GlassShape(window)!.Value;
            Assert.Equal(new Rect(50, 40, 202, 102), full.Body);
            Assert.Equal(PopoverCard.BodyRadius, full.Radius, 3);
            Assert.Equal(new Point(50 + 101, 40 + 102), full.TipCentre);
            Assert.Equal(PopoverCard.TipHalfWidth * Math.Sqrt(2), full.TipSide, 3);
            Assert.Equal(PopoverCard.TipApexRadius, full.TipRadius, 3);
            Assert.Equal(1, full.Opacity, 3);
            // Half grown from the tip and a third faded in.
            card.RenderTransformOrigin = card.TipOrigin;
            card.RenderTransform = new ScaleTransform(0.5, 0.5);
            card.Opacity = 1.0 / 3;
            Dispatcher.UIThread.RunJobs();
            var half = card.GlassShape(window)!.Value;
            var tip = new Point(50 + card.TipPoint.X, 40 + card.TipPoint.Y);
            Assert.Equal(101, half.Body.Width, 2);
            Assert.Equal(51, half.Body.Height, 2);
            Assert.Equal(tip.X - 0.5 * card.TipPoint.X, half.Body.X, 2);
            Assert.Equal(tip.Y - 0.5 * card.TipPoint.Y, half.Body.Y, 2);
            Assert.Equal(PopoverCard.BodyRadius / 2, half.Radius, 3);
            Assert.Equal(PopoverCard.TipHalfWidth * Math.Sqrt(2) / 2, half.TipSide, 3);
            Assert.Equal(1.0 / 3, half.Opacity, 3);
        }
        finally
        {
            window.Close();
        }
    });
}
