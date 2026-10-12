using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.5 and 10 (web surfaces W16a; ED-NEW-web-w16a-1): live answers draw in light and dark; every text colour keeps
// 4.5:1 on what it sits on (the thread, the card, a field, a code well, a menu, the chosen pill, a tone's tint); the accent
// marks only the current value and focus. The render tests read the drawn pixels of the approved canvas's live answer in
// both themes and save each card as a PNG next to the test binaries (nthui-snapshots) for review.
[Collection("Headless")]
public sealed class NthUiThemeTests(HeadlessSession headless)
{
    public static TheoryData<string> Themes => new(["Light", "Dark"]);

    private static ThemeVariant Theme(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    [MemberData(nameof(Themes))]
    public void Every_text_colour_keeps_four_and_a_half_to_one_on_everything_it_sits_on(string name) => headless.OnUiThread(() =>
    {
        var theme = Theme(name);
        var thread = name == "Dark" ? NthUiRig.DarkThread : NthUiRig.LightThread;
        var card = NthUiTokens.Over(NthUiTokens.CardFill.Resolve(theme), thread);
        var grounds = new Dictionary<string, Color>
        {
            ["thread"] = thread,
            ["card"] = card,
            ["field"] = NthUiTokens.Over(NthUiTokens.ControlFill.Resolve(theme), card),
            ["well"] = NthUiTokens.Over(NthUiTokens.Well.Resolve(theme), card),
            ["menu"] = NthUiTokens.Menu.Resolve(theme),
        };
        var problems = new List<string>();
        void Check(string what, Color text, Color ground)
        {
            var ratio = NthUiTokens.Contrast(NthUiTokens.Over(text, ground), ground);
            if (ratio < 4.5) problems.Add($"{what}: {ratio:0.00}");
        }
        foreach (var (token, color) in new[] { ("Text1", NthUiTokens.Text1), ("Text2", NthUiTokens.Text2), ("Text3", NthUiTokens.Text3) })
        {
            foreach (var (ground, back) in grounds) Check($"{token} on {ground}", color.Resolve(theme), back);
        }
        var chosen = NthUiTokens.Over(NthUiTokens.Chosen.Resolve(theme), grounds["field"]);
        Check("Text1 on the chosen pill", NthUiTokens.Text1.Resolve(theme), chosen);
        Check("OnStrong on Strong", NthUiTokens.OnStrong.Resolve(theme), NthUiTokens.Strong.Resolve(theme));
        foreach (var tone in new[] { "info", "success", "warning", "danger" })
        {
            var tint = NthUiTokens.Over(NthUiTokens.ToneFill(tone).Resolve(theme), card);
            Check($"{tone} on its tint", NthUiTokens.Tone(tone).Resolve(theme), tint);
            Check($"Text1 on the {tone} tint", NthUiTokens.Text1.Resolve(theme), tint);
        }
        Assert.Empty(problems);
    });

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    [MemberData(nameof(Themes))]
    public void The_live_answer_renders_its_card_text_and_accent_where_the_design_puts_them(string name) => headless.OnUiThread(() =>
    {
        var theme = Theme(name);
        var thread = name == "Dark" ? NthUiRig.DarkThread : NthUiRig.LightThread;
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English), theme: theme);
        rig.Settle();
        var pixels = rig.Capture();
        var origin = new Point(16, 16);
        Color At(Point p)
        {
            var (r, g, b) = pixels.At((int)Math.Round(p.X), (int)Math.Round(p.Y));
            return Color.FromRgb((byte)r, (byte)g, (byte)b);
        }
        static void Near(Color expected, Color actual, int tolerance, string what) =>
            Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
                $"{what}: expected {expected}, drew {actual}");

        Near(thread, At(new Point(4, 4)), 2, "thread");
        var cardSlot = rig.View.SlotFor(rig.Node("card").Key)!;
        var cardRect = rig.Layout(cardSlot).Translate(origin);
        var card = NthUiTokens.Over(NthUiTokens.CardFill.Resolve(theme), thread);
        Near(card, At(cardRect.TopLeft + new Point(8, 8)), 3, "card fill");
        Near(card, At(cardRect.BottomRight - new Point(8, 8)), 3, "card fill");

