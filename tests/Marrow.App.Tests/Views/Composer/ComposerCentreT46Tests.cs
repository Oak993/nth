using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pickerpill-2")]
public sealed class ComposerCentreT46Tests(HeadlessSession headless)
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Single_line_ink_and_every_control_share_the_unchanged_composer_centre(double scale) => headless.OnUiThread(() =>
    {
        var errors = new List<string>();
        foreach (var dark in new[] { false, true })
        foreach (var collab in new[] { false, true })
        foreach (var diameter in new[] { 26, 32 })
        {
            var accounts = new OpinionViewRig();
            using var model = accounts.Model();
            using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { OrbSize = diameter });
            using var offer = AskOthersComposerSeams.Offer.Override(id => collab && id == rig.Runtime.ThreadID ? model : null);
            var (view, window) = rig.Open(900);
            try
            {
                rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, dark));
                rig.PumpWindow(window);
                foreach (var text in new[] { "", "Single line with a descender: gyp" })
                {
                    rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = text });
                    Run(rig, window, .4);
                    rig.Main.OnMain(() =>
                    {
                        DictationProbe.Blur(view);
                        var centre = Frame(view, window).Center.Y * scale;
                        var centres = new Dictionary<string, double>();
                        // Existing row sizes are retained, including the collab button's 28+8 text column.
                        Assert.Equal(Math.Round((collab ? 52 : 46) * scale, MidpointRounding.AwayFromZero) / scale, view.Bounds.Height, .001);
                        void Check(string name, double actual)
                        {
                            centres[name] = actual;
                            if (Math.Abs(actual - centre) > .5) errors.Add($"{scale} dark={dark} collab={collab} diameter={diameter} text={text.Length}: {name}={actual:F3}, centre={centre:F3}");
                        }
                        Check("orb", Slot(view).TranslatePoint(new Point(diameter / 2.0, diameter / 2.0), window)!.Value.Y * scale);
                        foreach (var control in LogicalQuery.SelfAndDescendants<ComposerControls>(view).Single().GetVisualDescendants().OfType<Control>()
                                     .Where(c => c is ModelEffortSlot or ContextMeter or SendButton))
                        {
                            Check(control.GetType().Name, Frame(control, window).Center.Y * scale);
                            Check(control.GetType().Name + " ink", ComposerCentreProbe.InkCentre(control, window));
                        }
                        var attach = LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Single(a => a.Place == ComposerAttachPlace.Inside);
                        Check("attach", Frame(attach, window).Center.Y * scale);
                        Check("attach ink", ComposerCentreProbe.InkCentre(attach, window));
                        if (collab)
                        {
                            Check("all-models glyph", Frame(view.GetVisualDescendants().OfType<AskOthersGlyph>().Single(), window).Center.Y * scale);
                            Check("all-models ink", ComposerCentreProbe.InkCentre(view.GetVisualDescendants().OfType<AskOthersGlyph>().Single(), window));
                        }
                        Check("text ink", ComposerCentreProbe.InkCentre(view, window));
                        Check("painted outline", ComposerCentreProbe.OutlineCentre(view, window));
                        foreach (var measured in centres.Where(pair => pair.Key != "painted outline"))
                            if (Math.Abs(measured.Value - centres["painted outline"]) > .5)
                                errors.Add($"{scale} dark={dark} collab={collab} diameter={diameter} text={text.Length}: {measured.Key} differs from painted outline by {measured.Value - centres["painted outline"]:F3}px");
                        ComposerCentreProbe.Save(view, window, $"centre-{scale}-{dark}-{collab}-{diameter}-{text.Length}", centre, centres);
                    });
                }
            }
            finally { rig.Close(window); }
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    });

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Without_voice_the_controls_keep_the_source_trailing_edge_and_share_the_painted_centre(double scale) => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel = false);
        var view = Marrow.App.Tests.Views.Composer.ComposerViewTests.Pill(h, h.Runtime, null);
        var window = h.Main.OnMain(() => Show(view, h.App, 820));
        try
        {
            h.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, true));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                window.FocusManager?.Focus(null);
                var controls = One<ComposerControls>(view);
                Assert.Null(controls.OrbSlot);
                var row = Assert.IsType<StackPanel>(controls.Child);
                var disc = LogicalQuery.SelfAndDescendants<Button>(row.Children[^1]).First(button => button.Bounds.Width >= 27);
                var outline = ComposerCentreProbe.OutlineCentre(view, window);
                var centres = new Dictionary<string, double>();
                foreach (var control in row.Children)
                {
                    centres[control.GetType().Name] = Frame(control, window).Center.Y * scale;
                    centres[control.GetType().Name + " ink"] = ComposerCentreProbe.InkCentre(control, window);
                }
                centres["text ink"] = ComposerCentreProbe.InkCentre(view, window);
                centres["painted outline"] = outline;
                if (Environment.GetEnvironmentVariable("T46_CENTRE_DUMP") is { } folder)
                {
                    System.IO.Directory.CreateDirectory(folder);
                    var box = DictationProbe.Box(view);
                    var formatted = box.CreatePlaceholderText();
                    using var layout = box.CreateComposerLayout(box.Text ?? "", Math.Max(1, box.Bounds.Width));
                    System.IO.File.WriteAllText(System.IO.Path.Combine(folder, $"metric-no-voice-{scale}.json"), System.Text.Json.JsonSerializer.Serialize(new
                    {
                        text = box.Text, placeholder = box.Placeholder, box = Frame(box, window), column = Frame(One<ComposerTextColumn>(view), window),
                        box.Padding, box.FontSize, font = box.FontFamily.ToString(), transform = (box.RenderTransform as Avalonia.Media.TranslateTransform)?.Y,
                        formatted = new { formatted.Height, formatted.Extent, formatted.OverhangAfter, geometry = formatted.BuildGeometry(new Point())?.Bounds }, layout = new { layout.Height, layout.Extent, layout.OverhangAfter },
                    }));
                }
                ComposerCentreProbe.Save(view, window, $"no-voice-{scale}", outline, centres);
                var expected = Frame(view, window).Right - 8;
                var actual = Frame(disc, window).Right;
                Assert.True(Math.Abs(actual - expected) * scale <= .05,
                    $"{scale}: disc right={actual}, expected={expected}; controls right={Frame(controls, window).Right}, row right={Frame(row, window).Right}");
                foreach (var centre in centres) Assert.InRange(Math.Abs(centre.Value - outline), 0, .5);
            });
        }
        finally { Close(h, window); }
    });

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Outside_attachment_keeps_its_circle_and_painted_symbol_on_the_composer_centre(double scale) => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { Attachment = VoiceAttachment.Outside });
        var (view, window) = rig.Open(600);
        try
        {
            rig.Main.OnMain(() => ComposerCentreProbe.Prepare(window, scale, true)); rig.PumpWindow(window);
            rig.Main.OnMain(() =>
            {
                DictationProbe.Blur(view);
                var centre = Frame(view, window).Center.Y * scale;
                var attach = LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Single(a => a.Place == ComposerAttachPlace.Outside);
                Assert.InRange(Math.Abs(Frame(attach, window).Center.Y * scale - centre), 0, .5);
                Assert.InRange(Math.Abs(ComposerCentreProbe.InkCentre(attach, window) - centre), 0, .5);
            });
        }
        finally { rig.Close(window); }
    });

    [Fact]
    public void A_single_line_with_auxiliary_chips_keeps_the_controls_on_the_source_bottom_line() => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (view, window) = rig.Open(600);
        try
        {
            rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = "one line", Quotes = new Marrow.Core.Port.ValueList<Marrow.App.Runtime.ReplyQuote>([new() { Text = "quoted line" }]) });
            Run(rig, window, .6);
            rig.Main.OnMain(() =>
            {
                Assert.True(view.Bounds.Height > 46);
                Assert.Equal(view.Bounds.Height - 8, Frame(LogicalQuery.SelfAndDescendants<ComposerControls>(view).Single(), view).Bottom, .001);
            });
        }
        finally { rig.Close(window); }
    });

    [Fact]
    public void The_all_models_button_takes_pointer_input_on_its_glyph_and_its_padding() => headless.OnUiThread(() =>
    {
        var accounts = new OpinionViewRig();
        using var model = accounts.Model();
        using var rig = new VoiceComposerRig();
        using var offer = AskOthersComposerSeams.Offer.Override(_ => model);
        var (view, window) = rig.Open(600);
        try
        {
            rig.Main.OnMain(() =>
            {
                var button = view.GetVisualDescendants().OfType<AskOthersView>().Single().Button;
                foreach (var point in new[] { new Point(14, 14), new Point(2, 2), new Point(26, 26) })
                {
                    var at = button.TranslatePoint(point, window)!.Value;
                    var hit = window.InputHitTest(at) as Visual;
                    Assert.True(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true, $"button does not take {point}: {hit}");
                }
            });
        }
        finally { rig.Close(window); }
    });

    [Fact]
    public void Controls_stay_on_the_bottom_line_until_the_multiline_clip_finishes_folding() => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (view, window) = rig.Open(600);
        try
        {
            rig.Main.OnMain(() => Marrow.App.Port.AppEnvironment.SetWindowLiveResize(LogicalQuery.SelfAndDescendants<ComposerTextColumn>(view).Single(), new Marrow.App.Window.WindowLiveResize(window)));
            rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = "one\ntwo\nthree" });
            Run(rig, window, .6);
            rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = "one" });
            rig.PumpWindow(window);
            var frames = Frames(rig, window, 30, () => (Height: view.Bounds.Height, Bottom: Frame(LogicalQuery.SelfAndDescendants<ComposerControls>(view).Single(), view).Bottom));
            Assert.Contains(frames, f => f.Height > 46.01);
            foreach (var frame in frames.Where(f => f.Height > 46.01)) Assert.Equal(frame.Height - 8, frame.Bottom, .001);
            Assert.Equal(46, frames[^1].Height, .001);
        }
        finally { rig.Close(window); }
    });

    [Theory]
    [InlineData(VoiceAttachment.Inside)]
    [InlineData(VoiceAttachment.Outside)]
    public void Wrapped_and_multiline_controls_keep_the_source_bottom_alignment(VoiceAttachment place) => headless.OnUiThread(() =>
    {
        var accounts = new OpinionViewRig();
        using var model = accounts.Model();
        using var rig = new VoiceComposerRig(VoiceOrbSettings.Default with { Attachment = place });
        using var offer = AskOthersComposerSeams.Offer.Override(_ => model);
        var (view, window) = rig.Open(600);
        try
        {
            foreach (var text in new[] { "one\ntwo\nthree", string.Join(" ", Enumerable.Repeat("wrapping words", 20)) })
            {
                rig.Main.OnMain(() => rig.Runtime.Draft = rig.Runtime.Draft with { Text = text });
                Run(rig, window, .6);
                rig.Main.OnMain(() =>
                {
                    Assert.True(view.Bounds.Height > 52);
                    var controls = LogicalQuery.SelfAndDescendants<ComposerControls>(view).Single();
                    Assert.Equal(view.Bounds.Height - 8, Frame(controls, view).Bottom, .001);
                    var attach = LogicalQuery.SelfAndDescendants<ComposerAttachButton>(view).Single();
                    Assert.Equal(view.Bounds.Height - (place == VoiceAttachment.Inside ? 8 : 4), Frame(attach, view).Bottom, .001);
                    if (place == VoiceAttachment.Outside) Assert.Equal(view.Bounds.Height - 22, Frame(attach, view).Center.Y, .001);
                });
            }
        }
        finally { rig.Close(window); }
    });
}
