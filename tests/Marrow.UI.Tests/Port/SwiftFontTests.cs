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
    private const string TextFace = "Inter";
    private const string MonospacedFace = "Cascadia Mono";

    private static bool HasNoFeatures(FontFeatureCollection? features) => features is null || features.Count == 0;

    private static bool HasTabularDigits(FontFeatureCollection? features) =>
        features is not null && features.Count(f => f.Tag == "tnum" && f.Value == 1) == 1;

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
        Assert.Equal(TextFace, resolved.FontFamily.Name);
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
            Assert.Equal(fromSystem.FontFamily.Name, fromStatic.FontFamily.Name);
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
        Assert.Equal(TextFace, eleven.FontFamily.Name);
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
        Assert.Equal(MonospacedFace, styled.FontFamily.Name);
        Assert.Equal(11.0, styled.FontSize);
        Assert.Equal(400, (int)styled.FontWeight);

        // App/Views/Settings/SettingsView.swift:729: .system(size: 12, design: .monospaced)
        var sized = SwiftFont.System(size: 12, design: SwiftFont.Design.Monospaced).Resolve();
        Assert.Equal(MonospacedFace, sized.FontFamily.Name);
        Assert.Equal(12.0, sized.FontSize);
        Assert.Equal(400, (int)sized.FontWeight);

        Assert.Equal(TextFace, SwiftFont.System(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Default).Resolve().FontFamily.Name);
        Assert.Equal(TextFace, SwiftFont.System(size: 12, design: SwiftFont.Design.Default).Resolve().FontFamily.Name);
        Assert.Equal(TextFace, SwiftFont.Body.Resolve().FontFamily.Name);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void The_monospaced_design_keeps_the_style_size_and_the_given_weight()
    {
        // UI/Common/ChatFont.swift:8 passes design through Font.system(style, design:); TimelineRows.swift:2122
        // asks for .caption monospaced.
        var caption = SwiftFont.System(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Resolve();
        Assert.Equal(MonospacedFace, caption.FontFamily.Name);
        Assert.Equal(10.0, caption.FontSize);

        var semibold = SwiftFont.System(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Monospaced, weight: SwiftFont.Weight.Semibold).Resolve();
        Assert.Equal(MonospacedFace, semibold.FontFamily.Name);
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
        Assert.Equal(TextFace, badge.FontFamily.Name);
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
        Assert.Equal(TextFace, chevron.FontFamily.Name);

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
        Assert.Equal(TextFace, italic.FontFamily.Name);
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
        Assert.Equal(TextFace, bold.FontFamily.Name);

        var monospaced = SwiftFont.System(size: 12, design: SwiftFont.Design.Monospaced).Bold().Resolve();
        Assert.Equal(700, (int)monospaced.FontWeight);
        Assert.Equal(MonospacedFace, monospaced.FontFamily.Name);
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
        Assert.Equal(MonospacedFace, all.FontFamily.Name);
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
        Assert.Equal(TextFace, unchanged.FontFamily.Name);

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
        Assert.Equal(TextFace, composer.FontFamily.Name);
        Assert.Equal(FontStyle.Normal, composer.FontStyle);
        Assert.True(HasNoFeatures(composer.FontFeatures));

        // UI/Markdown/ProseRunView.swift:182: NSFont.systemFont(ofSize: size, weight: .semibold)
        var semibold = SwiftFont.SystemFont(ofSize: 13, weight: SwiftFont.Weight.Semibold).Resolve();
        Assert.Equal(13.0, semibold.FontSize);
        Assert.Equal(600, (int)semibold.FontWeight);
        Assert.Equal(TextFace, semibold.FontFamily.Name);
    }

    [Fact]
    [Trait("Decision", "ED-091")]
    public void NSFont_boldSystemFont_is_Inter_bold_and_the_italic_trait_is_the_italic_modifier()
    {
        // UI/Markdown/LinkParagraphView.swift:115: NSFont.boldSystemFont(ofSize: pointSize)
        var bold = SwiftFont.BoldSystemFont(ofSize: 13).Resolve();
        Assert.Equal(13.0, bold.FontSize);
        Assert.Equal(700, (int)bold.FontWeight);
        Assert.Equal(TextFace, bold.FontFamily.Name);
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
        Assert.Equal(MonospacedFace, inline.FontFamily.Name);
        Assert.Equal(12.0, inline.FontSize);
        Assert.Equal(400, (int)inline.FontWeight);
        Assert.Equal(FontStyle.Normal, inline.FontStyle);

        var nine = SwiftFont.MonospacedSystemFont(ofSize: 9, weight: SwiftFont.Weight.Medium).Resolve();
        Assert.Equal(9.0, nine.FontSize);
        Assert.Equal(500, (int)nine.FontWeight);
        Assert.Equal(MonospacedFace, nine.FontFamily.Name);
    }

    // ---- Apply to Avalonia controls (headless, R-TEST-7) ----

    [Fact]
    [Trait("Decision", "ED-091")]
    [Trait("Rule", "R-VIEW-11")]
    public void Apply_sets_family_size_weight_style_and_features_on_a_TextBlock() => headless.OnUiThread(() =>
    {
        var text = new TextBlock();
        SwiftFont.System(SwiftFont.TextStyle.Caption2, weight: SwiftFont.Weight.Bold).MonospacedDigit().Italic().Apply(text);
        Assert.Equal(TextFace, text.FontFamily.Name);
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
        Assert.Equal(MonospacedFace, button.FontFamily.Name);
        Assert.Equal(12.0, button.FontSize);
        Assert.Equal(400, (int)button.FontWeight);
        Assert.Equal(FontStyle.Normal, button.FontStyle);
        Assert.True(HasNoFeatures(button.FontFeatures));

        var box = new TextBox();
        SwiftFont.Headline.MonospacedDigit().Apply(box);
        Assert.Equal(TextFace, box.FontFamily.Name);
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
        Assert.Equal(TextFace, text.FontFamily.Name);
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
    public void Resolve_and_Apply_agree_for_every_text_style() => headless.OnUiThread(() =>
    {
        foreach (var style in Enum.GetValues<SwiftFont.TextStyle>())
        {
            var font = SwiftFont.System(style);
            var resolved = font.Resolve();
            var text = new TextBlock();
            font.Apply(text);
            Assert.Equal(resolved.FontSize, text.FontSize);
            Assert.Equal((int)resolved.FontWeight, (int)text.FontWeight);
            Assert.Equal(resolved.FontFamily.Name, text.FontFamily.Name);
            Assert.Equal(resolved.FontStyle, text.FontStyle);
        }
    });
}