        var title = rig.View.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Hosting estimate");
        var titleRect = rig.Layout(title).Translate(origin);
        var best = 0.0;
        for (var y = (int)titleRect.Top; y < (int)titleRect.Bottom; y++)
        {
            for (var x = (int)titleRect.Left; x < (int)titleRect.Right; x++)
                best = Math.Max(best, NthUiTokens.Contrast(At(new Point(x, y)), card));
        }
        Assert.True(best >= 4.5, $"title contrast {best:0.00}");

        var slider = rig.ViewOf<NthUiSliderView>().Slider;
        var sliderRect = rig.Layout(slider).Translate(origin);
        Near(NthUiTokens.Accent.Resolve(theme), At(new Point(sliderRect.X + 12, sliderRect.Y + 12)), 14, "the slider's value fill");
        var knob = At(new Point(sliderRect.X + slider.ThumbX, sliderRect.Y + 12));
        Assert.True(NthUiTokens.Luminance(knob) > .8, $"knob {knob}");
        var track = At(new Point(sliderRect.Right - 12, sliderRect.Y + 12));
        Assert.True(Math.Abs(track.R - NthUiTokens.Accent.Resolve(theme).R) + Math.Abs(track.B - NthUiTokens.Accent.Resolve(theme).B) > 40, $"track {track} is not the accent");

        var total = rig.View.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "$134");
        Assert.Equal(FontWeight.SemiBold, total.FontWeight);
        Assert.Equal(TextAlignment.Right, total.TextAlignment);

        var png = rig.View.ExportPng(rig.Node("card").Key, 2);
        Assert.NotNull(png);
        using (var bitmap = new Bitmap(new MemoryStream(png!)))
        {
            Assert.Equal((int)Math.Ceiling(cardSlot.Bounds.Width * 2), bitmap.PixelSize.Width);
            var width = bitmap.PixelSize.Width;
            var exported = GenieBitmaps.Pixels(bitmap);
            Color Exported(double x, double y)
            {
                var (b, g, r, a) = GenieBitmaps.At(exported, width, (int)Math.Round(x * 2), (int)Math.Round(y * 2));
                Assert.Equal(255, a);
                return Color.FromRgb(r, g, b);
            }
            Near(thread, Exported(.5, .5), 3, "the export's backdrop outside the card's corner");
            Near(card, Exported(8, 8), 3, "the exported card fill");
            var offset = rig.Layout(cardSlot).Position;
            var cardTitle = rig.Layout(title).Translate(new Vector(-offset.X, -offset.Y));
            var exportedBest = 0.0;
            for (var y = cardTitle.Top; y < cardTitle.Bottom; y += .5)
            {
                for (var x = cardTitle.Left; x < cardTitle.Right; x += .5)
                    exportedBest = Math.Max(exportedBest, NthUiTokens.Contrast(Exported(x, y), card));
            }
            Assert.True(exportedBest >= 4.5, $"exported title contrast {exportedBest:0.00}");
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "nthui-snapshots");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"canvas-hosting-{name.ToLowerInvariant()}.png"), png!);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Switching_the_theme_redraws_the_same_view_without_rebuilding_it() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        rig.Settle();
        var stepper = rig.ViewOf<NthUiStepperView>().Stepper;
        var title0 = rig.View.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Hosting estimate");
        Assert.Equal(NthUiTokens.Text1.Resolve(ThemeVariant.Light), ((ISolidColorBrush)title0.Foreground!).Color);
        rig.Window.RequestedThemeVariant = ThemeVariant.Dark;
        rig.Pump();
        Assert.Same(stepper, rig.ViewOf<NthUiStepperView>().Stepper);
        var title = rig.View.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Hosting estimate");
        Assert.Equal(NthUiTokens.Text1.Resolve(ThemeVariant.Dark), ((ISolidColorBrush)title.Foreground!).Color);
    });
}
