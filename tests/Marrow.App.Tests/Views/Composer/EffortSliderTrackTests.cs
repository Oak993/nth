using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :711-837 the slider and :839-1027 the palette, the brands
// and the track's look. PR-COMPOSER-127 (the four looks), 128 (the fill per brand and pair), 132 (the effect's identity and clock).
[Collection("Headless")]
public sealed class EffortSliderTrackTests(HeadlessSession headless)
{
    private static readonly ThemeVariant Light = ThemeVariant.Light;

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 330, Height = 220, Content = content };
        window.Show(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Border FillOf(EffortSlider slider) => slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "EffortFill");

    private static EffortSlider Slider(int index, bool fast = false, EffortBrand brand = EffortBrand.Purple, EffortPairBrand? pair = null, int count = 3) =>
        new(count, new Binding<int>(() => index, _ => { }), "Title", fast, brand, pair);

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1 && Math.Abs(a.A - b.A) <= 1;

    private static Color Hex(string hex) => Color.Parse(hex);

    private static void AssertSolid(Color expected, IBrush? brush, string why)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.True(Near(expected, solid.Color), $"{why}: {solid.Color} is not {expected}");
    }

    private static void AssertGradient(Color[] expected, IBrush? brush, string why)
    {
        var gradient = Assert.IsType<LinearGradientBrush>(brush);
        Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), gradient.StartPoint); // .leading
        Assert.Equal(new RelativePoint(1, 0.5, RelativeUnit.Relative), gradient.EndPoint);   // .trailing
        Assert.Equal(expected.Length, gradient.GradientStops.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(Near(expected[i], gradient.GradientStops[i].Color), $"{why}: stop {i} is {gradient.GradientStops[i].Color}, not {expected[i]}");
            Assert.Equal(i / (double)(expected.Length - 1), gradient.GradientStops[i].Offset, 6);
        }
    }

    private static Color Source(double r, double g, double b) => new SwiftColor(r, g, b).Resolve(Light);

    // ---- PR-COMPOSER-127: the 2x2 mapping of maximum effort and fast mode onto the four looks ---------------------------------

    [Theory]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-127 is not claimed (STOP effortslider-fix #2): App/Views/Composer/EffortSlider.swift:729-738 look: (isMax, fastMode) -> fusion, supercharged, fast, plain; no effect for plain
    [InlineData(0, false, TrackLook.Kind.Plain)]
    [InlineData(2, false, TrackLook.Kind.Supercharged)]
    [InlineData(0, true, TrackLook.Kind.Fast)]
    [InlineData(2, true, TrackLook.Kind.Fusion)]
    [InlineData(1, true, TrackLook.Kind.Fast)]          // a middle stop is not maximum
    [InlineData(1, false, TrackLook.Kind.Plain)]
    public void The_slider_hands_the_effect_the_mapped_look_and_fills_it_per_kind(int index, bool fast, TrackLook.Kind kind) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = Slider(index, fast, EffortBrand.Claude);
        var window = Show(slider);
        try
        {
            var effects = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            if (kind == TrackLook.Kind.Plain)
            {
                Assert.Empty(effects); // the plain track draws no effect
                AssertSolid(Chrome.Accent.Resolve(slider.ActualThemeVariant), FillOf(slider).Background, "plain: Chrome.accent (:990)");
                return;
            }
            var effect = Assert.Single(effects);
            Assert.Equal(kind, effect.Look.TrackLookKind);
            Assert.Equal(EffortBrand.Claude, effect.Look.Brand);
            Assert.Null(effect.Look.Pair);
            // :991 supercharged = the brand's fill; DEVIATION OD-0073 (ED-248): fast mode is the liquid with Strike at every effort,
            // so the fill under it (:992-996 the amber fast fill, brand-to-amber for fusion) is the brand's, as at the top
            AssertSolid(Source(0.80, 0.42, 0.28), FillOf(slider).Background, $"{kind}: Claude's fill");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-127 is not claimed (STOP effortslider-fix #2): App/Views/Composer/EffortSlider.swift:799 .animation(.smooth(duration: 0.3), value: look): the fill follows a look change over 0.3 s
    public void A_fast_toggle_crossfades_the_fill_over_smooth_point_three_and_back() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var slider = Slider(0, false, EffortBrand.Purple);
        var window = Show(slider);
        try
        {
            var theme = slider.ActualThemeVariant;
            var accent = Chrome.Accent.Resolve(theme);
            var purple = Source(0.55, 0.34, 0.97); // :843 EffortPalette.superchargedFill, the brand's fill the fast look keeps (OD-0073)
            var smooth = Animation.Smooth(0.3);
            Color Along(Color from, Color to, double p) => Color.FromRgb((byte)Math.Round(from.R + (to.R - from.R) * p), (byte)Math.Round(from.G + (to.G - from.G) * p), (byte)Math.Round(from.B + (to.B - from.B) * p));
            void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
            AssertSolid(accent, FillOf(slider).Background, "plain");
            slider.UpdateLook(true, "Low", null); // fast mode on: plain becomes fast
            AssertSolid(accent, FillOf(slider).Background, "the fade starts where the fill was");
            Step(100);
            var mid = Assert.IsAssignableFrom<ISolidColorBrush>(FillOf(slider).Background).Color;
            Assert.True(Near(Along(accent, purple, smooth.Progress(0.1)), mid), $"100 ms into smooth(0.3) the fill is {mid}, not {Along(accent, purple, smooth.Progress(0.1))}");
            Step(300);
            AssertSolid(purple, FillOf(slider).Background, "settled on the brand's fill");
            slider.UpdateLook(false, "Low", null);
            Step(100);
            var back = Assert.IsAssignableFrom<ISolidColorBrush>(FillOf(slider).Background).Color;
            Assert.True(Near(Along(purple, accent, smooth.Progress(0.1)), back), $"on the way back {back}");
            Step(300);
            AssertSolid(accent, FillOf(slider).Background, "plain again");
        }
        finally { window.Close(); }
    });

    // ---- PR-COMPOSER-128: the fill per brand and per pair ------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-128 is not claimed (STOP effortslider-fix #2): App/Views/Composer/EffortSlider.swift:889-909 brand.fill, :862-871 init(provider:): the source's six brands at maximum
    [InlineData(ProviderKind.Claude, 0.80, 0.42, 0.28)]
    [InlineData(ProviderKind.Deepseek, 0.24, 0.36, 0.98)]
    [InlineData(null, 0.55, 0.34, 0.97)]
    public void A_maximum_fill_is_the_brands_solid_colour(ProviderKind? provider, double r, double g, double b) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = Slider(2, false, EffortBrand.Create(provider));
        var window = Show(slider);
        try { AssertSolid(Source(r, g, b), FillOf(slider).Background, $"{provider} at maximum"); }
        finally { window.Close(); }
    });

    [Theory]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-128 is not claimed (STOP effortslider-fix #2): App/Views/Composer/EffortSlider.swift:893-908 the source's gradients: Meta two stops, Codex's silver two stops, Antigravity's Gemini sweep
    [InlineData(ProviderKind.Meta, "0,0.39,0.88|0,0.51,0.98")]
    [InlineData(ProviderKind.Codex, "1,1,1|0.88,0.88,0.91")]
    [InlineData(ProviderKind.Antigravity, "0.26,0.52,0.96|0.61,0.45,0.80|0.85,0.40,0.44")]
    public void A_maximum_fill_can_be_a_gradient_of_the_brands_stops(ProviderKind provider, string stops) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = Slider(2, false, EffortBrand.Create(provider));
        var window = Show(slider);
        try
        {
            var expected = stops.Split('|').Select(stop => stop.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
                .Select(v => Source(v[0], v[1], v[2])).ToArray();
            AssertGradient(expected, FillOf(slider).Background, $"{provider} at maximum");
        }
        finally { window.Close(); }
    });

    // OD-0047 (locked canvas work/design/effort-liquid-approved-v15.dc.html:360-373): every other provider has its own colours;
    // the track's fill under the liquid is the canvas's three stops.
    [Theory]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-128 is not claimed (STOP effortslider-fix #2): DEVIATION OD-0047: the nine providers that shared the source's silver wear their own brand
    [InlineData(ProviderKind.Cursor, "#d93f00", "#f54e00", "#ff8040")]
    [InlineData(ProviderKind.Grok, "#030304", "#121218", "#26262e")]
    [InlineData(ProviderKind.Opencode, "#168a3a", "#25b34f", "#45dc6c")]
    [InlineData(ProviderKind.OpencodeGo, "#007ea3", "#00a4cd", "#3ccbec")]
    [InlineData(ProviderKind.Zai, "#0a1630", "#14306e", "#2f6bff")]
    [InlineData(ProviderKind.Devin, "#239377", "#3cbc9b", "#6fdcbf")]
    [InlineData(ProviderKind.Copilot, "#8534f3", "#d25ab4", "#f4a876")]
    [InlineData(ProviderKind.Commandcode, "#24157e", "#2e1b9c", "#6145dc")]
    [InlineData(ProviderKind.Pi, "#a30c25", "#c8102e", "#ec5560")]
    public void The_nine_providers_that_shared_silver_fill_with_their_own_three_stops(ProviderKind provider, string a, string b, string c) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var slider = Slider(2, false, EffortBrand.Create(provider));
        var window = Show(slider);
        try { AssertGradient([Hex(a), Hex(b), Hex(c)], FillOf(slider).Background, $"{provider} at maximum"); }
        finally { window.Close(); }
    });

    [Theory]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-128 is not claimed (STOP effortslider-fix #2): App/Views/Composer/EffortSlider.swift:985-988 a pair's fill is [lead fill] + the heads' colours, at every effort; :942-961 the heads' colours
    [InlineData(ProviderKind.Claude, ProviderKind.Claude, 5, 0)]    // heads on the lead's provider: the roster's first four persona colours
    [InlineData(ProviderKind.Claude, ProviderKind.Antigravity, 4, 0)] // heads on Antigravity: the Gemini sweep
    [InlineData(ProviderKind.Claude, ProviderKind.Codex, 3, 0)]     // silver heads: two greys
    [InlineData(ProviderKind.Claude, ProviderKind.Deepseek, 3, 0)]  // another brand: its fill and its title colour
    [InlineData(ProviderKind.Claude, ProviderKind.Claude, 5, 2)]    // the same at maximum
    [InlineData(ProviderKind.Claude, ProviderKind.Claude, 5, 1)]    // fast mode (index 1 with fast): the kind changes, the fill does not
    public void A_pairs_fill_runs_from_the_leads_fill_through_the_heads_colours_whatever_the_effort(ProviderKind lead, ProviderKind heads, int stops, int index) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var hydra = new HydraPair(lead) { WorkerProvider = heads };
        var pair = new EffortPairBrand(hydra);
        var slider = Slider(index, fast: index == 1, EffortBrand.Create(lead), pair);
        var window = Show(slider);
        try
        {
            var expected = new List<Color> { Source(0.80, 0.42, 0.28) }; // Claude's fill colour (:895): the lead's
            if (heads == lead) expected.AddRange(HydraRoster.Personas.Take(4).Select(persona => new SwiftColor(persona.Color.Red, persona.Color.Green, persona.Color.Blue, persona.Color.Alpha).Resolve(Light)));
            else if (heads == ProviderKind.Antigravity) expected.AddRange([Source(0.26, 0.52, 0.96), Source(0.61, 0.45, 0.80), Source(0.85, 0.40, 0.44)]); // :931-935 the Gemini sweep
            else if (heads == ProviderKind.Codex) expected.AddRange([new SwiftColor(white: 0.8).Resolve(Light), new SwiftColor(white: 0.95).Resolve(Light)]); // :953
            else expected.AddRange([Source(0.24, 0.36, 0.98), Source(0.49, 0.58, 1.0)]); // DeepSeek's fill (:896) and title colour (:877)
            Assert.Equal(stops, expected.Count);
            AssertGradient(expected.ToArray(), FillOf(slider).Background, $"{lead} leading {heads} at stop {index}");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-COMPOSER-128 is not claimed (STOP effortslider-fix #2): App/Views/Composer/EffortSlider.swift:990 plain is Chrome.accent and :992-996 fast and fusion keep the brand's fill (DEVIATION OD-0073) for every brand
    public void The_fast_and_fusion_fills_are_the_brands_fill_for_each_source_brand() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        foreach (var (provider, r, g, b) in new[] { (ProviderKind.Claude, 0.80, 0.42, 0.28), (ProviderKind.Deepseek, 0.24, 0.36, 0.98) })
            foreach (var (index, fast) in new[] { (0, true), (2, true) })
            {
                var slider = Slider(index, fast, EffortBrand.Create(provider));
                var window = Show(slider);
                try { AssertSolid(Source(r, g, b), FillOf(slider).Background, $"{provider} fast={fast} index={index}"); }
                finally { window.Close(); }
            }
    });

    // ---- PR-COMPOSER-132: the effect's identity and clock ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-132")] // App/Views/Composer/EffortSlider.swift:760-767,1020-1026 effectIdentity: a pair keeps its effect, and so its clock, through kind changes
    public void A_pairs_effect_keeps_its_clock_through_kind_changes() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var pair = new EffortPairBrand(new HydraPair(ProviderKind.Codex));
        var slider = Slider(0, false, EffortBrand.Claude, pair);
        var window = Show(slider);
        try
        {
            var effect = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()); // a pair draws the liquid at every effort
            void Step(TimeSpan span) { clock.Advance(span); frames.Advance(span); Dispatcher.UIThread.RunJobs(); }
            Step(TimeSpan.FromSeconds(3));
            Assert.Equal(3, effect.TimeAt(SwiftDate.Now), 3);
            // plain -> fast -> fusion -> plain, all in the same pair: the same instance, the clock never restarts
            foreach (var (index, fast) in new[] { (0, true), (2, true), (2, false), (0, false) })
            {
                slider.UpdateLook(fast, "Title", pair);
                Dispatcher.UIThread.RunJobs();
                var before = effect.TimeAt(SwiftDate.Now);
                Step(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
                Assert.Same(effect, Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()));
                Assert.Equal(before + 1.0 / 60, effect.TimeAt(SwiftDate.Now), 3);
                Assert.True(effect.TimeAt(SwiftDate.Now) >= 3, "the pair's clock has not gone back to 0");
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [Trait("Req", "PR-COMPOSER-132")] // App/Views/Composer/EffortSlider.swift:760-767,1024-1026: without a pair every look change starts the effect over (a new identity, crossfading)
    [InlineData(0, false, 2, false)]  // plain -> supercharged
    [InlineData(2, false, 2, true)]   // supercharged -> fusion
    [InlineData(0, true, 2, true)]    // fast -> fusion
    [InlineData(2, true, 1, false)]   // fusion -> plain (the effect leaves)
    [InlineData(0, true, 0, false)]   // fast -> plain
    public void Without_a_pair_a_look_change_starts_a_new_effect_from_zero_and_the_old_one_keeps_its_own_time(int from, bool fromFast, int to, bool toFast) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var index = from;
        var slider = new EffortSlider(3, new Binding<int>(() => index, _ => { }), "Title", fromFast, EffortBrand.Claude);
        var window = Show(slider);
        try
        {
            void Step(TimeSpan span) { clock.Advance(span); frames.Advance(span); Dispatcher.UIThread.RunJobs(); }
            var outgoing = slider.GetVisualDescendants().OfType<TrackEffect>().SingleOrDefault();
            Step(TimeSpan.FromSeconds(3));
            index = to; slider.UpdateLook(toFast, "Title", null);
            Dispatcher.UIThread.RunJobs();
            var effects = slider.GetVisualDescendants().OfType<TrackEffect>().ToArray();
            var toHasEffect = toFast || to == 2;
            if (toHasEffect)
            {
                var incoming = Assert.Single(effects, effect => !ReferenceEquals(effect, outgoing));
                Assert.Equal(0, incoming.TimeAt(SwiftDate.Now), 3); // the new effect starts over
                if (outgoing is not null) Assert.Equal(3, outgoing.TimeAt(SwiftDate.Now), 3); // the leaving one keeps the time it had
            }
            else if (outgoing is not null) Assert.DoesNotContain(effects, effect => !ReferenceEquals(effect, outgoing)); // nothing new: the old one fades out alone
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-132")] // App/Views/Composer/EffortSlider.swift:1024-1026: a different pair is a different identity, so the effect starts over
    public void Another_pair_is_a_new_identity_and_starts_the_effect_over() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        var first = new EffortPairBrand(new HydraPair(ProviderKind.Claude));
        var other = new EffortPairBrand(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Deepseek });
        var slider = Slider(0, false, EffortBrand.Claude, first);
        var window = Show(slider);
        try
        {
            var kept = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>());
            clock.Advance(TimeSpan.FromSeconds(3)); frames.Advance(TimeSpan.FromSeconds(3)); Dispatcher.UIThread.RunJobs();
            slider.UpdateLook(false, "Title", first); // the same pair again: nothing restarts
            Assert.Same(kept, Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>()));
            slider.UpdateLook(false, "Title", other);
            var fresh = Assert.Single(slider.GetVisualDescendants().OfType<TrackEffect>(), effect => !ReferenceEquals(effect, kept));
            Assert.Equal(0, fresh.TimeAt(SwiftDate.Now), 3);
        }
        finally { window.Close(); }
    });
}
