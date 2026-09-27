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
    [Trait("Req", "PR-COMPOSER-129")] // EffortSlider.swift:862-871
    public void EffortBrand_Create_maps_each_provider_to_its_brand()
    {
        Assert.Equal(EffortBrand.Claude, EffortBrand.Create(ProviderKind.Claude));
        Assert.Equal(EffortBrand.Deepseek, EffortBrand.Create(ProviderKind.Deepseek));
        Assert.Equal(EffortBrand.Meta, EffortBrand.Create(ProviderKind.Meta));
        Assert.Equal(EffortBrand.Antigravity, EffortBrand.Create(ProviderKind.Antigravity));
        Assert.Equal(EffortBrand.Silver, EffortBrand.Create(ProviderKind.Codex));
        Assert.Equal(EffortBrand.Silver, EffortBrand.Create(ProviderKind.Copilot));
        Assert.Equal(EffortBrand.Purple, EffortBrand.Create(null));
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
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018
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
    [Trait("Req", "PR-COMPOSER-131")] // EffortSlider.swift:1000-1018
    public void TrackLook_with_a_pair_follows_the_pair_instead_of_its_kind()
    {
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex) { WorkerProvider = ProviderKind.Antigravity });
        var look = new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple, pair);

        Assert.Equal(Resolved(pair.Colors[^1].Opacity(0.7)), Resolved(look.Glow));
        Assert.Equal(pair.IsLight, look.IsLightFill);
        Assert.Equal(Resolved(SwiftColor.White), Resolved(look.Spark)); // :1018 a pair never sparks
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1099-1115
    public void TrackEffect_Traits_Build_scales_particle_and_streak_counts_with_width_and_floors_them()
    {
        var narrow = TrackEffect.Traits.Build(10);
        Assert.Equal(10, narrow.Particles.Count); // floor of 10, not 10/5=2
        Assert.Equal(5, narrow.Streaks.Count);    // floor of 5, not 10/14=0

        var wide = TrackEffect.Traits.Build(280);
        Assert.Equal(56, wide.Particles.Count); // 280/5
        Assert.Equal(20, wide.Streaks.Count);   // 280/14
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1099-1115
    public void TrackEffect_Traits_Build_is_deterministic_per_width()
    {
        var first = TrackEffect.Traits.Build(200);
        var second = TrackEffect.Traits.Build(200);
        Assert.Equal(first, second);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1121-1132
    public void TrackEffect_TraitCache_rebuilds_only_when_the_width_changes()
    {
        var cache = new TrackEffect.TraitCache();
        var first = cache.Traits(200);
        var same = cache.Traits(200);
        var different = cache.Traits(300);

        Assert.Same(first, same);
        Assert.NotSame(first, different);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-134")] // EffortSlider.swift:1343-1346
    public void TrackEffect_Random_is_deterministic_and_stays_in_zero_to_one()
    {
        var first = TrackEffect.Random(3, 2);
        var second = TrackEffect.Random(3, 2);
        Assert.Equal(first, second);
        Assert.InRange(first, 0, 1);
    }

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
    public void EffortPairLook_caption_names_the_heads_effort_when_the_pair_sets_one()
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
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // EffortSlider.swift:196, :496
    public void EffortPairLook_falls_back_to_the_resolved_launch_effort_when_the_pair_sets_none()
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
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // EffortSlider.swift:502
    public void EffortPairLook_caption_says_heads_keep_their_own_when_neither_is_set()
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
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-115")] // EffortSlider.swift:494-495; HydraButton.swift:648-711 HydraPairSummary
    public void EffortPairLook_title_and_brand_delegate_to_the_established_pair_helpers()
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
    }

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
    [Trait("Req", "PR-COMPOSER-119")] // EffortSlider.swift:670 .symbolEffect(.bounce, value: isOn); ED-182 defers the effect itself but requires it be recorded (MARROW_DEFERRED ED-182), per SwiftImage.cs:32's own citation of this exact call site
    public void A_fast_mode_buttons_glyph_records_the_deferred_bounce_symbol_effect_on_build_and_after_toggling() => headless.OnUiThread(() =>
    {
        var box = new ObservableBoolBox();
        var control = new FastModeButton(box.AsBinding());
        var window = Show(control);

        Assert.Equal(SymbolEffectKind.Bounce, Assert.Single(window.GetVisualDescendants().OfType<SwiftImage>()).DeferredSymbolEffect);

        box.Value = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(SymbolEffectKind.Bounce, Assert.Single(window.GetVisualDescendants().OfType<SwiftImage>()).DeferredSymbolEffect);
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

// STOP App.Views.Composer.EffortSlider #3
// Kind: missing shared UI rendering and styling dependencies (checked on sweep-A base d62329d42).
// Swift: App/Views/Composer/EffortSlider.swift:73-75,114,436-439,606-608; UI/Common/Components.swift:4-14,58-99.
// Records: PR-COMPOSER-106,107,114,143; UI-COMPOSER-012,032,043 (only the provider artwork/chip-style sites).
// Missing: UI.Common.Components.ProviderIcon has no renderer, and ChipButtonStyleHelper is an empty type.
// Needed: port ProviderIcon's bundled provider artwork and the shared chip(active:) style in their owning unit.
// Surrounding labels, actions, widths, popover, rows and subtitles are now implemented and tested in EffortSliderSweepTests.

// PR-COMPOSER-125 windows_path explicitly drops haptics silently on Windows, without a sound substitute.
// The shared Haptics.Perform no-op is the specified adaptation, not an unported dependency (port_impacts X6).
// The source Alignment call remains once per index change; drag rounding, clamping, capture and springs are tested.

// Former STOP #5 resolved by ED-213: DrawFusion uses a private Skia-lease operation with Plus save layers,
// Gaussian sigma equal to the source radius, and anti-aliased side clips. EffortSliderFusionTests covers PR-COMPOSER-138.
// Former STOP #6 resolved by ED-214: TextScaling measures before trimming, with 0.85 subtitle and 0.9 caption.
// TextScalingTests and TextScalingSitesTests exercise the primitive and both finite-width sites.
