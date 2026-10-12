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
using Avalonia.Media;
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
    [Trait("Decision", "OD-0132")] // ED-NEW-voicefix-ui-1
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
    [Trait("Decision", "OD-0130")] // ED-NEW-voicefix-ui-2
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
    [Trait("Decision", "OD-0130")] // ED-NEW-voicefix-ui-3
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
    [Trait("Decision", "OD-0130")] // ED-NEW-voicefix-ui-3
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
    [Trait("Decision", "OD-0132")] // ED-NEW-voicefix-ui-6
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

    // ---- 8: the voice language question leaves the composer's outline as it was -----------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-voicefix-ui-8
    [InlineData(1.0)]
    [InlineData(1.25)]
    public void The_language_question_leaves_the_composer_outline_as_it_was(double scale) => headless.OnUiThread(() =>
    {
        // the question's card stands over the composer's top edge, in ComposerArea's GlassEffectContainer(spacing: 8): its glass used to melt into the composer's
        // there, the composer's top edge flaring up into the card's bottom corners
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
            var closed = rig.Main.OnMain(() => { Tick(); return DictationProbe.Frame(window); });
            rig.Main.OnMain(() => Question(view).Open(false));
            Run(rig, window, 1.0);
            rig.Main.OnMain(() =>
            {
                Tick();
                var open = DictationProbe.Frame(window);
                var question = Frame(Question(view), window);
                var composer = Frame(view, window);
                Assert.True(question.Bottom > composer.Top && question.Left > composer.Left && question.Right < composer.Right,
                    $"the question {question} stands over the composer's top edge {composer}");
                // every device pixel within 3 points of the composer's edge, outside it or in, is the same open as closed, but for those under the question's card
                var s = window.RenderScaling;
                var worst = 0;
                var where = default(Point);
                var band = 0;
                for (var y = 0; y < open.Size.Height; y++)
                for (var x = 0; x < open.Size.Width; x++)
                {
                    var at = new Point((x + .5) / s, (y + .5) / s);
                    if (question.Contains(at) || !composer.Inflate(3).Contains(at) || composer.Deflate(3).Contains(at)) continue;
                    band++;
                    var d = Most(open, closed, x, y);
                    if (d > worst) { worst = d; where = at; }
                }
                Assert.True(band > 1000, "the outline was looked at: " + band);
                Assert.True(worst <= 3, $"the composer's outline changed by {worst} at {where} beside the question {question} (composer {composer} at {s})");
            });
        }
        finally { rig.Close(window); }
    });

    // ---- 9: in a narrow composer the buttons move out of the open pill's way ------------------------------------------------------------------------------------

    /// <summary>A control's layout box in window points (its render transform left out: a button scaling away still owns this box).</summary>
    private static Rect LayoutBox(Control control, AvaloniaWindow window) => new(control.GetVisualParent()!.TranslatePoint(control.Bounds.TopLeft, window)!.Value, control.Bounds.Size);

    /// <summary>A button's box and the room the pill leaves it on its right (ComposerView.PillClearance).</summary>
    private static Rect Room(Control control, AvaloniaWindow window)
    {
        var box = LayoutBox(control, window);
        return box.WithWidth(box.Width + 6);
    }

    private static double ScaleOf(Control control) => control.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;

    [Theory]
    [Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-voicefix-ui-9
    [InlineData(1.0, 380)]
    [InlineData(1.25, 380)]
    [InlineData(1.25, 340)]
    public void In_a_narrow_composer_the_buttons_move_out_of_the_open_pills_way_and_come_back(double scale, double width) => headless.OnUiThread(() =>
    {
        // the narrowest composer (the 860 point window less the sidebar and a side panel): the open pill would run over the ask-others and attach buttons
        var accounts = new OpinionViewRig();
        using var model = accounts.Model();
        using var rig = new VoiceComposerRig();
        using var offer = AskOthersComposerSeams.Offer.Override(_ => model);
        rig.QuietRoom();
        var (view, window) = rig.Open(width);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, true));
            rig.PumpWindow(window);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .3);
            var (attach, ask) = rig.Main.OnMain(() => (LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Single(), view.GetVisualDescendants().OfType<AskOthersView>().Single()));
            var buttons = new (Control Button, string Name)[] { (attach, "attach"), (ask, "ask-others") };
            rig.Main.OnMain(() =>
            {
                foreach (var (button, name) in buttons) Assert.True(button.Opacity > .999 && ScaleOf(button) > .999 && button.IsHitTestVisible, name + " is in place before the pill opens");
            });
            var between = new Dictionary<string, int>();
            var step = new Dictionary<string, double>();
            void Frames(string phase)
            {
                var last = buttons.ToDictionary(b => b.Name, b => rig.Main.OnMain(() => b.Button.Opacity));
                for (var k = 0; k < 60; k++)
                {
                    rig.Elapse(window, FrameTime);
                    rig.Main.OnMain(() =>
                    {
                        var pill = Pill(view);
                        var pillRect = Frame(pill, window);
                        foreach (var (button, name) in buttons)
                        {
                            if (button.Opacity > .05 && button.Opacity < .95) between[phase + name] = between.GetValueOrDefault(phase + name) + 1;
                            step[phase + name] = Math.Max(step.GetValueOrDefault(phase + name), Math.Abs(button.Opacity - last[name]));
                            last[name] = button.Opacity;
                            // the pill anyone can see never comes within 6 points of the layout box of a button anyone can see
                            if (pill.ShownOpacity <= .05) continue;
                            var room = Room(button, window);
                            Assert.False(room.Intersects(pillRect) && button.Opacity > .01,
                                $"{phase}frame {k}: the pill {pillRect} (opacity {pill.ShownOpacity:F2}) in the room of the {name} button {room} (opacity {button.Opacity:F2})");
                        }
                    });
                }
            }
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Frames("opening ");
            var gone = rig.Main.OnMain(() =>
            {
                var pill = Pill(view);
                Assert.True(pill.ShownOpacity > .99, "the pill is open: " + pill.ShownOpacity);
                var pillRect = Frame(pill, window);
                // the case is real: at its full width the pill comes within 6 points of a button's box, and that button has gone (and takes no clicks)
                var reached = buttons.Where(b => Room(b.Button, window).Intersects(pillRect)).ToArray();
                Assert.True(reached.Length > 0, $"the open pill {pillRect} reaches no button at {width}");
                foreach (var (button, name) in reached) Assert.True(button.Opacity < .01 && !button.IsHitTestVisible, $"the {name} button is out of the way: opacity {button.Opacity:F3}");
                return reached.Select(b => b.Name).ToArray();
            });
            rig.Main.OnMain(() => view.Voice!.Cancel());
            Frames("closing ");
            Run(rig, window, 1.0);
            rig.Main.OnMain(() =>
            {
                Assert.True(Pill(view).ShownOpacity < .01, "the pill has closed");
                foreach (var (button, name) in buttons)
                {
                    Assert.True(Math.Abs(button.Opacity - 1) < .001 && Math.Abs(ScaleOf(button) - 1) < .001 && button.IsHitTestVisible,
                        $"the {name} button is back: opacity {button.Opacity:F3}, scale {ScaleOf(button):F3}");
                }
            });
            // it moves over several frames and never jumps: out with the pill's own travel, back on the spring
            foreach (var name in gone)
            {
                Assert.True(between.GetValueOrDefault("opening " + name) >= 3, $"the {name} button went in {between.GetValueOrDefault("opening " + name)} frames");
                Assert.True(step.GetValueOrDefault("opening " + name) <= .4, $"the {name} button went by {step.GetValueOrDefault("opening " + name):F2} in one frame");
                Assert.True(between.GetValueOrDefault("closing " + name) >= 10, $"the {name} button came back in {between.GetValueOrDefault("closing " + name)} frames");
                Assert.True(step.GetValueOrDefault("closing " + name) <= .15, $"the {name} button came back by {step.GetValueOrDefault("closing " + name):F2} in one frame");
            }
        }
        finally { rig.Close(window); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-reltrace-1")] // ED-NEW-voicefix-ui-9
    public void A_composer_with_room_keeps_its_buttons_while_the_pill_is_open() => headless.OnUiThread(() =>
    {
        var accounts = new OpinionViewRig();
        using var model = accounts.Model();
        using var rig = new VoiceComposerRig();
        using var offer = AskOthersComposerSeams.Offer.Override(_ => model);
        rig.QuietRoom();
        var (view, window) = rig.Open(900);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, 1.25, true));
            rig.PumpWindow(window);
            rig.Main.OnMain(() => DictationProbe.Blur(view));
            Run(rig, window, .3);
            var (attach, ask) = rig.Main.OnMain(() => (LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Single(), view.GetVisualDescendants().OfType<AskOthersView>().Single()));
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            for (var k = 0; k < 60; k++)
            {
                rig.Elapse(window, FrameTime);
                rig.Main.OnMain(() =>
                {
                    foreach (var button in new Control[] { attach, ask })
                        Assert.True(button.Opacity > .999 && Math.Abs(ScaleOf(button) - 1) < .001 && button.IsHitTestVisible, $"frame {k}: {button.GetType().Name} at {button.Opacity:F3}");
                });
            }
            rig.Main.OnMain(() => Assert.True(Pill(view).ShownOpacity > .99, "the pill is open"));
            rig.Main.OnMain(() => view.Voice!.Cancel());
            Run(rig, window, 1.0);
        }
        finally { rig.Close(window); }
    });
}
