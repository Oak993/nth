using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;
using Pixels = (int Stride, byte[] Bytes, Avalonia.PixelSize Size);

namespace Marrow.App.Tests.Views.Composer;

// The owner's composer findings of 2026-10-11 (installed release, 125%): the composer swelled above and below round the open voice pill (OD-0132), the placeholder
// stopped in a hard cut at the orb, and the ask-others glyph sat too close to the text.
[Collection("Headless")]
public sealed class ComposerVoiceUiFixTests(HeadlessSession headless)
{
    private static void Tick() { for (var k = 0; k < 3; k++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }

    private static int Most(Pixels a, Pixels b, int x, int y)
    {
        var at = y * a.Stride + x * 4;
        return Math.Max(Math.Abs(a.Bytes[at] - b.Bytes[at]), Math.Max(Math.Abs(a.Bytes[at + 1] - b.Bytes[at + 1]), Math.Abs(a.Bytes[at + 2] - b.Bytes[at + 2])));
    }

    /// <summary>What <paramref name="element"/> paints: the frame with it and without it (opacity 0), every device pixel where they differ by more than <paramref name="threshold"/>.</summary>
    private static (Pixels With, Pixels Without) Toggle(Control element)
    {
        Tick();
        var with = DictationProbe.Frame((AvaloniaWindow)TopLevel.GetTopLevel(element)!);
        var opacity = element.Opacity;
        element.Opacity = 0;
        Tick();
        var without = DictationProbe.Frame((AvaloniaWindow)TopLevel.GetTopLevel(element)!);
        element.Opacity = opacity;
        Tick();
        return (with, without);
    }

    /// <summary>The left and right edges, in window points, of the ink <paramref name="element"/> paints.</summary>
    private static (double Left, double Right) InkX(Control element, AvaloniaWindow window)
    {
        var (with, without) = Toggle(element);
        var first = int.MaxValue;
        var last = -1;
        for (var y = 0; y < with.Size.Height; y++)
        for (var x = 0; x < with.Size.Width; x++)
        {
            if (Most(with, without, x, y) < 24) continue;
            first = Math.Min(first, x);
            last = Math.Max(last, x);
        }
        Assert.True(last >= first, element.GetType().Name + " paints nothing");
        return (first / window.RenderScaling, (last + 1) / window.RenderScaling);
    }

    // ---- 1: the pill never swells the composer (OD-0132) ------------------------------------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-NEW-voicefix-ui-1")]
    [InlineData(1.0)]
    [InlineData(1.25)]
    public void The_open_pill_leaves_the_composer_outline_as_it_was(double scale) => headless.OnUiThread(() =>
    {
        // ComposerArea holds the composer in its GlassEffectContainer(spacing: 8): the pill's glass used to melt into the composer's there (the bulge)
        using var rig = new VoiceComposerRig();
        rig.QuietRoom();
        var area = rig.Main.OnMain(() => { var a = new ComposerArea(rig.Runtime, workingDirectory: null); AppEnvironment.SetAppModel(a, rig.App); return a; });
        var window = rig.Main.OnMain(() => Show(area, rig.App, 900));
        rig.PumpWindow(window);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, true));
            rig.PumpWindow(window);
            var view = rig.Main.OnMain(() => area.GetVisualDescendants().OfType<ComposerView>().Single());
            Run(rig, window, .4);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .3);
            var (closedRect, closed) = rig.Main.OnMain(() => { Tick(); return (Frame(view, window), DictationProbe.Frame(window)); });
            rig.Main.OnMain(() => Pill(view).Open = true);
            Run(rig, window, 1.0);
            rig.Main.OnMain(() =>
            {
                Tick();
                var open = DictationProbe.Frame(window);
                var openRect = Frame(view, window);
                Assert.Equal(closedRect, openRect);
                var pill = Pill(view);
                Assert.True(pill.ShownOpacity > .99, "the pill is open: " + pill.ShownOpacity);
                var pillRect = Frame(pill, window);
                Assert.True(pillRect.Left >= openRect.Left && pillRect.Right <= openRect.Right && pillRect.Top >= openRect.Top && pillRect.Bottom <= openRect.Bottom,
                    $"pill {pillRect} inside the composer {openRect}");
                // every device pixel outside the composer's box, and in the 2 points just inside its edge (its outline), is the same open as closed: nothing of the
                // pill (its glass, glow or rim) reaches the outline or past it
                var s = window.RenderScaling;
                var outer = new PixelRect((int)Math.Floor(openRect.Left * s), (int)Math.Floor(openRect.Top * s),
                    (int)Math.Ceiling(openRect.Right * s) - (int)Math.Floor(openRect.Left * s), (int)Math.Ceiling(openRect.Bottom * s) - (int)Math.Floor(openRect.Top * s));
                var inner = new PixelRect((int)Math.Ceiling((openRect.Left + 2) * s), (int)Math.Ceiling((openRect.Top + 2) * s),
                    (int)Math.Floor((openRect.Right - 2) * s) - (int)Math.Ceiling((openRect.Left + 2) * s), (int)Math.Floor((openRect.Bottom - 2) * s) - (int)Math.Ceiling((openRect.Top + 2) * s));
                var worst = 0;
                var where = default(PixelPoint);
                var changedInside = 0;
                for (var y = 0; y < open.Size.Height; y++)
                for (var x = 0; x < open.Size.Width; x++)
                {
                    var d = Most(open, closed, x, y);
                    if (inner.Contains(new PixelPoint(x, y))) { if (d > 24) changedInside++; continue; }
                    if (d > worst) { worst = d; where = new PixelPoint(x, y); }
                }
                Assert.True(changedInside > 200, "the pill shows inside the composer: " + changedInside);
                Assert.True(worst <= 3, $"the outline or the page round it changed by {worst} at {where} (composer {outer} at {s})");
            });
        }
        finally { rig.Close(window); }
    });

    // ---- 2: the fade beside the pill holds in a narrow column (OD-0130) ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-NEW-voicefix-ui-2")]
    [InlineData(1.0, 380)]
    [InlineData(1.25, 380)]
    [InlineData(1.25, 340)]
    public void A_narrow_column_beside_the_open_pill_still_fades(double scale, double width) => headless.OnUiThread(() =>
    {
        // a narrow window: the open pill squeezes the text column under the fade's width; whatever it holds never shows at full strength beside or under the pill
        using var rig = new VoiceComposerRig();
        rig.QuietRoom();
        var (view, window) = rig.Open(width);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, true));
            rig.PumpWindow(window);
            rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = "Abi, what's going on?" });
            Run(rig, window, .4);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .2);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var narrowest = double.MaxValue;
            for (var k = 0; k < 60; k++)
            {
                rig.Elapse(window, FrameTime);
                rig.Main.OnMain(() =>
                {
                    var column = LogicalQuery.SelfAndDescendants<ComposerTextColumn>(view).Single();
                    if (column.Margin.Right < .5) return;
                    narrowest = Math.Min(narrowest, column.Bounds.Width);
                    Assert.True(column.OpacityMask is not null, $"frame {k}: column {column.Bounds.Width:F1} wide, inset {column.Margin.Right:F1}, no fade");
                });
            }
            Assert.True(narrowest <= 24, "the pill squeezed the column under the fade's width: " + narrowest);
            rig.Main.OnMain(() =>
            {
                // and none of the text's ink is inside the pill
                var (with, without) = Toggle(DictationProbe.Box(view));
                var pill = Frame(Pill(view), window);
                var s = window.RenderScaling;
                var ink = 0;
                for (var y = (int)Math.Ceiling(pill.Top * s); y < (int)Math.Floor(pill.Bottom * s); y++)
                for (var x = (int)Math.Ceiling(pill.Left * s); x < (int)Math.Floor(pill.Right * s); x++)
                    if (Most(with, without, x, y) > 8) ink++;
                Assert.True(ink == 0, $"{ink} pixels of text inside the pill {pill}");
            });
            rig.Main.OnMain(() => view.Voice!.Cancel());
            Run(rig, window, 1.0);
        }
        finally { rig.Close(window); }
    });

    // ---- 3: a long placeholder fades out before the orb ------------------------------------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-NEW-voicefix-ui-3")]
    [InlineData(1.0)]
    [InlineData(1.25)]
    public void A_placeholder_longer_than_the_field_fades_out_before_its_edge(double scale) => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (view, window) = rig.Open(480);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, true));
            rig.PumpWindow(window);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .3);
            rig.Main.OnMain(() =>
            {
                var box = DictationProbe.Box(view);
                Assert.True(string.IsNullOrEmpty(box.Text));
                var available = box.Bounds.Width - box.Padding.Left - box.Padding.Right;
                Assert.True(box.CreatePlaceholderText().Width > available + 40, $"the placeholder '{box.Placeholder}' overruns the field ({available:F1})");
                var (with, without) = Toggle(box);
                var region = Frame(box, window);
                var orb = Frame(Slot(view), window);
                var s = window.RenderScaling;
                var top = (int)Math.Floor(region.Top * s);
                var bottom = (int)Math.Ceiling(region.Bottom * s);
                int Ink(double from, double to)
                {
                    var most = 0;
                    for (var y = top; y < bottom; y++)
                    for (var x = (int)Math.Floor(from * s); x < (int)Math.Ceiling(to * s); x++) most = Math.Max(most, Most(with, without, x, y));
                    return most;
                }
                var right = region.Right - box.Padding.Right;
                Assert.True(right <= orb.Left, $"the field ends ({right:F2}) before the orb ({orb.Left:F2})");
                // full ink well before the edge (the placeholder colour is dim: a few tens of levels over the glass), under a sixth of it in the last 2 points
                // (the last 24 fade to clear; 2 of 24 is a twelfth) and none past the field
                var full = Ink(region.Left, right - 30);
                Assert.True(full >= 30, "the placeholder is drawn: " + full);
                var edge = Ink(right - 2, right);
                Assert.True(edge <= Math.Max(3, full / 6), $"the placeholder ends in a cut: {edge} in its last 2 points against {full} before the fade");
                Assert.True(Ink(right, orb.Left + orb.Width / 2) == 0, "nothing of it past the field");
            });
        }
        finally { rig.Close(window); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-voicefix-ui-3")]
    public void A_placeholder_that_fits_is_drawn_as_before() => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (view, window) = rig.Open(900);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, 1.25, true));
            rig.PumpWindow(window);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .3);
            rig.Main.OnMain(() =>
            {
                var box = DictationProbe.Box(view);
                var available = box.Bounds.Width - box.Padding.Left - box.Padding.Right;
                var width = box.CreatePlaceholderText().Width;
                Assert.True(width < available, $"fits: {width:F1} in {available:F1}");
                var ink = InkX(box, window);
                var origin = Frame(box, window).Left + box.Padding.Left;
                // its last glyph is at full strength: the ink runs to the text's own end
                Assert.Equal(origin + width, ink.Right, 1.6);
            });
        }
        finally { rig.Close(window); }
    });

    // ---- 6: the ask-others glyph sits midway between the attach glyph and the text -------------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-NEW-voicefix-ui-6")]
    [InlineData(1.0)]
    [InlineData(1.25)]
    public void The_ask_others_glyph_is_as_far_from_the_attach_glyph_as_from_the_text(double scale) => headless.OnUiThread(() =>
    {
        var accounts = new OpinionViewRig();
        using var model = accounts.Model();
        using var rig = new VoiceComposerRig();
        using var offer = AskOthersComposerSeams.Offer.Override(_ => model);
        var (view, window) = rig.Open(900);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, true));
            rig.PumpWindow(window);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .3);
            rig.Main.OnMain(() =>
            {
                var attach = LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Single();
                var glyph = view.GetVisualDescendants().OfType<AskOthersGlyph>().Single();
                var box = DictationProbe.Box(view);
                var a = InkX(attach, window);
                var g = InkX(glyph, window);
                var t = InkX(box, window);
                var before = g.Left - a.Right;
                var after = t.Left - g.Right;
                Assert.True(Math.Abs(before - after) <= 1 / scale + .01, $"attach to ask-others {before:F2}, ask-others to text {after:F2} at {scale}");
                // the glyph is whole (nothing clips it) and the text starts where it did (14 + 34 + the 28 point button)
                Assert.True(g.Right - g.Left >= 12.5, "the glyph is drawn whole: " + (g.Right - g.Left));
                Assert.Equal(Frame(view, window).Left + 76, Frame(box, window).Left, .01);
            });
        }
        finally { rig.Close(window); }
    });
}
