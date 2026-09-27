using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// Source under test: DroppyCode/UI/Common/HydraGlyph.swift (B2 1fbf81e), :1-243. Expected values are transcribed
// from the Swift and the part's records, never from the C# under test:
//   :9-41   HydraGlyph - size default 20, breath ring while running, image tinted with persona.color, badge only
//           when a status is given and finished, accessibility label "name" or "name, rawValue"
//   :45-65  badge(for:) - diameter max(8, size * 0.5), checkmark/Chrome.success, xmark/Chrome.danger,
//           stop.fill/Chrome.secondaryText, symbol size diameter * 0.55 heavy white, stroke max(1, diameter * 0.12),
//           offset (diameter * 0.3, diameter * 0.3)
//   :91-97  HydraBreathRingView constants - period 1.6, restingScale 1.05, swollenScale 1.5, restingOpacity 0.9
//   :184-212 installAnimation - resting frame first (also what Reduce Motion shows forever), then an ease-out
//           swell/fade repeating every period seconds
//   :217-224 HydraMarkImage - Image("hydra-mark"), resizable, template, fit
//   :226-232 HydraNameStyle.text - Text(verbatim: persona.name).foregroundColor(base), one colour for the whole name
//   :234-242 HydraNameText - size default 13, weight default .medium, color default Chrome.primaryText.opacity(0.92)
[Collection("Headless")]
public sealed class HydraGlyphTests(HeadlessSession headless)
{
    private static readonly HydraPersona Persona = new("Hank", "hydra-head-01", 0xFF8A3D); // Core/Models/Hydra.swift:233

    private static Window Show(Control content, double width = 200, double height = 200)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Color Effective(SwiftColor color, Control control) =>
        ((ISolidColorBrush)color.Brush(control)).Color;

