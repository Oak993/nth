using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.SwiftFont (ED-091): the SwiftUI Font value and the AppKit NSFont factories, resolved to Avalonia.
// Expected values come from the B2 call sites cited on each test, the ED-059 size table
// (B1:DroppyCode/UI/Common/ChatZoom.swift:45-55), ED-091 (weights 100 to 900, headline bold, no zoom,
// tnum for monospacedDigit, only the default and monospaced designs) and OD-0009 (Inter for text,
// Cascadia Mono for monospaced). Never from the C# implementation.
// Visual parity of the sizes and of the headline weight stays UNVERIFIED under OD-0001 (ED-059, ED-091);
// these tests pin the recorded decisions, not the macOS rendering.
[Collection("Headless")]
public sealed class SwiftFontTests(HeadlessSession headless)
{
    // OD-0009 and porting/probes/p9/INTER.md "What to ship": Inter comes from Avalonia.Fonts.Inter, reached as
    // fonts:Inter#Inter through the package's InterFontCollection (key fonts:Inter). A name lookup ("Inter") is not
    // enough: probes/p9/RESULT.md section 2 finding 1, Avalonia substitutes Segoe UI for a family it cannot find, and on
    // the owner's PC a per-user Inter 4.001 is installed (probes/p9/INTER.md, "Reference only"), so the key is checked too.
    private const string TextFace = "Inter";
    private static readonly Uri TextFaceSource = new("fonts:Inter");

    // ED-107 (3): Cascadia Mono (OD-0009) ships inside the app (porting/probes/p9/CASCADIA.md) and is reached as
    // fonts:CascadiaMono#Cascadia Mono through CascadiaMonoFontCollection (key fonts:CascadiaMono), never by name:
    // probes/p9/RESULT.md section 2, it is not an inbox font, and a family Avalonia cannot find silently becomes Segoe UI.
    // So the key is checked as well as the name.
    private const string MonospacedFace = "Cascadia Mono";
    private static readonly Uri MonospacedFaceSource = new("fonts:CascadiaMono");

    private static void AssertTextFace(FontFamily family)
    {
        Assert.Equal(TextFace, family.Name);
        Assert.Equal(TextFaceSource, family.Key?.Source);
    }

    private static void AssertMonospacedFace(FontFamily family)
    {
        Assert.Equal(MonospacedFace, family.Name);
        Assert.Equal(MonospacedFaceSource, family.Key?.Source);
    }

