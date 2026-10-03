using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// OD-0090, owner correction 2026-10-03 ("ensure the lightning strikes contrast with each different model's colour palette
// and bubbles too"), as the locked page settles it after the owner's review: the ink amount (jewel-knob.html inkOf: white's
// contrast against the liquid's light stop, (2.9 - ratio) / 1, clamped) crossfades with a model change, and drives the
// bubbles' pale-liquid look (drawBub: FastC .bub.dark blended in by the ink). The bolt's own rim was rejected by the owner and
// is not drawn. Expected inks: inkOf(liquidOf(palettes, maker, 'dark')) run with node (tests/fixtures/jewel-knob/expected.mjs).
[Collection("Headless")]
public sealed class EffortSliderJewelInkTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Decision", "ED-252")] // jewel-knob.html inkOf(q).ink for each app maker
    [InlineData("Claude", EffortBrand.Claude)]
    [InlineData("OpenAI", EffortBrand.Silver)]
    [InlineData("Gemini", EffortBrand.Antigravity)]
    [InlineData("DeepSeek", EffortBrand.Deepseek)]
    [InlineData("Meta", EffortBrand.Meta)]
    [InlineData("Grok", EffortBrand.Grok)]
    [InlineData("Cursor", EffortBrand.Cursor)]
    [InlineData("OpenCode", EffortBrand.Opencode)]
    [InlineData("OpenCodeGo", EffortBrand.OpencodeGo)]
    [InlineData("Zai", EffortBrand.Zai)]
    [InlineData("Devin", EffortBrand.Devin)]
    [InlineData("Copilot", EffortBrand.Copilot)]
    [InlineData("CommandCode", EffortBrand.Commandcode)]
    [InlineData("Pi", EffortBrand.Pi)]
    public void Each_makers_ink_is_the_pages(string maker, EffortBrand brand)
    {
        var expected = JewelFixtures.Expected.GetProperty("ink").GetProperty(maker).GetProperty("ink").GetDouble();
        Assert.Equal(expected, StrikeTint.For(brand).Ink, 12);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // inkOf: OpenAI's silver (white on it 1.2:1) needs it all, Claude's terracotta none
    public void Silver_needs_full_ink_and_terracotta_none()
    {
        Assert.Equal(1, StrikeTint.For(EffortBrand.Silver).Ink);
        Assert.Equal(1, StrikeTint.For(EffortBrand.Devin).Ink);
        Assert.Equal(0, StrikeTint.For(EffortBrand.Claude).Ink);
        Assert.InRange(StrikeTint.For(EffortBrand.Opencode).Ink, 0.88, 0.89);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // mixTint2: the ink crossfades linearly with the maker change
    public void The_ink_crossfades_with_a_model_change()
    {
        foreach (var step in JewelFixtures.Expected.GetProperty("inkMix").EnumerateArray())
        {
            var u = step.GetProperty("u").GetDouble();
            var mixed = StrikeTint.Mix(StrikeTint.For(EffortBrand.Claude), StrikeTint.For(EffortBrand.Silver), u);
            Assert.Equal(step.GetProperty("tint").GetProperty("ink").GetDouble(), mixed.Ink, 12);
        }
    }

    private static SKBitmap Liquid(EffortBrand brand, double ink, double seconds)
    {
        var spec = LiquidSpec.For(new TrackLook(TrackLook.Kind.Fast, brand));
        var lanes = new BubbleLanes(7, 240, 34);
        using var bitmap = new RenderTargetBitmap(new PixelSize(240, 34));
        using (var context = bitmap.CreateDrawingContext())
            LiquidTrack.Draw(context, new Size(240, 34), 240, seconds, spec, LiquidTrack.GradientFor(spec, 240, ThemeVariant.Light), lanes, false,
                ThemeVariant.Light, LiquidFlow.At(seconds, true), null, ink);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // drawBub: at ink 0 (every dark and mid liquid) nothing changes; on silver the bubbles get the neutral rim
    public void On_a_pale_liquid_the_bubbles_take_the_pale_look_and_at_ink_zero_nothing_changes() => headless.OnUiThread(() =>
    {
        using var plain = Liquid(EffortBrand.Silver, 0, 3.3);
        using var inked = Liquid(EffortBrand.Silver, 1, 3.3);
        using var again = Liquid(EffortBrand.Silver, 0.005, 3.3); // below 0.01 the page draws nothing extra
        int darker = 0, changed = 0;
        for (var y = 0; y < 34; y++)
            for (var x = 0; x < 240; x++)
            {
                SKColor a = plain.GetPixel(x, y), b = inked.GetPixel(x, y), c = again.GetPixel(x, y);
                Assert.Equal(a, c);
                if (a != b) changed++;
                if (b.Red + b.Green + b.Blue < a.Red + a.Green + a.Blue - 30) darker++;
            }
        Assert.True(changed > 0, "the ink draws the pale look on the bubbles");
        Assert.True(darker > 5, $"the neutral rim reads darker on the silver liquid ({darker} px)");
        Assert.True(changed < 240 * 34 / 4, "only the bubbles change, not the liquid");
    });
}