    // ---------------------------------------------------------------------------------------------------------
    // PR-HYDRA-193, UI-HYDRA-032: the glyph itself
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-HYDRA-193")]
    [Trait("Req", "UI-HYDRA-032")] // :9-11, :20, :27-34
    public void The_default_size_is_20_and_the_image_and_frame_are_sized_to_it() => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona);
        Assert.Equal(20, glyph.Size);
        var window = Show(glyph);
        var image = Assert.Single(VisualQuery.Descendants<SwiftImage>(glyph));
        Assert.Equal("hydra-head-01", image.AssetName);
        Assert.Equal(20, image.Width);
        Assert.Equal(20, image.Height);
        Assert.True(image.IsResizable);
        Assert.Equal(ImageRenderingMode.Template, image.EffectiveRenderingMode);
        Assert.Equal(ContentMode.Fit, image.AspectContentMode);
        Assert.Equal(Effective(HydraGlyph.PersonaColor(Persona), image), Effective(new SwiftColor(1, 138.0 / 255, 61.0 / 255, 1), image));
        window.Close();
    });

    [Theory]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(22)]
    [InlineData(24)]
    [InlineData(28)]
    [InlineData(30)]
    [Trait("Req", "UI-HYDRA-032")] // every call site's size, :11, :24, :32, :34
    public void Every_call_site_size_sizes_the_frame_and_the_image_alike(double size) => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, size: size);
        Assert.Equal(size, glyph.Size);
        var window = Show(glyph);
        var image = Assert.Single(VisualQuery.Descendants<SwiftImage>(glyph));
        Assert.Equal(size, image.Width);
        Assert.Equal(size, image.Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-193")] // :22-25
    public void A_running_head_shows_a_ring_that_takes_no_hits_and_a_head_at_rest_shows_none() => headless.OnUiThread(() =>
    {
        var running = new HydraGlyph(Persona, isRunning: true);
        var ring = Assert.Single(VisualQuery.Descendants<HydraBreathRingView>(running));
        Assert.False(ring.IsHitTestVisible);
        Assert.Equal(1.5, ring.StrokeWidth);

        var idle = new HydraGlyph(Persona);
        Assert.Empty(VisualQuery.Descendants<HydraBreathRingView>(idle));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-193")] // :23 NSColor(persona.color.opacity(0.55)).cgColor
    public void The_rings_colour_is_the_personas_colour_at_55_percent() => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, isRunning: true);
        var ring = VisualQuery.Descendants<HydraBreathRingView>(glyph).Single();
        Assert.Equal(Effective(HydraGlyph.PersonaColor(Persona).Opacity(0.55), ring), Effective(ring.StrokeColor, ring));
    });

    public static TheoryData<HydraHeadInfo.Status?> NotBadged() => new()
    {
        null,
        HydraHeadInfo.Status.Running,
    };

    [Theory]
    [MemberData(nameof(NotBadged))]
    [Trait("Req", "PR-HYDRA-193")]
    [Trait("Req", "UI-HYDRA-033")] // :36-38 if let status, status.isFinished
    public void No_status_or_a_running_status_shows_no_badge(HydraHeadInfo.Status? status) => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, status: status);
        Assert.Empty(VisualQuery.Descendants<Ellipse>(glyph));
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed)]
    [InlineData(HydraHeadInfo.Status.Failed)]
    [InlineData(HydraHeadInfo.Status.Stopped)]
    [Trait("Req", "PR-HYDRA-193")]
    [Trait("Req", "UI-HYDRA-033")] // :36-38, :45-65
    public void A_finished_status_shows_exactly_one_badge(HydraHeadInfo.Status status) => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, status: status);
        // Two ellipses: the badge's filled disc and its cut-out ring (:53-54, :60-62).
        Assert.Equal(2, VisualQuery.Descendants<Ellipse>(glyph).Count());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-193")] // :40
    public void The_accessibility_label_is_the_name_alone_with_no_status() => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona);
        Assert.Equal("Hank", AutomationProperties.GetName(glyph));
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Running, "running")]
    [InlineData(HydraHeadInfo.Status.Completed, "completed")]
    [InlineData(HydraHeadInfo.Status.Failed, "failed")]
    [InlineData(HydraHeadInfo.Status.Stopped, "stopped")]
    [Trait("Req", "PR-HYDRA-193")] // :40, HydraHeadInfo.Status raw values (Core/Models/Hydra.swift:313-320)
    public void The_accessibility_label_is_name_comma_space_raw_value_with_a_status(HydraHeadInfo.Status status, string raw) => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, status: status);
        Assert.Equal($"Hank, {raw}", AutomationProperties.GetName(glyph));
    });

    // ---------------------------------------------------------------------------------------------------------
    // UI-HYDRA-033: the outcome badge
    // ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(8.0, 8.0)]   // max(8, size * 0.5) floors at 8
    [InlineData(20.0, 10.0)]
    [InlineData(30.0, 15.0)]
    [Trait("Req", "UI-HYDRA-033")] // :46 let diameter = max(8, size * 0.5)
    public void The_badge_diameter_is_the_greater_of_8_and_half_the_size(double size, double diameter) => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, size: size, status: HydraHeadInfo.Status.Completed);
        var discs = VisualQuery.Descendants<Ellipse>(glyph).ToList();
        Assert.All(discs, e => Assert.Equal(diameter, e.Width));
        Assert.All(discs, e => Assert.Equal(diameter, e.Height));
    });

    [Theory]
    [InlineData(8.0, 1.0)]   // size 8 -> diameter 8 (floored by max(8, ...)) -> 8 * 0.12 = 0.96, floored by max(1, ...)
    [InlineData(24.0, 1.44)] // size 24 -> diameter 12 -> 12 * 0.12 = 1.44
    [Trait("Req", "UI-HYDRA-033")] // :61 lineWidth: max(1, diameter * 0.12)
    public void The_cut_out_rings_stroke_is_the_greater_of_1_and_12_percent_of_the_diameter(double size, double expected) => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, size: size, status: HydraHeadInfo.Status.Completed);
        var diameter = Math.Max(8, size * 0.5);
        var strokeWidth = Math.Max(1, diameter * 0.12);
        Assert.Equal(expected, strokeWidth, 3);
        var ring = VisualQuery.Descendants<Ellipse>(glyph).Single(e => e.StrokeThickness > 0);
        Assert.Equal(strokeWidth, ring.StrokeThickness, 3);
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-033")] // :63 .offset(x: diameter * 0.3, y: diameter * 0.3)
    public void The_badge_is_offset_by_30_percent_of_its_diameter_on_both_axes() => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, size: 20, status: HydraHeadInfo.Status.Completed);
        var diameter = Math.Max(8, 20 * 0.5);
        var badgeRoot = VisualQuery.Descendants<Ellipse>(glyph).First().GetVisualParent()!.GetVisualParent();
        var translate = Assert.IsType<TranslateTransform>(((Control)badgeRoot!).RenderTransform);
        Assert.Equal(diameter * 0.3, translate.X, 3);
        Assert.Equal(diameter * 0.3, translate.Y, 3);
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed, "checkmark")]
    [InlineData(HydraHeadInfo.Status.Failed, "xmark")]
    [InlineData(HydraHeadInfo.Status.Stopped, "stop.fill")]
    [Trait("Req", "UI-HYDRA-033")] // :47-51
    public void The_badges_symbol_and_disc_colour_follow_the_status(HydraHeadInfo.Status status, string symbol) => headless.OnUiThread(() =>
    {
        var glyph = new HydraGlyph(Persona, status: status);
        var symbolImage = VisualQuery.Descendants<SwiftImage>(glyph).Single(i => i.SymbolName is not null);
        Assert.Equal(symbol, symbolImage.SymbolName);
        Assert.Equal(Color.FromArgb(255, 255, 255, 255), ((ISolidColorBrush)symbolImage.Foreground!).Color);
        var font = SwiftImage.GetFont(symbolImage)!;
        var diameter = 20 * 0.5;
        Assert.Equal(diameter * 0.55, font.PointSize, 3);
        Assert.Equal(800, (int)font.Resolve().FontWeight);
        var disc = VisualQuery.Descendants<Ellipse>(glyph).Single(e => e.StrokeThickness == 0);
        var expected = status switch
        {
            HydraHeadInfo.Status.Completed => Marrow.UI.Chrome.Chrome.Success,
            HydraHeadInfo.Status.Failed => Marrow.UI.Chrome.Chrome.Danger,
            _ => Marrow.UI.Chrome.Chrome.SecondaryText,
        };
        Assert.Equal(Effective(expected, disc), ((ISolidColorBrush)disc.Fill!).Color); // UI/Common/HydraGlyph.swift:47-51
        var badgeRoot = Assert.IsAssignableFrom<Control>(disc.GetVisualParent()!.GetVisualParent());
        var table = typeof(SwiftTransition).GetField("attachments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var arguments = new object?[] { badgeRoot, null };
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!);
        var transition = arguments[1]!.GetType().GetProperty("Transition")!.GetValue(arguments[1]);
        Assert.Equal(SwiftTransition.Scale().Combined(SwiftTransition.Opacity), transition); // :64
    });

    // ---------------------------------------------------------------------------------------------------------
    // PR-HYDRA-194: the breath ring's constants and its swell/fade curve
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-HYDRA-194")] // :94-97
    public void The_breath_constants_are_1point6_seconds_1point05_to_1point5_scale_and_0point9_to_0_opacity()
    {
        Assert.Equal(1.6, HydraBreathRingView.Period);
        Assert.Equal(1.05, HydraBreathRingView.RestingScale);
        Assert.Equal(1.5, HydraBreathRingView.SwollenScale);
        Assert.Equal(0.9, HydraBreathRingView.RestingOpacity);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-194")] // :189-191 the resting frame is set first
    public void At_the_start_of_a_breath_the_ring_is_at_its_resting_scale_and_opacity()
    {
        var (scale, opacity) = HydraBreathRingView.Phase(0, reduceMotion: false);
        Assert.Equal(1.05, scale, 6);
        Assert.Equal(0.9, opacity, 6);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-194")] // :192 with Reduce Motion nothing further happens
    public void With_reduce_motion_the_ring_stays_at_its_resting_scale_and_opacity_forever()
    {
        foreach (var elapsed in new[] { 0.0, 0.1, 0.8, 1.59, 3.2, 100.0 })
        {
            var (scale, opacity) = HydraBreathRingView.Phase(elapsed, reduceMotion: true);
            Assert.Equal(1.05, scale, 6);
            Assert.Equal(0.9, opacity, 6);
        }
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-194")] // :193-198 the swell grows, the fade shrinks, as the breath advances
    public void Partway_through_a_breath_the_ring_has_grown_and_faded_from_its_resting_values()
    {
        var (scale, opacity) = HydraBreathRingView.Phase(0.8, reduceMotion: false);
        Assert.InRange(scale, 1.05, 1.5);
        Assert.True(scale > 1.05);
        Assert.InRange(opacity, 0.0, 0.9);
        Assert.True(opacity < 0.9);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-194")] // :206 repeatCount = .infinity: a new period starts exactly where the last one ends
    public void The_breath_repeats_forever_from_the_same_shared_origin()
    {
        var oneCycle = HydraBreathRingView.Phase(1.6 + 0.4, reduceMotion: false);
        var freshCycle = HydraBreathRingView.Phase(0.4, reduceMotion: false);
        Assert.Equal(freshCycle.Scale, oneCycle.Scale, 9);
        Assert.Equal(freshCycle.Opacity, oneCycle.Opacity, 9);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-194")] // :22-25 .allowsHitTesting(false); :103-104 masksToBounds = false (no ClipToBounds set)
    public void The_ring_view_never_clips_and_takes_no_hits() => headless.OnUiThread(() =>
    {
        var ring = new HydraBreathRingView();
        Assert.False(ring.IsHitTestVisible);
        Assert.False(ring.ClipToBounds);
    });

    // ---------------------------------------------------------------------------------------------------------
    // PR-HYDRA-195: HydraMarkImage, HydraNameStyle and HydraNameText
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-HYDRA-195")] // :219-222
    public void HydraMarkImage_is_the_hydra_mark_asset_resizable_template_and_fit() => headless.OnUiThread(() =>
    {
        var mark = new HydraMarkImage();
        var image = Assert.IsType<SwiftImage>(mark.Child);
        Assert.Equal("hydra-mark", image.AssetName);
        Assert.True(image.IsResizable);
        Assert.Equal(ImageRenderingMode.Template, image.EffectiveRenderingMode);
        Assert.Equal(ContentMode.Fit, image.AspectContentMode);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-195")] // :229-230
    public void HydraNameStyle_text_is_the_verbatim_name_in_one_colour() => headless.OnUiThread(() =>
    {
        var text = HydraNameStyle.Text(Persona, SwiftColor.White);
        var run = Assert.Single(text.Runs);
        Assert.Equal("Hank", run.Text);
        Assert.Equal(SwiftColor.White, run.Color);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-195")] // :236-238 defaults: size 13, weight .medium, color Chrome.primaryText.opacity(0.92)
    public void HydraNameText_defaults_to_13_point_medium_at_92_percent_primary_text() => headless.OnUiThread(() =>
    {
        var nameText = new HydraNameText(Persona);
        var block = Assert.IsType<TextBlock>(nameText.Child);
        var window = Show(nameText);
        var run = Assert.IsType<Avalonia.Controls.Documents.Run>(Assert.Single(block.Inlines!));
        Assert.Equal("Hank", run.Text);
        Assert.Equal(Effective(Marrow.UI.Chrome.Chrome.PrimaryText.Opacity(0.92), block), ((ISolidColorBrush)run.Foreground!).Color);
        Assert.Equal(13, run.FontSize, 3);
        Assert.Equal(500, (int)run.FontWeight); // UI/Common/HydraGlyph.swift:237-240 .medium
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-195")] // :226-232 the round number is never coloured differently from the name
    public void The_name_reads_as_a_single_run_never_splitting_out_the_round_number() => headless.OnUiThread(() =>
    {
        var round2 = new HydraPersona("Hank 2", "hydra-head-01", 0xFF8A3D);
        var text = HydraNameStyle.Text(round2, SwiftColor.White);
        Assert.Single(text.Runs);
        Assert.Equal("Hank 2", text.Runs[0].Text);
    });
}
