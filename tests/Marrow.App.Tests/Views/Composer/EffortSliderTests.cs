using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :1-1347.
// Existing helper coverage; EffortSliderSweepTests adds the interactive views and effect draw paths.
[Collection("Headless")]
public sealed class EffortSliderTests(HeadlessSession headless)
{
    // ---- shared headless helpers (same idioms as AboutUpdateViewsSecondTests.cs / ChromeTests.cs) ----

    private static Avalonia.Controls.Window Show(Control content, double width = 400, double height = 300)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Rect BoundsIn(Control control, Visual ancestor) =>
        new(control.TranslatePoint(new Point(0, 0), ancestor)!.Value, control.Bounds.Size);

    private static Point Centre(Control control, Avalonia.Controls.Window window)
    {
        var bounds = BoundsIn(control, window);
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static void Click(Avalonia.Controls.Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Frames(ManualFrameSource frames, int count = 30)
    {
        for (var i = 0; i < count; i++)
        {
            (SwiftClock.Provider.Current as ManualTimeProvider)?.Advance(TimeSpan.FromMilliseconds(10));
            frames.Advance(TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Color Effective(IBrush? brush)
    {
        if (brush is null) return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1);
    }

    // SwiftColor is a plain class with no value equality (this file's own skeleton header documents this: "SwiftColor
    // has no value equality... compare them by reference until that is settled"), so two separately-constructed
    // instances with the same RGBA never compare equal via Assert.Equal directly; resolving to Avalonia's own Color
    // (a value-equatable struct) is the established comparison path (matches HydraFusedPairIconTests.cs).
    private static Avalonia.Media.Color Resolved(SwiftColor color) => color.Resolve(ThemeVariant.Light);

    [Fact]
    [Trait("Req", "PR-COMPOSER-129")] // EffortSlider.swift:862-871; DEVIATION OD-0047 (locked, OD-0051): one brand per provider
    public void EffortBrand_Create_maps_each_provider_to_its_own_brand()
    {
        Assert.Equal(EffortBrand.Claude, EffortBrand.Create(ProviderKind.Claude));
        Assert.Equal(EffortBrand.Deepseek, EffortBrand.Create(ProviderKind.Deepseek));
        Assert.Equal(EffortBrand.Meta, EffortBrand.Create(ProviderKind.Meta));
        Assert.Equal(EffortBrand.Antigravity, EffortBrand.Create(ProviderKind.Antigravity));
        Assert.Equal(EffortBrand.Silver, EffortBrand.Create(ProviderKind.Codex)); // OD-0047: ChatGPT keeps the silver-white
        Assert.Equal(EffortBrand.Cursor, EffortBrand.Create(ProviderKind.Cursor));
        Assert.Equal(EffortBrand.Grok, EffortBrand.Create(ProviderKind.Grok));
        Assert.Equal(EffortBrand.Opencode, EffortBrand.Create(ProviderKind.Opencode));
        Assert.Equal(EffortBrand.OpencodeGo, EffortBrand.Create(ProviderKind.OpencodeGo));
        Assert.Equal(EffortBrand.Zai, EffortBrand.Create(ProviderKind.Zai));
        Assert.Equal(EffortBrand.Devin, EffortBrand.Create(ProviderKind.Devin));
        Assert.Equal(EffortBrand.Copilot, EffortBrand.Create(ProviderKind.Copilot));
        Assert.Equal(EffortBrand.Commandcode, EffortBrand.Create(ProviderKind.Commandcode));
        Assert.Equal(EffortBrand.Pi, EffortBrand.Create(ProviderKind.Pi));
        Assert.Equal(EffortBrand.Purple, EffortBrand.Create(null));
        // Existing brands stay distinct; Local uses the unchanged no-provider fallback (ED-410).
        var brands = SwiftEnum<ProviderKind>.AllCases.Where(provider => provider != ProviderKind.Local).Select(provider => EffortBrand.Create(provider)).ToArray();
        Assert.Equal(brands.Length, brands.Distinct().Count());
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-129")] // DEVIATION OD-0047 / OD-0051: the locked palette, transcribed from the approved canvas
    [InlineData(ProviderKind.Codex, "#FFFFFF", "#EEF0F5", "#D9DBE3")]
    [InlineData(ProviderKind.Claude, "#B85A38", "#CC6B47", "#E0875F")]
    [InlineData(ProviderKind.Deepseek, "#2A45E0", "#3D5CFA", "#6A82FF")]
    [InlineData(ProviderKind.Meta, "#0054C4", "#0070EC", "#2B95FF")]
    [InlineData(ProviderKind.Antigravity, "#4285F5", "#9C73CC", "#D96670")]
    [InlineData(ProviderKind.Cursor, "#D93F00", "#F54E00", "#FF8040")]
    [InlineData(ProviderKind.Grok, "#030304", "#121218", "#26262E")]
    [InlineData(ProviderKind.Opencode, "#168A3A", "#25B34F", "#45DC6C")]
    [InlineData(ProviderKind.OpencodeGo, "#007EA3", "#00A4CD", "#3CCBEC")]
    [InlineData(ProviderKind.Zai, "#0A1630", "#14306E", "#2F6BFF")]
    [InlineData(ProviderKind.Devin, "#239377", "#3CBC9B", "#6FDCBF")]
    [InlineData(ProviderKind.Copilot, "#8534F3", "#D25AB4", "#F4A876")]
    [InlineData(ProviderKind.Commandcode, "#24157E", "#2E1B9C", "#6145DC")]
    [InlineData(ProviderKind.Pi, "#A30C25", "#C8102E", "#EC5560")]
    public void Each_provider_has_its_approved_liquid_colours(ProviderKind provider, string dark, string heart, string light)
    {
        var stops = EffortBrand.Create(provider).LiquidStops;
        Assert.Equal(new[] { dark, heart, light }.Select(Avalonia.Media.Color.Parse), stops.Select(Resolved));
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-129")] // DEVIATION OD-0047: a new brand's title colour, and its fill heart for glows and blends
    [InlineData(ProviderKind.Cursor, "#FF8A4C", "#F54E00")]
    [InlineData(ProviderKind.Grok, "#D4D6DE", "#121218")]
    [InlineData(ProviderKind.Opencode, "#5BE07F", "#25B34F")]
    [InlineData(ProviderKind.OpencodeGo, "#4FD0EE", "#00A4CD")]
    [InlineData(ProviderKind.Zai, "#6E98FF", "#14306E")]
    [InlineData(ProviderKind.Devin, "#7FE3C9", "#3CBC9B")]
    [InlineData(ProviderKind.Copilot, "#C898FD", "#D25AB4")]
    [InlineData(ProviderKind.Commandcode, "#A98BFF", "#2E1B9C")]
    [InlineData(ProviderKind.Pi, "#FF6B7A", "#C8102E")]
    public void A_new_brand_has_its_title_and_fill_colour(ProviderKind provider, string title, string fill)
    {
        var brand = EffortBrand.Create(provider);
        Assert.Equal(Avalonia.Media.Color.Parse(title), Resolved(brand.TitleColor));
        Assert.Equal(Avalonia.Media.Color.Parse(fill), Resolved(brand.FillColor));
        Assert.False(brand.IsLight);
        Assert.Equal(SwiftColor.White, brand.Spark);
        Assert.Equal(brand == EffortBrand.Grok, brand.HasOilSheen);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-129")] // EffortSlider.swift:923-928
    public void EffortBrand_Silver_is_the_only_light_brand()
    {
        Assert.True(EffortBrand.Silver.IsLight);
        Assert.False(EffortBrand.Claude.IsLight);
        Assert.Equal(SwiftColor.Black, EffortBrand.Silver.Spark);
        Assert.Equal(SwiftColor.White, EffortBrand.Claude.Spark);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-129")] // EffortSlider.swift:931-935
    public void EffortBrand_Gemini_has_three_stops()
    {
        Assert.Equal(3, EffortBrand.Gemini.Count);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961
    public void EffortPairBrand_uses_the_heads_providers_brand_when_they_run_elsewhere()
    {
        var pair = new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Claude };
        var brand = new EffortPairBrand(pair);

        Assert.Equal(2, brand.Colors.Count);
        Assert.Equal(Resolved(EffortBrand.Claude.FillColor), Resolved(brand.Colors[0]));
        Assert.Equal(Resolved(EffortBrand.Claude.TitleColor), Resolved(brand.Colors[1]));
        Assert.False(brand.IsLight);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961
    public void EffortPairBrand_uses_Gemini_colors_for_an_antigravity_heads_provider()
    {
        var pair = new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Antigravity };
        var brand = new EffortPairBrand(pair);

        Assert.Equal(EffortBrand.Gemini.Count, brand.Colors.Count);
        Assert.Equal(Resolved(EffortBrand.Gemini[0]), Resolved(brand.Colors[0]));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-130")] // EffortSlider.swift:948-961
    public void EffortPairBrand_uses_the_first_four_roster_persona_colours_when_heads_run_in_the_leads_project()
    {
        var pair = new HydraPair(ProviderKind.Codex);
        var brand = new EffortPairBrand(pair);

        Assert.Equal(4, brand.Colors.Count);
        Assert.False(brand.IsLight);
    }

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:1000-1018 TrackLook's values by kind: the stop colour and the light-fill test (drawn: the stops, the bubbles' rims) and the record's glow and spark, which nothing draws now (the glow was the knob's blurred disc, :781-786, replaced by OD-0090's Jewel orb; src reads StopColor and IsLightFill only). Part of PR-COMPOSER-131's evidence as amended for OD-0090, OD-0075 and OD-0073 by lead ruling R1 (ED-286 (6)): the plain Purple and the supercharged Silver rows of its table, the others are in EffortSliderKnobRecordsTests and PlainSilverStopsReviewTests
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018 as amended by OD-0090 (the glow is no longer drawn)
    public void TrackLook_without_a_pair_follows_its_own_kind()
    {
        var plain = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple);
        Assert.Equal(Resolved(SwiftColor.Black.Opacity(0.28)), Resolved(plain.Glow));
        Assert.False(plain.IsLightFill);
        Assert.Equal(Resolved(SwiftColor.White.Opacity(0.6)), Resolved(plain.StopColor));
        Assert.Equal(Resolved(SwiftColor.White), Resolved(plain.Spark));

        var supercharged = new TrackLook(TrackLook.Kind.Supercharged, EffortBrand.Silver);
        Assert.Equal(Resolved(EffortBrand.Silver.Glow), Resolved(supercharged.Glow));
        Assert.True(supercharged.IsLightFill);
        Assert.Equal(Resolved(SwiftColor.Black.Opacity(0.35)), Resolved(supercharged.StopColor));
    }

    [Fact]
    [Trait("Decision", "ED-252")] // EffortSlider.swift:1000-1018 TrackLook's values with a pair: the light-fill test follows the pair (drawn: the stop colour, the bubbles' rims); the record's glow follows the pair's last colour and its spark stays white (:1018), which nothing draws now (see TrackLook_without_a_pair_follows_its_own_kind). Part of PR-COMPOSER-131's evidence (the pair rule of the amended record)
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018 as amended by OD-0090 (the glow is no longer drawn)
    public void TrackLook_with_a_pair_follows_the_pair_instead_of_its_kind()
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Antigravity });
        var look = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple, pair);

        Assert.Equal(Resolved(pair.Colors[^1].Opacity(0.7)), Resolved(look.Glow));
        Assert.Equal(pair.IsLight, look.IsLightFill);
        Assert.Equal(Resolved(SwiftColor.White), Resolved(look.Spark)); // :1018 a pair never sparks
    }

    // PR-COMPOSER-134 (EffortSlider.swift:1099-1132, 1343-1346, the streak traits, their cache and their random): DEVIATION
    // OD-0073, gone with the streaks and bolts Strike replaces; EffortSliderStrikeTests.The_source_streaks_and_bolts_are_gone.

    [Fact]
    [Trait("Req", "PR-COMPOSER-140")] // EffortSlider.swift:11
    public void ModelCatalog_Entry_id_joins_provider_and_option_ids()
    {
        var option = new ModelOption("gpt-6", "GPT-6", null, null, null);
        var entry = new ModelCatalog.Entry(ProviderKind.Codex, option);
        Assert.Equal(ProviderKind.Codex.Id + "/" + option.Id, entry.Id);
    }

    // ---- EffortPairLook, :485-504 ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // EffortSlider.swift:496, :501
    public void EffortPairLook_caption_names_the_heads_effort_when_the_pair_sets_one() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            var pair = new HydraPair(ProviderKind.Codex) { WorkerEffort = "xhigh" };
            var look = new EffortPairLook(pair, registry, resolvedHeadsEffort: null);

            Assert.Equal("extra high", look.HeadsEffort); // effortTitle("xhigh") = "Extra High", lowercased
            Assert.Equal("The lead's effort · heads run at extra high effort", look.Caption);
        });
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // EffortSlider.swift:196, :496
    public void EffortPairLook_falls_back_to_the_resolved_launch_effort_when_the_pair_sets_none() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            var pair = new HydraPair(ProviderKind.Codex);
            var look = new EffortPairLook(pair, registry, resolvedHeadsEffort: "medium");

            Assert.Equal("medium", look.HeadsEffort);
            Assert.Equal("The lead's effort · heads run at medium effort", look.Caption);
        });
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // EffortSlider.swift:502
    public void EffortPairLook_caption_says_heads_keep_their_own_when_neither_is_set() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            var pair = new HydraPair(ProviderKind.Codex);
            var look = new EffortPairLook(pair, registry, resolvedHeadsEffort: null);

            Assert.Null(look.HeadsEffort);
            Assert.Equal("The lead's effort · heads keep their own", look.Caption);
        });
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // EffortSlider.swift:494-495; HydraButton.swift:648-711 HydraPairSummary
    public void EffortPairLook_title_and_brand_delegate_to_the_established_pair_helpers() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var registry = new ProviderRegistry(new AppSettings());
            var pair = new HydraPair(ProviderKind.Codex) { Name = "Night shift" };
            var look = new EffortPairLook(pair, registry, resolvedHeadsEffort: null);

            Assert.Equal("Night shift", look.Title); // HydraPairSummary.Title: pair.CustomName ?? ModelsTitle(...)
            Assert.Equal(4, look.Brand.Colors.Count); // PR-COMPOSER-130: first four roster persona colours
        });
    });

    // ---- RoundIconButton, :685-708 ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-120")] // EffortSlider.swift:699 .help(help).accessibilityLabel(Text(help))
    public void A_round_icon_buttons_help_is_both_its_tooltip_and_its_accessible_name() => headless.OnUiThread(() =>
    {
        var control = new RoundIconButton("arrow.counterclockwise", "Reset to default", () => { });
        var window = Show(control);

        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>());
        Assert.Equal("Reset to default", ToolTip.GetTip(button));
        Assert.Equal("Reset to default", AutomationProperties.GetName(button));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-120")] // EffortSlider.swift:692 RoundIconButton(... action: onReset)
    public void A_round_icon_button_runs_its_action_once_per_click() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource()); // a click also hovers, and the hover animates
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var runs = 0;
        var control = new RoundIconButton("arrow.counterclockwise", "Reset to default", () => runs++);
        var window = Show(control);

        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>());
        Click(window, Centre(button, window));

        Assert.Equal(1, runs);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-120")] // EffortSlider.swift:695 Circle().fill(isHovering ? Chrome.overlay(0.1) : Color.clear)
    public void Hovering_a_round_icon_button_reveals_a_10_percent_plate_and_leaving_clears_it() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var control = new RoundIconButton("arrow.counterclockwise", "Reset to default", () => { });
        var window = Show(control);
        var theme = window.ActualThemeVariant;
        var circle = Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());

        AssertColor(Color.FromArgb(0, 0, 0, 0), Effective(circle.Fill));

        window.MouseMove(Centre(control, window), RawInputModifiers.None);
        Frames(frames);
        AssertColor(Chrome.Overlay(0.1).Resolve(theme), Effective(circle.Fill));

        window.MouseMove(new Point(390, 290), RawInputModifiers.None);
        Frames(frames);
        AssertColor(Color.FromArgb(0, 0, 0, 0), Effective(circle.Fill));
        window.Close();
    });

    // ---- FastModeButton, :659-683 ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:664, :667 bolt vs bolt.fill, secondaryText vs yellow
    public void A_fast_mode_button_starts_as_a_plain_bolt_in_the_secondary_colour_when_off() => headless.OnUiThread(() =>
    {
        var control = new FastModeButton(new ObservableBoolBox().AsBinding());
        var window = Show(control);

        Assert.Equal("Fast mode", ToolTip.GetTip(Assert.Single(window.GetVisualDescendants().OfType<Button>())));
        Assert.Equal("Off", AutomationProperties.GetItemStatus(Assert.Single(window.GetVisualDescendants().OfType<Button>())));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:671-672 withAnimation(.spring...) { isOn.toggle() }
    public void Clicking_a_fast_mode_button_toggles_the_bound_value_and_updates_its_tooltip() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource()); // a click also hovers, and the hover animates
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var box = new ObservableBoolBox();
        var control = new FastModeButton(box.AsBinding());
        var window = Show(control);

        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>());
        Click(window, Centre(button, window));

        Assert.True(box.Value);
        Assert.Equal("Fast mode is on", ToolTip.GetTip(button)); // :679
        Assert.Equal("On", AutomationProperties.GetItemStatus(button)); // :681
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:680 .accessibilityLabel(Text("Fast mode")), fixed regardless of state
    public void A_fast_mode_buttons_accessibility_label_is_always_fast_mode() => headless.OnUiThread(() =>
    {
        var control = new FastModeButton(new ObservableBoolBox { Value = true }.AsBinding());
        var window = Show(control);

        Assert.Equal("Fast mode", AutomationProperties.GetName(Assert.Single(window.GetVisualDescendants().OfType<Button>())));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:673 Color.yellow.opacity(0.18) once on
    public void An_on_fast_mode_button_plates_itself_yellow_regardless_of_hover() => headless.OnUiThread(() =>
    {
        var control = new FastModeButton(new ObservableBoolBox { Value = true }.AsBinding());
        var window = Show(control);
        var theme = window.ActualThemeVariant;
        var circle = Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());

        AssertColor(SwiftColor.Yellow.Opacity(0.18).Resolve(theme), Effective(circle.Fill));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:670 .symbolEffect(.bounce, value: isOn): one bounce per change (SymbolBounce, measured 2-chat-use.mov 47.55-48.20 s)
    public void A_fast_mode_buttons_bolt_bounces_once_when_toggled_and_not_on_build() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var box = new ObservableBoolBox();
        var control = new FastModeButton(box.AsBinding());
        var window = Show(control);
        double Scale() => Assert.Single(window.GetVisualDescendants().OfType<SwiftImage>()).RenderTransform is ScaleTransform s ? s.ScaleX : 1;

        Assert.Equal(SymbolEffectKind.Bounce, Assert.Single(window.GetVisualDescendants().OfType<SwiftImage>()).DeferredSymbolEffect);
        clock.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, Scale()); // no bounce on the first build: the value has not changed

        box.Value = true;
        Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(Scale() > 1.2, "the bolt swells about 0.1 s after the toggle: " + Scale()); // video about 19 of 13.5 px
        clock.Advance(TimeSpan.FromMilliseconds(250)); frames.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, Scale()); // back at rest by about 0.3 s
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:678 .onHover { isHovering = $0 } carries no withAnimation, unlike RoundIconButton's :702-703
    public void Hovering_a_fast_mode_button_changes_its_plate_instantly_with_no_animation() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var control = new FastModeButton(new ObservableBoolBox().AsBinding());
        var window = Show(control);
        var theme = window.ActualThemeVariant;
        var circle = Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());

        AssertColor(Chrome.Overlay(0.06).Resolve(theme), Effective(circle.Fill));

        window.MouseMove(Centre(control, window), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs(); // no frame ticks advanced: an animated transition would still show the old colour here

        AssertColor(Chrome.Overlay(0.1).Resolve(theme), Effective(circle.Fill));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:665, :673: the isOn toggle's plate rides the toggle's own spring(response: 0.3, dampingFraction: 0.7), not a hover ease
    public void Toggling_a_fast_mode_button_animates_its_plate_with_the_toggles_spring_not_a_hover_ease() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var box = new ObservableBoolBox();
        var control = new FastModeButton(box.AsBinding());
        var window = Show(control);
        var theme = window.ActualThemeVariant;
        var circle = Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());

        Transaction.WithAnimation(Animation.Spring(response: 0.3, dampingFraction: 0.7), () => box.Value = true); // :665
        Dispatcher.UIThread.RunJobs();

        Frames(frames, count: 15); // 150ms: past Chrome.Hover's 0.1s duration, well inside the spring's ~0.47s settling time
        Assert.NotEqual(SwiftColor.Yellow.Opacity(0.18).Resolve(theme), Effective(circle.Fill)); // still mid-flight on the (slower) spring; a 0.1s ease would already be done

        Frames(frames, count: 60); // finish it off
        AssertColor(SwiftColor.Yellow.Opacity(0.18).Resolve(theme), Effective(circle.Fill));
        window.Close();
    });
}

// PR-COMPOSER-125 windows_path explicitly drops haptics silently on Windows, without a sound substitute.
// The shared Haptics.Perform no-op is the specified adaptation, not an unported dependency (port_impacts X6).
// The source Alignment call remains once per index change; drag rounding, clamping, capture and springs are tested.

// Former STOP #5: ED-213's DrawFusion is replaced by the liquid tube (DEVIATION OD-0048/OD-0051/OD-0052, no seam, plasma, sparks or
// glow); PR-COMPOSER-138's tests are EffortSliderLiquidTests (EffortSliderFusionTests holds only the pair caption's first paint now).
// Former STOP #6 resolved by ED-214: TextScaling measures before trimming, with 0.85 subtitle and 0.9 caption.
// TextScalingTests and TextScalingSitesTests exercise the primitive and both finite-width sites.