    private static void AssertSameFace(FontFamily expected, FontFamily actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Key?.Source, actual.Key?.Source);
    }

    private static bool HasNoFeatures(FontFeatureCollection? features) => features is null || features.Count == 0;

    // ED-091: monospacedDigit is the tnum feature; exactly one feature, tnum with value 1, and nothing else.
    private static bool HasTabularDigits(FontFeatureCollection? features) =>
        features is not null && features.Count == 1 && features[0].Tag == "tnum" && features[0].Value == 1;

    // ---- The size of each text style (ED-059 table) ----

    [Theory]
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-091")]
    [Trait("Rule", "R-VIEW-11")]
    [InlineData(SwiftFont.TextStyle.LargeTitle, 26.0)] // B1 ChatZoom.swift:47
    [InlineData(SwiftFont.TextStyle.Title, 22.0)] // B1 ChatZoom.swift:48
    [InlineData(SwiftFont.TextStyle.Title2, 17.0)] // B1 ChatZoom.swift:49
    [InlineData(SwiftFont.TextStyle.Title3, 15.0)] // B1 ChatZoom.swift:50
    [InlineData(SwiftFont.TextStyle.Headline, 13.0)] // B1 ChatZoom.swift:51
    [InlineData(SwiftFont.TextStyle.Body, 13.0)] // B1 ChatZoom.swift:51
    [InlineData(SwiftFont.TextStyle.Callout, 12.0)] // B1 ChatZoom.swift:52
    [InlineData(SwiftFont.TextStyle.Subheadline, 11.0)] // B1 ChatZoom.swift:53
    [InlineData(SwiftFont.TextStyle.Footnote, 10.0)] // B1 ChatZoom.swift:54
    [InlineData(SwiftFont.TextStyle.Caption, 10.0)] // B1 ChatZoom.swift:54
    [InlineData(SwiftFont.TextStyle.Caption2, 10.0)] // B1 ChatZoom.swift:54
    public void A_text_style_resolves_to_the_ED_059_point_size_in_Inter_upright_with_no_features(SwiftFont.TextStyle style, double size)
    {
        var resolved = SwiftFont.System(style).Resolve();
        Assert.Equal(size, resolved.FontSize);
        AssertTextFace(resolved.FontFamily);
        Assert.Equal(FontStyle.Normal, resolved.FontStyle);
        Assert.True(HasNoFeatures(resolved.FontFeatures));
    }

    [Fact]
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-091")]
    public void Each_text_style_static_is_the_same_style_as_system_of_that_style()
    {
        // Swift: Font.largeTitle ... Font.caption2 are the text styles (.font(.caption), .font(.headline) at
        // UI/Common/Components.swift:54 and App/Views/Composer/RequestCards.swift:13).
        var pairs = new (SwiftFont Static, SwiftFont.TextStyle Style)[]
        {
            (SwiftFont.LargeTitle, SwiftFont.TextStyle.LargeTitle),
            (SwiftFont.Title, SwiftFont.TextStyle.Title),
            (SwiftFont.Title2, SwiftFont.TextStyle.Title2),
            (SwiftFont.Title3, SwiftFont.TextStyle.Title3),
            (SwiftFont.Headline, SwiftFont.TextStyle.Headline),
            (SwiftFont.Subheadline, SwiftFont.TextStyle.Subheadline),
            (SwiftFont.Body, SwiftFont.TextStyle.Body),
            (SwiftFont.Callout, SwiftFont.TextStyle.Callout),
            (SwiftFont.Footnote, SwiftFont.TextStyle.Footnote),
            (SwiftFont.Caption, SwiftFont.TextStyle.Caption),
            (SwiftFont.Caption2, SwiftFont.TextStyle.Caption2),
        };
        Assert.Equal(11, pairs.Length);
        foreach (var (font, style) in pairs)
        {
            var fromStatic = font.Resolve();
            var fromSystem = SwiftFont.System(style).Resolve();
            Assert.Equal(fromSystem.FontSize, fromStatic.FontSize);
            Assert.Equal((int)fromSystem.FontWeight, (int)fromStatic.FontWeight);
            AssertSameFace(fromSystem.FontFamily, fromStatic.FontFamily);
            AssertTextFace(fromStatic.FontFamily);
            Assert.Equal(fromSystem.FontStyle, fromStatic.FontStyle);
        }
        Assert.Equal(26.0, SwiftFont.LargeTitle.Resolve().FontSize);
        Assert.Equal(10.0, SwiftFont.Caption2.Resolve().FontSize);
        Assert.Equal(12.0, SwiftFont.Callout.Resolve().FontSize);
    }

    // ---- Default weights: regular, except headline (ED-091) ----

    [Theory]
    [Trait("Decision", "ED-091")]
    [InlineData(SwiftFont.TextStyle.LargeTitle)]
    [InlineData(SwiftFont.TextStyle.Title)]
    [InlineData(SwiftFont.TextStyle.Title2)]
    [InlineData(SwiftFont.TextStyle.Title3)]
    [InlineData(SwiftFont.TextStyle.Subheadline)]
    [InlineData(SwiftFont.TextStyle.Body)]
    [InlineData(SwiftFont.TextStyle.Callout)]
    [InlineData(SwiftFont.TextStyle.Footnote)]
    [InlineData(SwiftFont.TextStyle.Caption)]
    [InlineData(SwiftFont.TextStyle.Caption2)]
    public void A_text_style_other_than_headline_with_no_weight_resolves_regular_400(SwiftFont.TextStyle style)
    {
        Assert.Equal(400, (int)SwiftFont.System(style).Resolve().FontWeight);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Headline_with_no_weight_resolves_bold_700()
    {
        // ED-091: macOS sets headline bold; platform knowledge, UNVERIFIED under OD-0001.
        // .font(.headline) at App/Views/Composer/RequestCards.swift:13.
        Assert.Equal(700, (int)SwiftFont.Headline.Resolve().FontWeight);
        Assert.Equal(700, (int)SwiftFont.System(SwiftFont.TextStyle.Headline).Resolve().FontWeight);
        Assert.Equal(13.0, SwiftFont.Headline.Resolve().FontSize);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Headline_with_an_explicit_weight_resolves_that_weight_not_bold()
    {
        // UI/Common/ChatFont.swift:7-9: Font.chat(.headline) is Font.system(.headline).weight(.semibold).
        Assert.Equal(600, (int)SwiftFont.System(SwiftFont.TextStyle.Headline).FontWeight(SwiftFont.Weight.Semibold).Resolve().FontWeight);
        Assert.Equal(500, (int)SwiftFont.System(SwiftFont.TextStyle.Headline, weight: SwiftFont.Weight.Medium).Resolve().FontWeight);
        // An explicit regular is a weight, so it is not replaced by the headline default.
        Assert.Equal(400, (int)SwiftFont.System(SwiftFont.TextStyle.Headline, weight: SwiftFont.Weight.Regular).Resolve().FontWeight);
        Assert.Equal(400, (int)SwiftFont.Headline.FontWeight(SwiftFont.Weight.Regular).Resolve().FontWeight);
    }

    // ---- The weight map, 100 to 900 in Font.Weight order (ED-091) ----

    [Theory]
    [Trait("Decision", "ED-091")]
    [InlineData(SwiftFont.Weight.UltraLight, 100)]
    [InlineData(SwiftFont.Weight.Thin, 200)]
    [InlineData(SwiftFont.Weight.Light, 300)]
    [InlineData(SwiftFont.Weight.Regular, 400)]
    [InlineData(SwiftFont.Weight.Medium, 500)]
    [InlineData(SwiftFont.Weight.Semibold, 600)]
    [InlineData(SwiftFont.Weight.Bold, 700)]
    [InlineData(SwiftFont.Weight.Heavy, 800)]
    [InlineData(SwiftFont.Weight.Black, 900)]
    public void Each_weight_resolves_to_its_OpenType_weight_class_through_every_way_of_giving_it(SwiftFont.Weight weight, int weightClass)
    {
        Assert.Equal(weightClass, (int)SwiftFont.System(size: 12, weight: weight).Resolve().FontWeight);
        Assert.Equal(weightClass, (int)SwiftFont.System(SwiftFont.TextStyle.Body, weight: weight).Resolve().FontWeight);
        Assert.Equal(weightClass, (int)SwiftFont.Caption2.FontWeight(weight).Resolve().FontWeight);
        Assert.Equal(weightClass, (int)SwiftFont.SystemFont(ofSize: 12, weight: weight).Resolve().FontWeight);
        Assert.Equal(weightClass, (int)SwiftFont.MonospacedSystemFont(ofSize: 12, weight: weight).Resolve().FontWeight);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void The_weights_are_exactly_the_nine_Font_Weight_cases_in_order()
    {
        Assert.Equal(
            [SwiftFont.Weight.UltraLight, SwiftFont.Weight.Thin, SwiftFont.Weight.Light, SwiftFont.Weight.Regular, SwiftFont.Weight.Medium,
             SwiftFont.Weight.Semibold, SwiftFont.Weight.Bold, SwiftFont.Weight.Heavy, SwiftFont.Weight.Black],
            Enum.GetValues<SwiftFont.Weight>());
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void The_text_styles_are_exactly_the_eleven_Font_TextStyle_cases()
    {
        Assert.Equal(
            [SwiftFont.TextStyle.LargeTitle, SwiftFont.TextStyle.Title, SwiftFont.TextStyle.Title2, SwiftFont.TextStyle.Title3,
             SwiftFont.TextStyle.Headline, SwiftFont.TextStyle.Subheadline, SwiftFont.TextStyle.Body, SwiftFont.TextStyle.Callout,
             SwiftFont.TextStyle.Footnote, SwiftFont.TextStyle.Caption, SwiftFont.TextStyle.Caption2],
            Enum.GetValues<SwiftFont.TextStyle>());
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void The_designs_are_only_default_and_monospaced_because_rounded_and_serif_do_not_occur_at_B2()
    {
        Assert.Equal([SwiftFont.Design.Default, SwiftFont.Design.Monospaced], Enum.GetValues<SwiftFont.Design>());
    }

    // ---- Fixed sizes ----

    [Fact]
    [Trait("Decision", "ED-091")]
    [Trait("Rule", "R-VIEW-11")]
    public void A_fixed_size_resolves_to_that_size_exactly_with_regular_weight_by_default()
    {
        // App/Views/Settings/ModelsSettingsPage.swift:68: .system(size: 11)
        var eleven = SwiftFont.System(size: 11).Resolve();
        Assert.Equal(11.0, eleven.FontSize);
        Assert.Equal(400, (int)eleven.FontWeight);
        AssertTextFace(eleven.FontFamily);
        Assert.Equal(FontStyle.Normal, eleven.FontStyle);
        Assert.True(HasNoFeatures(eleven.FontFeatures));

        // UI/Chrome/Chrome.swift:30: chevronFont = .system(size: 8, weight: .bold)
        var chevron = SwiftFont.System(size: 8, weight: SwiftFont.Weight.Bold).Resolve();
        Assert.Equal(8.0, chevron.FontSize);
        Assert.Equal(700, (int)chevron.FontWeight);

        // App/Views/RootView.swift:459: .system(size: 46, weight: .light)
        var light = SwiftFont.System(size: 46, weight: SwiftFont.Weight.Light).Resolve();
        Assert.Equal(46.0, light.FontSize);
        Assert.Equal(300, (int)light.FontWeight);

        // App/Views/Chat/HydraPanel.swift:235 passes 12.5; a fractional size is kept, not rounded.
        Assert.Equal(12.5, SwiftFont.System(size: 12.5, weight: SwiftFont.Weight.Semibold).Resolve().FontSize);
        // UI/Markdown/MarkdownView.swift:1130 rounds the markdown size to a half point: 9.5 stays 9.5.
        Assert.Equal(9.5, SwiftFont.System(size: 9.5).Resolve().FontSize);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void A_fixed_size_is_never_replaced_by_a_text_style_size()
    {
        // App/Views/Settings/SettingsView.swift:202: .system(size: 44, weight: .light), a size no style has.
        Assert.Equal(44.0, SwiftFont.System(size: 44, weight: SwiftFont.Weight.Light).Resolve().FontSize);
        Assert.Equal(13.0, SwiftFont.System(size: 13).Resolve().FontSize);
        Assert.Equal(14.0, SwiftFont.System(size: 14).Resolve().FontSize);
    }

    // ---- The faces (OD-0009) ----

    [Fact]
    [Trait("Decision", "ED-091")]
    public void The_default_design_is_Inter_and_the_monospaced_design_is_Cascadia_Mono()
    {
        // App/Views/Chat/ChatView.swift:1550: .system(.subheadline, design: .monospaced)
        var styled = SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced).Resolve();
        AssertMonospacedFace(styled.FontFamily);
        Assert.Equal(11.0, styled.FontSize);
        Assert.Equal(400, (int)styled.FontWeight);

        // App/Views/Settings/SettingsView.swift:729: .system(size: 12, design: .monospaced)
        var sized = SwiftFont.System(size: 12, design: SwiftFont.Design.Monospaced).Resolve();
        AssertMonospacedFace(sized.FontFamily);
        Assert.Equal(12.0, sized.FontSize);
        Assert.Equal(400, (int)sized.FontWeight);

        AssertTextFace(SwiftFont.System(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Default).Resolve().FontFamily);
        AssertTextFace(SwiftFont.System(size: 12, design: SwiftFont.Design.Default).Resolve().FontFamily);
        AssertTextFace(SwiftFont.Body.Resolve().FontFamily);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void The_monospaced_design_keeps_the_style_size_and_the_given_weight()
    {
        // UI/Common/ChatFont.swift:8 passes design through Font.system(style, design:); TimelineRows.swift:2122
        // asks for .caption monospaced.
        var caption = SwiftFont.System(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Resolve();
        AssertMonospacedFace(caption.FontFamily);
        Assert.Equal(10.0, caption.FontSize);

        var semibold = SwiftFont.System(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Monospaced, weight: SwiftFont.Weight.Semibold).Resolve();
        AssertMonospacedFace(semibold.FontFamily);
        Assert.Equal(12.0, semibold.FontSize);
        Assert.Equal(600, (int)semibold.FontWeight);
    }

    // ---- Modifiers ----

    [Fact]
    [Trait("Decision", "ED-091")]
    public void MonospacedDigit_adds_the_tnum_feature_and_changes_nothing_else()
    {
        // App/Views/Chat/HydraButton.swift:630: .system(.caption2, weight: .bold).monospacedDigit()
        var badge = SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Bold).MonospacedDigit().Resolve();
        Assert.True(HasTabularDigits(badge.FontFeatures));
        Assert.Equal(10.0, badge.FontSize);
        Assert.Equal(700, (int)badge.FontWeight);
        AssertTextFace(badge.FontFamily);
        Assert.Equal(FontStyle.Normal, badge.FontStyle);

        // UI/Common/Components.swift:54: .caption.monospacedDigit()
        var caption = SwiftFont.Caption.MonospacedDigit().Resolve();
        Assert.True(HasTabularDigits(caption.FontFeatures));
        Assert.Equal(10.0, caption.FontSize);
        Assert.Equal(400, (int)caption.FontWeight);

        // App/Views/Chat/HydraPanel.swift:235: .system(size: 12.5, weight: .semibold).monospacedDigit()
        var panel = SwiftFont.System(size: 12.5, weight: SwiftFont.Weight.Semibold).MonospacedDigit().Resolve();
        Assert.True(HasTabularDigits(panel.FontFeatures));
        Assert.Equal(12.5, panel.FontSize);
        Assert.Equal(600, (int)panel.FontWeight);

        // Applied twice it is still one tnum feature.
        Assert.True(HasTabularDigits(SwiftFont.Body.MonospacedDigit().MonospacedDigit().Resolve().FontFeatures));
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Without_monospacedDigit_there_is_no_tnum_feature_even_in_the_monospaced_design()
    {
        Assert.True(HasNoFeatures(SwiftFont.Body.Resolve().FontFeatures));
        Assert.True(HasNoFeatures(SwiftFont.System(size: 12, design: SwiftFont.Design.Monospaced).Resolve().FontFeatures));
        Assert.True(HasNoFeatures(SwiftFont.MonospacedSystemFont(ofSize: 12, weight: SwiftFont.Weight.Regular).Resolve().FontFeatures));
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Weight_modifier_sets_the_weight_and_keeps_the_style_size()
    {
        // App/Views/Diff/DiffInspector.swift:197: .caption2.weight(.semibold)
        var chevron = SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Semibold).Resolve();
        Assert.Equal(600, (int)chevron.FontWeight);
        Assert.Equal(10.0, chevron.FontSize);
        AssertTextFace(chevron.FontFamily);

        // App/Views/Terminal/TerminalPanel.swift:277: .caption2.weight(.bold)
        Assert.Equal(700, (int)SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Bold).Resolve().FontWeight);

        // The later weight is the one used.
        Assert.Equal(300, (int)SwiftFont.Body.FontWeight(SwiftFont.Weight.Bold).FontWeight(SwiftFont.Weight.Light).Resolve().FontWeight);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Italic_sets_the_italic_style_and_keeps_size_weight_and_face()
    {
        var italic = SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).Italic().Resolve();
        Assert.Equal(FontStyle.Italic, italic.FontStyle);
        Assert.Equal(12.0, italic.FontSize);
        Assert.Equal(500, (int)italic.FontWeight);
        AssertTextFace(italic.FontFamily);
        Assert.True(HasNoFeatures(italic.FontFeatures));
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Bold_sets_the_bold_weight_and_keeps_size_style_and_face()
    {
        var bold = SwiftFont.Footnote.Bold().Resolve();
        Assert.Equal(700, (int)bold.FontWeight);
        Assert.Equal(10.0, bold.FontSize);
        Assert.Equal(FontStyle.Normal, bold.FontStyle);
        AssertTextFace(bold.FontFamily);

        var monospaced = SwiftFont.System(size: 12, design: SwiftFont.Design.Monospaced).Bold().Resolve();
        Assert.Equal(700, (int)monospaced.FontWeight);
        AssertMonospacedFace(monospaced.FontFamily);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Modifiers_compose_and_each_keeps_what_the_others_set()
    {
        var all = SwiftFont.System(SwiftFont.TextStyle.Title2, design: SwiftFont.Design.Monospaced)
            .FontWeight(SwiftFont.Weight.Semibold).Italic().MonospacedDigit().Resolve();
        Assert.Equal(17.0, all.FontSize);
        Assert.Equal(600, (int)all.FontWeight);
        Assert.Equal(FontStyle.Italic, all.FontStyle);
        Assert.True(HasTabularDigits(all.FontFeatures));
        AssertMonospacedFace(all.FontFamily);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Modifiers_return_new_values_and_never_change_the_receiver()
    {
        var body = SwiftFont.Body;
        var bold = body.Bold();
        var italic = body.Italic();
        var digits = body.MonospacedDigit();
        var light = body.FontWeight(SwiftFont.Weight.Light);

        Assert.NotSame(body, bold);
        Assert.NotSame(body, italic);
        Assert.NotSame(body, digits);
        Assert.NotSame(body, light);

        var unchanged = body.Resolve();
        Assert.Equal(13.0, unchanged.FontSize);
        Assert.Equal(400, (int)unchanged.FontWeight);
        Assert.Equal(FontStyle.Normal, unchanged.FontStyle);
        Assert.True(HasNoFeatures(unchanged.FontFeatures));
        AssertTextFace(unchanged.FontFamily);

        // The shared static is untouched as well.
        var again = SwiftFont.Body.Resolve();
        Assert.Equal(400, (int)again.FontWeight);
        Assert.Equal(FontStyle.Normal, again.FontStyle);
        Assert.True(HasNoFeatures(again.FontFeatures));

        // A derived value is not changed by modifying it further.
        var caption = SwiftFont.Caption2.FontWeight(SwiftFont.Weight.Bold);
        var captionDigits = caption.MonospacedDigit();
        var captionItalic = caption.Italic();
        Assert.True(HasNoFeatures(caption.Resolve().FontFeatures));
        Assert.Equal(FontStyle.Normal, caption.Resolve().FontStyle);
        Assert.Equal(700, (int)caption.Resolve().FontWeight);
        Assert.True(HasTabularDigits(captionDigits.Resolve().FontFeatures));
        Assert.Equal(FontStyle.Normal, captionDigits.Resolve().FontStyle);
        Assert.Equal(FontStyle.Italic, captionItalic.Resolve().FontStyle);
        Assert.True(HasNoFeatures(captionItalic.Resolve().FontFeatures));

        // The same holds for a fixed size and for the headline default.
        var eight = SwiftFont.System(size: 8);
        _ = eight.FontWeight(SwiftFont.Weight.Heavy);
        Assert.Equal(400, (int)eight.Resolve().FontWeight);
        var headline = SwiftFont.Headline;
        _ = headline.FontWeight(SwiftFont.Weight.Regular);
        Assert.Equal(700, (int)headline.Resolve().FontWeight);
    }

    // ---- NSFont factories through the same type ----

    [Fact]
    [Trait("Decision", "ED-091")]
    public void NSFont_systemFont_of_size_is_Inter_regular_at_that_size()
    {
        // App/Views/Composer/ComposerView.swift:557: NSFont.systemFont(ofSize: 14)
        var composer = SwiftFont.SystemFont(ofSize: 14).Resolve();
        Assert.Equal(14.0, composer.FontSize);
        Assert.Equal(400, (int)composer.FontWeight);
        AssertTextFace(composer.FontFamily);
        Assert.Equal(FontStyle.Normal, composer.FontStyle);
        Assert.True(HasNoFeatures(composer.FontFeatures));

        // UI/Markdown/ProseRunView.swift:182: NSFont.systemFont(ofSize: size, weight: .semibold)
        var semibold = SwiftFont.SystemFont(ofSize: 13, weight: SwiftFont.Weight.Semibold).Resolve();
        Assert.Equal(13.0, semibold.FontSize);
        Assert.Equal(600, (int)semibold.FontWeight);
        AssertTextFace(semibold.FontFamily);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void NSFont_boldSystemFont_is_Inter_bold_and_the_italic_trait_is_the_italic_modifier()
    {
        // UI/Markdown/LinkParagraphView.swift:115: NSFont.boldSystemFont(ofSize: pointSize)
        var bold = SwiftFont.BoldSystemFont(ofSize: 13).Resolve();
        Assert.Equal(13.0, bold.FontSize);
        Assert.Equal(700, (int)bold.FontWeight);
        AssertTextFace(bold.FontFamily);
        Assert.Equal(FontStyle.Normal, bold.FontStyle);

        // UI/Markdown/LinkParagraphView.swift:116: the base font converted to the italic trait.
        var italic = SwiftFont.SystemFont(ofSize: 13).Italic().Resolve();
        Assert.Equal(FontStyle.Italic, italic.FontStyle);
        Assert.Equal(13.0, italic.FontSize);
        Assert.Equal(400, (int)italic.FontWeight);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void NSFont_monospacedSystemFont_is_Cascadia_Mono_at_that_size_and_weight()
    {
        // UI/Markdown/LinkParagraphView.swift:117: monospacedSystemFont(ofSize: max(9, pointSize - 1), weight: .regular)
        var inline = SwiftFont.MonospacedSystemFont(ofSize: 12, weight: SwiftFont.Weight.Regular).Resolve();
        AssertMonospacedFace(inline.FontFamily);
        Assert.Equal(12.0, inline.FontSize);
        Assert.Equal(400, (int)inline.FontWeight);
        Assert.Equal(FontStyle.Normal, inline.FontStyle);

        var nine = SwiftFont.MonospacedSystemFont(ofSize: 9, weight: SwiftFont.Weight.Medium).Resolve();
        Assert.Equal(9.0, nine.FontSize);
        Assert.Equal(500, (int)nine.FontWeight);
        AssertMonospacedFace(nine.FontFamily);
    }

    // ---- Apply to Avalonia controls (headless, R-TEST-7) ----

    [Fact]
    [Trait("Decision", "ED-091")]
    [Trait("Rule", "R-VIEW-11")]
    public void Apply_sets_family_size_weight_style_and_features_on_a_TextBlock() => headless.OnUiThread(() =>
    {
        var text = new TextBlock();
        SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Bold).MonospacedDigit().Italic().Apply(text);
        AssertTextFace(text.FontFamily);
        Assert.Equal(10.0, text.FontSize);
        Assert.Equal(700, (int)text.FontWeight);
        Assert.Equal(FontStyle.Italic, text.FontStyle);
        Assert.True(HasTabularDigits(text.FontFeatures));
    });

    [Fact]
    [Trait("Decision", "ED-091")]
    [Trait("Rule", "R-VIEW-11")]
    public void Apply_sets_family_size_weight_style_and_features_on_a_TemplatedControl() => headless.OnUiThread(() =>
    {
        var button = new Button();
        SwiftFont.System(size: 12, design: SwiftFont.Design.Monospaced).Apply(button);
        AssertMonospacedFace(button.FontFamily);
        Assert.Equal(12.0, button.FontSize);
        Assert.Equal(400, (int)button.FontWeight);
        Assert.Equal(FontStyle.Normal, button.FontStyle);
        Assert.True(HasNoFeatures(button.FontFeatures));

        var box = new TextBox();
        SwiftFont.Headline.MonospacedDigit().Apply(box);
        AssertTextFace(box.FontFamily);
        Assert.Equal(13.0, box.FontSize);
        Assert.Equal(700, (int)box.FontWeight);
        Assert.True(HasTabularDigits(box.FontFeatures));
    });

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Applying_another_font_replaces_every_property_of_the_previous_one() => headless.OnUiThread(() =>
    {
        // App/Views/Sidebar/SidebarView.swift:2042: .system(size: isSettled ? 12 : 13, weight: ... ? .medium : .regular)
        // switches the whole font when the state changes; nothing of the old font survives.
        var text = new TextBlock();
        SwiftFont.System(size: 13, weight: SwiftFont.Weight.Medium, design: SwiftFont.Design.Monospaced).Italic().MonospacedDigit().Apply(text);
        SwiftFont.System(size: 12, weight: SwiftFont.Weight.Regular).Apply(text);
        AssertTextFace(text.FontFamily);
        Assert.Equal(12.0, text.FontSize);
        Assert.Equal(400, (int)text.FontWeight);
        Assert.Equal(FontStyle.Normal, text.FontStyle);
        Assert.True(HasNoFeatures(text.FontFeatures));

        var button = new Button();
        SwiftFont.Title3.Bold().Italic().MonospacedDigit().Apply(button);
        SwiftFont.Caption.Apply(button);
        Assert.Equal(10.0, button.FontSize);
        Assert.Equal(400, (int)button.FontWeight);
        Assert.Equal(FontStyle.Normal, button.FontStyle);
        Assert.True(HasNoFeatures(button.FontFeatures));
    });

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Resolve_and_Apply_agree_for_every_text_style_with_and_without_monospacedDigit() => headless.OnUiThread(() =>
    {
        // UI/Common/Components.swift:54 and HydraButton.swift:630 put .monospacedDigit() on a text style; Apply must
        // carry the tnum feature exactly as Resolve gives it.
        foreach (var style in Enum.GetValues<SwiftFont.TextStyle>())
        {
            foreach (var font in new[] { SwiftFont.System(style), SwiftFont.System(style).MonospacedDigit() })
            {
                var resolved = font.Resolve();
                var text = new TextBlock();
                font.Apply(text);
                Assert.Equal(resolved.FontSize, text.FontSize);
                Assert.Equal((int)resolved.FontWeight, (int)text.FontWeight);
                AssertSameFace(resolved.FontFamily, text.FontFamily);
                AssertTextFace(text.FontFamily);
                Assert.Equal(resolved.FontStyle, text.FontStyle);
            }

            var plain = new TextBlock();
            SwiftFont.System(style).Apply(plain);
            Assert.True(HasNoFeatures(SwiftFont.System(style).Resolve().FontFeatures));
            Assert.True(HasNoFeatures(plain.FontFeatures));

            var digits = new TextBlock();
            SwiftFont.System(style).MonospacedDigit().Apply(digits);
            Assert.True(HasTabularDigits(SwiftFont.System(style).MonospacedDigit().Resolve().FontFeatures));
            Assert.True(HasTabularDigits(digits.FontFeatures));
        }

        // The monospaced design with monospacedDigit: App/Views/Chat/HydraReportsPopover.swift:605 .monospacedDigit() inside
        // :608 .font(.system(.subheadline, design: .monospaced)) (probes/p9/RESULT.md section 4).
        var mono = SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced).MonospacedDigit();
        var monoText = new TextBlock();
        mono.Apply(monoText);
        Assert.True(HasTabularDigits(mono.Resolve().FontFeatures));
        Assert.True(HasTabularDigits(monoText.FontFeatures));
        AssertMonospacedFace(monoText.FontFamily);
        Assert.Equal(11.0, monoText.FontSize);
    });

    // ---- The headline default through every modifier and factory (ED-091) ----

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Headline_stays_bold_through_italic()
    {
        var italic = SwiftFont.Headline.Italic().Resolve();
        Assert.Equal(700, (int)italic.FontWeight);
        Assert.Equal(13.0, italic.FontSize);
        Assert.Equal(FontStyle.Italic, italic.FontStyle);
        AssertTextFace(italic.FontFamily);
        Assert.True(HasNoFeatures(italic.FontFeatures));

        Assert.Equal(700, (int)SwiftFont.System(SwiftFont.TextStyle.Headline).Italic().Resolve().FontWeight);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Headline_in_the_monospaced_design_stays_bold()
    {
        // UI/Common/ChatFont.swift:8: let font = Font.system(style, design: design); :7 gives headline its weight only
        // when the caller passes none, so the (style, design) factory itself must keep the headline default.
        var mono = SwiftFont.System(SwiftFont.TextStyle.Headline, design: SwiftFont.Design.Monospaced).Resolve();
        Assert.Equal(700, (int)mono.FontWeight);
        Assert.Equal(13.0, mono.FontSize);
        Assert.Equal(FontStyle.Normal, mono.FontStyle);
        AssertMonospacedFace(mono.FontFamily);
        Assert.True(HasNoFeatures(mono.FontFeatures));

        var monoDigits = SwiftFont.System(SwiftFont.TextStyle.Headline, design: SwiftFont.Design.Monospaced).MonospacedDigit().Italic().Resolve();
        Assert.Equal(700, (int)monoDigits.FontWeight);
        Assert.Equal(FontStyle.Italic, monoDigits.FontStyle);
        Assert.True(HasTabularDigits(monoDigits.FontFeatures));
        AssertMonospacedFace(monoDigits.FontFamily);

        // The default design through the same factory is Inter, still bold.
        var text = SwiftFont.System(SwiftFont.TextStyle.Headline, design: SwiftFont.Design.Default).Resolve();
        Assert.Equal(700, (int)text.FontWeight);
        AssertTextFace(text.FontFamily);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Headline_bold_is_700_and_keeps_size_style_and_face()
    {
        var bold = SwiftFont.Headline.Bold().Resolve();
        Assert.Equal(700, (int)bold.FontWeight);
        Assert.Equal(13.0, bold.FontSize);
        Assert.Equal(FontStyle.Normal, bold.FontStyle);
        AssertTextFace(bold.FontFamily);
        Assert.True(HasNoFeatures(bold.FontFeatures));
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Headline_stays_bold_through_every_modifier_chain_of_two_and_three()
    {
        var digitsItalic = SwiftFont.Headline.MonospacedDigit().Italic().Resolve();
        Assert.Equal(700, (int)digitsItalic.FontWeight);
        Assert.Equal(13.0, digitsItalic.FontSize);
        Assert.Equal(FontStyle.Italic, digitsItalic.FontStyle);
        Assert.True(HasTabularDigits(digitsItalic.FontFeatures));
        AssertTextFace(digitsItalic.FontFamily);

        var chains = new[]
        {
            SwiftFont.Headline.Italic().MonospacedDigit(),
            SwiftFont.Headline.MonospacedDigit().MonospacedDigit(),
            SwiftFont.Headline.Italic().Italic(),
            SwiftFont.Headline.MonospacedDigit().Italic().MonospacedDigit(),
            SwiftFont.Headline.Italic().MonospacedDigit().Italic(),
            SwiftFont.Headline.Bold().Italic().MonospacedDigit(),
        };
        foreach (var chain in chains)
        {
            var resolved = chain.Resolve();
            Assert.Equal(700, (int)resolved.FontWeight);
            Assert.Equal(13.0, resolved.FontSize);
            AssertTextFace(resolved.FontFamily);
        }

        // An explicit weight later in the chain still wins over the default (ChatFont.swift:7-9).
        Assert.Equal(600, (int)SwiftFont.Headline.Italic().MonospacedDigit().FontWeight(SwiftFont.Weight.Semibold).Resolve().FontWeight);
        Assert.Equal(400, (int)SwiftFont.Headline.MonospacedDigit().FontWeight(SwiftFont.Weight.Regular).Italic().Resolve().FontWeight);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Headline_applied_to_a_control_through_italic_is_bold() => headless.OnUiThread(() =>
    {
        var text = new TextBlock();
        SwiftFont.Headline.Italic().Apply(text);
        Assert.Equal(700, (int)text.FontWeight);
        Assert.Equal(FontStyle.Italic, text.FontStyle);
        Assert.Equal(13.0, text.FontSize);
        AssertTextFace(text.FontFamily);

        var button = new Button();
        SwiftFont.System(SwiftFont.TextStyle.Headline, design: SwiftFont.Design.Monospaced).Apply(button);
        Assert.Equal(700, (int)button.FontWeight);
        AssertMonospacedFace(button.FontFamily);
    });

    // ---- Resolved Avalonia objects are not shared between values or controls ----

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Changing_a_resolved_feature_collection_does_not_change_a_later_resolve()
    {
        // ED-091: modifiers never change the receiver; the Avalonia FontFeatureCollection a Resolve hands out is
        // mutable, so a later Resolve must not see changes made to an earlier one.
        var font = SwiftFont.Caption.MonospacedDigit();

        var first = font.Resolve();
        Assert.NotNull(first.FontFeatures);
        first.FontFeatures!.Add(new FontFeature { Tag = "liga", Value = 0 });
        Assert.True(HasTabularDigits(font.Resolve().FontFeatures));
        Assert.True(HasTabularDigits(SwiftFont.Caption.MonospacedDigit().Resolve().FontFeatures));
        Assert.True(HasTabularDigits(SwiftFont.Body.MonospacedDigit().Resolve().FontFeatures));

        var second = font.Resolve();
        second.FontFeatures!.Clear();
        Assert.True(HasTabularDigits(font.Resolve().FontFeatures));
        Assert.True(HasTabularDigits(SwiftFont.System(size: 12.5, weight: SwiftFont.Weight.Semibold).MonospacedDigit().Resolve().FontFeatures));

        // A value with no features: filling what it hands out (if it hands out a collection) leaks nowhere.
        var plain = SwiftFont.Body.Resolve();
        plain.FontFeatures?.Add(new FontFeature { Tag = "tnum", Value = 1 });
        Assert.True(HasNoFeatures(SwiftFont.Body.Resolve().FontFeatures));
        Assert.True(HasNoFeatures(SwiftFont.Caption.Resolve().FontFeatures));
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void Changing_one_controls_features_after_Apply_changes_no_other_control_and_no_later_resolve() => headless.OnUiThread(() =>
    {
        var font = SwiftFont.Caption.MonospacedDigit();
        var a = new TextBlock();
        var b = new TextBlock();
        font.Apply(a);
        font.Apply(b);

        Assert.NotNull(a.FontFeatures);
        a.FontFeatures!.Add(new FontFeature { Tag = "liga", Value = 0 });
        Assert.True(HasTabularDigits(b.FontFeatures));
        Assert.True(HasTabularDigits(font.Resolve().FontFeatures));

        a.FontFeatures.Clear();
        Assert.True(HasTabularDigits(b.FontFeatures));
        Assert.True(HasTabularDigits(font.Resolve().FontFeatures));

        var c = new TextBlock();
        font.Apply(c);
        Assert.True(HasTabularDigits(c.FontFeatures));

        // The same for a font with no features, and across control types.
        var plainA = new TextBlock();
        var plainB = new Button();
        SwiftFont.Body.Apply(plainA);
        SwiftFont.Body.Apply(plainB);
        plainA.FontFeatures?.Add(new FontFeature { Tag = "tnum", Value = 1 });
        Assert.True(HasNoFeatures(plainB.FontFeatures));
        Assert.True(HasNoFeatures(SwiftFont.Body.Resolve().FontFeatures));
    });

    // ---- The faces resolve to real glyph typefaces (ED-107 (6), OD-0009) ----
    // Rasterisation stays UNVERIFIED under OD-0001; these tests check which face Avalonia picks, not how it draws.

    // ED-107 (6): the face is the typographic family name, or the family name when that is empty. Inter's static
    // Medium and SemiBold files name themselves 'Inter Medium' and 'Inter SemiBold' in name ID 1 and 'Inter' in
    // name ID 16 (probes/p9/INTER.md section 5).
    private static string FaceName(GlyphTypeface g) =>
        string.IsNullOrEmpty(g.TypographicFamilyName) ? g.FamilyName : g.TypographicFamilyName;

    private static GlyphTypeface ResolveGlyphTypeface(SwiftFont font)
    {
        var resolved = font.Resolve();
        var typeface = new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight);
        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var g));
        return g!;
    }

    [Theory]
    [Trait("Decision", "ED-107")]
    [Trait("Decision", "ED-091")]
    [InlineData(SwiftFont.Weight.Regular, 400)]
    [InlineData(SwiftFont.Weight.Medium, 500)]
    [InlineData(SwiftFont.Weight.Semibold, 600)]
    [InlineData(SwiftFont.Weight.Bold, 700)]
    public void The_default_design_resolves_to_a_real_Inter_face_at_the_requested_weight_without_simulation(SwiftFont.Weight weight, int weightClass) => headless.OnUiThread(() =>
    {
        // probes/p9/INTER.md section 5 (E:/Droppy/work/font-p9/run2.txt:1078-1081): fonts:Inter#Inter resolves 400, 500,
        // 600 and 700 to the package's static files with sim=None. Only these four weights were measured (ED-107 (6)).
        // App/Views/Chat/ChatBadge.swift:28 (.medium), ChatView.swift:1204 (.semibold), HydraButton.swift:630 (.bold).
        var g = ResolveGlyphTypeface(SwiftFont.System(SwiftFont.TextStyle.Body, weight: weight));
        Assert.Equal(TextFace, FaceName(g));
        Assert.Equal(weightClass, (int)g.Weight);
        Assert.Equal(FontSimulations.None, g.FontSimulations);
        Assert.Equal(FontStyle.Normal, g.Style);

        // The fixed-size and NSFont factories reach the same face.
        var sized = ResolveGlyphTypeface(SwiftFont.SystemFont(ofSize: 13, weight: weight));
        Assert.Equal(TextFace, FaceName(sized));
        Assert.Equal(weightClass, (int)sized.Weight);
        Assert.Equal(FontSimulations.None, sized.FontSimulations);
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    [Trait("Decision", "ED-091")]
    public void The_monospaced_design_resolves_to_the_bundled_fixed_pitch_Cascadia_Mono_regular_without_simulation() => headless.OnUiThread(() =>
    {
        // ED-107 (6) and probes/p9/RESULT.md section 2: Cascadia Mono at 400 with IsFixedPitch true.
        // App/Views/Chat/ChatView.swift:1550 .system(.subheadline, design: .monospaced);
        // UI/Markdown/LinkParagraphView.swift:117 monospacedSystemFont(ofSize:weight: .regular).
        foreach (var font in new[]
                 {
                     SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced),
                     SwiftFont.MonospacedSystemFont(ofSize: 12, weight: SwiftFont.Weight.Regular),
                 })
        {
            var g = ResolveGlyphTypeface(font);
            Assert.Equal(MonospacedFace, FaceName(g));
            Assert.Equal(400, (int)g.Weight);
            Assert.Equal(FontStyle.Normal, g.Style);
            Assert.Equal(FontSimulations.None, g.FontSimulations);
            Assert.True(g.Metrics.IsFixedPitch);
        }
    });

    [Theory]
    [Trait("Decision", "ED-107")]
    [Trait("Decision", "ED-091")]
    [InlineData(SwiftFont.Weight.Semibold, 600, false)]
    [InlineData(SwiftFont.Weight.Bold, 700, false)]
    [InlineData(SwiftFont.Weight.Regular, 400, true)]
    public void The_monospaced_design_resolves_semibold_bold_and_italic_to_bundled_Cascadia_Mono_files_without_simulation(SwiftFont.Weight weight, int weightClass, bool italic) => headless.OnUiThread(() =>
    {
        // ED-107 (1) and (6): markdown inline code reaches monospaced text at 600, 700 and italic
        // (UI/Markdown/MarkdownView.swift:1192-1196, headings :302-304 and :376-380). porting/probes/p9/CASCADIA.md
        // records CascadiaMono-SemiBold.ttf, -Bold.ttf and -Italic.ttf as present in release v2407.24 and shipped.
        var font = SwiftFont.MonospacedSystemFont(ofSize: 12, weight: weight);
        if (italic) font = font.Italic();
        var g = ResolveGlyphTypeface(font);
        Assert.Equal(MonospacedFace, FaceName(g));
        Assert.Equal(weightClass, (int)g.Weight);
        Assert.Equal(italic ? FontStyle.Italic : FontStyle.Normal, g.Style);
        Assert.Equal(FontSimulations.None, g.FontSimulations);
        Assert.True(g.Metrics.IsFixedPitch);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-107")]
    public void A_family_looked_up_by_name_resolves_even_when_the_face_is_missing_so_the_key_and_the_face_are_checked() => headless.OnUiThread(() =>
    {
        // probes/p9/RESULT.md section 2, finding 1: a by-name FontFamily("SF Mono") gets TryGetGlyphTypeface true, but
        // Avalonia substitutes another face. The boolean proves nothing; the fonts: key and the resolved name do.
        var typeface = new Typeface(new FontFamily("SF Mono"), FontStyle.Normal, Avalonia.Media.FontWeight.Normal);
        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var g));
        Assert.NotEqual("SF Mono", FaceName(g!));
        Assert.NotEqual("SF Mono", g!.FamilyName);
    });

    // ---- Inter italic and weight 800 come from the rsms/inter release files, under the fonts:Inter key ----
    // OD-0020 item 2 and porting/probes/p9/INTER-FILES.md: Inter-Italic, -MediumItalic, -SemiBoldItalic, -BoldItalic,
    // -ExtraBold and -ExtraBoldItalic (rsms/inter v4.1, extras/ttf) ship in src/Marrow.UI/Assets/Fonts beside the
    // Cascadia Mono files, and INTER-FILES.md:22 gives their registration beside the package's Inter family to this unit
    // (ED-107). Reviewer finding 1: registered only through the Cascadia collection, they resolved under
    // fonts:CascadiaMono#Inter, while fonts:Inter#Inter fell back to the upright face with Oblique simulation and to the
    // 700 face with Bold simulation. B2 reaches these faces: italic through the emphasized run,
    // UI/Markdown/LinkParagraphView.swift:116 (NSFontManager convert toHaveTrait: .italicFontMask) and :131
    // (intent.contains(.emphasized) -> italic); .heavy (800, ED-091) at UI/Common/HydraGlyph.swift:56 and
    // UI/Common/MCPGlyph.swift:49. No weight-900 file ships, so 900 stays unasserted (ED-107 (6)).

    [Theory]
    [Trait("Decision", "ED-107")]
    [Trait("Decision", "ED-091")]
    [InlineData(SwiftFont.Weight.Regular, 400, true)]
    [InlineData(SwiftFont.Weight.Medium, 500, true)]
    [InlineData(SwiftFont.Weight.Semibold, 600, true)]
    [InlineData(SwiftFont.Weight.Bold, 700, true)]
    [InlineData(SwiftFont.Weight.Heavy, 800, false)]
    [InlineData(SwiftFont.Weight.Heavy, 800, true)]
    public void The_default_design_resolves_italic_and_heavy_to_the_shipped_Inter_release_files_without_simulation(SwiftFont.Weight weight, int weightClass, bool italic) => headless.OnUiThread(() =>
    {
        foreach (var start in new[]
                 {
                     SwiftFont.System(SwiftFont.TextStyle.Body, weight: weight),
                     SwiftFont.SystemFont(ofSize: 13, weight: weight),
                 })
        {
            var font = italic ? start.Italic() : start;
            AssertTextFace(font.Resolve().FontFamily);
            var g = ResolveGlyphTypeface(font);
            Assert.Equal(TextFace, FaceName(g));
            Assert.Equal(weightClass, (int)g.Weight);
            Assert.Equal(italic ? FontStyle.Italic : FontStyle.Normal, g.Style);
            Assert.Equal(FontSimulations.None, g.FontSimulations);
        }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void The_monospaced_collection_holds_only_Cascadia_Mono_so_Inter_is_not_reachable_through_its_key() => headless.OnUiThread(() =>
    {
        // ED-107 (3): fonts:CascadiaMono is the Cascadia Mono family (CASCADIA.md). The Inter release files share its
        // folder (INTER-FILES.md), so the collection must not pick them up: before this fix fonts:CascadiaMono#Inter
        // resolved to Inter ExtraBold (reviewer finding 1).
        var typeface = new Typeface(new FontFamily("fonts:CascadiaMono#Inter"), FontStyle.Normal, Avalonia.Media.FontWeight.Normal);
        Assert.False(FontManager.Current.TryGetGlyphTypeface(typeface, out _));
    });
}
