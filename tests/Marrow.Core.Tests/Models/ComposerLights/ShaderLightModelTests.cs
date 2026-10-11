using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// PORT-ONLY, owner-ruled addition (OD-0139 Composer Light v2, v3 and v4; ED-NEW-lightv2-2, -4, -5, -6, -8): the shader
// lights' catalogue, settings and motion. Every light is listed with every control; each light's options, Custom colours, the
// Text and light settings and the wallpaper's dimming persist; the loop moves with the speed and the state's rate and stands
// at the light's still under Reduce Motion; the state's energy eases (no jumps); keystrokes lift and age as the page's do; the
// readability guard follows a brighter light at once and lets go of a dimmer one over 150 ms; the wallpaper's veil is off
// with no light or no wallpaper and follows the measured wallpaper.
public sealed class ShaderLightModelTests
{
    private static readonly string[] Ids =
    [
        "lumen", "fluxa", "lacus", "caustica", "borea", "quies",
        "nebula", "serica", "favilla", "unda", "argenta", "vesper", "neonis",
        "crystallum", "radiantia", "nacrea", "aurum", "stellaris", "velum", "lamina", "fulgor",
    ];

    private static ComposerLightSettings On(string id, ComposerLightSettings? s = null) =>
        (s ?? ComposerLightSettings.Default) with { Style = ComposerLightStyle.Shader, ShaderId = id };

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-4")]
    public void Every_light_is_listed_with_every_control_and_the_pages_defaults()
    {
        Assert.Equal(Ids, ShaderLightCatalog.All.Select(d => d.Id));
        Assert.DoesNotContain(ShaderLightCatalog.All, d => d.Id == "prisma"); // G was retired
        foreach (var def in ShaderLightCatalog.All)
        {
            Assert.Equal([ShaderLightControl.Pack, ShaderLightControl.Spread, ShaderLightControl.Intensity, ShaderLightControl.Speed], def.Controls);
            Assert.NotNull(ShaderLightCatalog.Pack(def.Pack));
            Assert.InRange(def.Spread, ShaderLightCatalog.SpreadSlider.Min, ShaderLightCatalog.SpreadSlider.Max);
            Assert.InRange(def.Still, 0, 1);
            Assert.True(def.Reach >= 1, def.Id);
            Assert.Equal(new ShaderLightOptions(def.Pack, def.Spread, 1, 1, ShaderLightCatalog.CustomSeed(def.Pack)), def.Defaults);
        }
        // the v2 page's lights, as locked
        Assert.Equal(("spectrum", 130.0, .13), (ShaderLightCatalog.Find("lumen")!.Pack, ShaderLightCatalog.Find("lumen")!.Spread, ShaderLightCatalog.Find("lumen")!.Still));
        Assert.Equal(("accent", 75.0, .18), (ShaderLightCatalog.Find("quies")!.Pack, ShaderLightCatalog.Find("quies")!.Spread, ShaderLightCatalog.Find("quies")!.Still));
        Assert.Equal(15, ShaderLightCatalog.Packs.Count);
        Assert.Equal(["#3DF5A6", "#1FC8D0", "#7C6BFF", "#A6FF9E"], ShaderLightCatalog.Pack("aurora")!.Colours!); // v3's and v4's PACKS
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-4")]
    public void Each_lights_options_custom_colours_and_the_text_and_dimming_settings_persist()
    {
        var custom = new ShaderLightColours("#112233", "#445566", "#778899", "#AABBCC");
        var s = On("velum") with
        {
            Shaders = ShaderLightOptionsSet.Empty
                .With("velum", new ShaderLightOptions("custom", 180, 1.4, .5, custom))
                .With("lumen", new ShaderLightOptions("gold", 60, .8, 2.5, ShaderLightCatalog.CustomSeed("spectrum"))),
            TextMode = ComposerLightTextMode.Difference, TextBehind = .7, TextHalo = .2,
            WallpaperDim = WallpaperDimMode.Off, WallpaperDimStrength = .9,
        };
        var back = ComposerLightSettings.FromJson(s.ToJson());
        Assert.Equal(s, back);
        Assert.Equal(custom, back.ShaderOptions("velum").Custom);
        Assert.Equal("velum", back.ActiveShader!.Id);
        // every light picked persists as itself; another style keeps the light picked
        foreach (var id in Ids) Assert.Equal(id, ComposerLightSettings.FromJson(On(id).ToJson()).ActiveShader?.Id);
        var beam = ComposerLightSettings.FromJson((On("aurum") with { Style = ComposerLightStyle.Beam }).ToJson());
        Assert.Equal((ComposerLightStyle.Beam, "aurum"), (beam.Style, beam.ShaderId));
        // the shipped values: Border beam, Text above light at 35 % and 60 %, the wallpaper dimmed on Auto at 50 %
        var d = ComposerLightSettings.Default;
        Assert.Equal((ComposerLightStyle.Beam, ComposerLightTextMode.TextAbove, .35, .6, WallpaperDimMode.Auto, .5),
            (d.Style, d.TextMode, d.TextBehind, d.TextHalo, d.WallpaperDim, d.WallpaperDimStrength));
        Assert.True(d.TextAndDimAtDefaults);
        Assert.True(s.WithTextAndDimDefaults().TextAndDimAtDefaults);
        // out of range and unknown values are clamped or fall back
        var odd = ComposerLightSettings.FromJson("{\"style\":\"nebula\",\"text\":{\"mode\":\"sideways\",\"behind\":7,\"halo\":-1},\"wallDim\":{\"mode\":\"never\",\"strength\":2}}"u8);
        Assert.Equal((ComposerLightTextMode.TextAbove, 1.0, 0.0, WallpaperDimMode.Auto, 1.0), (odd.TextMode, odd.TextBehind, odd.TextHalo, odd.WallpaperDim, odd.WallpaperDimStrength));
    }

    private static ShaderLightAnimator Animate(ComposerLightSettings s, bool reduce = false, ComposerLightPhase phase = ComposerLightPhase.Rest) =>
        new(s, phase, dark: true, reduce, ComposerLightPalette.Default);

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-2")]
    public void The_loop_moves_with_the_speed_and_the_rate_and_reduce_motion_holds_the_still()
    {
        foreach (var id in new[] { "lumen", "unda", "fulgor" })
        {
            var def = ShaderLightCatalog.Find(id)!;
            var slow = Animate(On(id));
            var fast = Animate(On(id) with { Shaders = ShaderLightOptionsSet.Empty.With(id, def.Defaults with { Speed = 3 }) });
            for (var i = 1; i <= 60; i++) { slow.Advance(1 / 60.0, 1, i / 60.0); fast.Advance(1 / 60.0, 1, i / 60.0); }
            double Moved(ShaderLightAnimator a) => (a.LoopOf(id) - def.Still + 1) % 1;
            Assert.Equal(.55 / 24, Moved(slow), 1e-9);  // a second at rest: rate .55 of a 24 s loop
            Assert.Equal(3 * .55 / 24, Moved(fast), 1e-9);
            var still = Animate(On(id), reduce: true, phase: ComposerLightPhase.Running);
            for (var i = 1; i <= 120; i++) still.Advance(1 / 60.0, 1, i / 60.0);
            Assert.Equal(def.Still, still.LoopOf(id), 9);
            still.Keystroke(100);
            Assert.Equal(0, still.KeyLift);
            Assert.Equal((1.0, 1.0, 1.9), (still.Energy, still.Work, still.Rate)); // the page's settle(): Working's values at once
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-2")]
    public void The_state_eases_with_no_jumps_and_keystrokes_lift_and_age_as_the_pages_do()
    {
        var a = Animate(On("nebula"));
        a.Retarget(On("nebula"), ComposerLightPhase.Running, true, ComposerLightPalette.Default, false, 0);
        double lastE = a.Energy, lastR = a.Rate;
        for (var i = 1; i <= 180; i++)
        {
            a.Advance(1 / 60.0, 1, i / 60.0);
            Assert.InRange(a.Energy - lastE, 0, .04);  // .45 s easing: at most 1/27 of the way a frame
            Assert.InRange(a.Rate - lastR, 0, .035); // .7 s: at most 1/42 of 1.35 a frame
            (lastE, lastR) = (a.Energy, a.Rate);
        }
        Assert.Equal(1, a.Energy, 2);
        a.Keystroke(80);
        Assert.Equal(.75, a.KeyLift, 9);
        a.Keystroke(80);
        Assert.Equal(1, a.KeyLift, 9);           // capped at 1
        foreach (var step in new[] { .05, .05, .05, .01 }) a.Advance(step, 1, 4);
        Assert.Equal(Math.Exp(-1), a.KeyLift, 6); // decays over .16 s
        // a frame's step is capped at 50 ms (the page's dt)
        var b = Animate(On("nebula"), phase: ComposerLightPhase.Running);
        b.Advance(5, 1, 5);
        b.Produce(5, 400, 46, 23);
        Assert.Equal((ShaderLightCatalog.Find("nebula")!.Still + 1.9 * .05 / 24) % 1, b.Drawn[0].Loop, 6);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-8")]
    public void The_readability_guard_follows_a_brighter_light_at_once_and_lets_go_over_150_ms()
    {
        var a = Animate(On("nebula"), phase: ComposerLightPhase.Running);
        double Lift(double now) { a.Produce(now, 400, 46, 23); return a.Drawn[0].GuardLift; }
        double Bright() => (.38 + .82 * a.Energy) * (1 + .3 * a.KeyLift); // the light's brightness over its (steady) intensity
        a.Advance(1 / 60.0, 1, 1 / 60.0);
        Assert.Equal(1, Lift(1 / 60.0), 9);          // a steady light: nothing held
        var held = Bright();
        a.Keystroke(80);
        Assert.Equal(1, Lift(1 / 60.0), 9);          // a brighter light: the guard reads it at once (no lag behind it)
        double last = 1, most = 1;
        for (var i = 2; i <= 91; i++)
        {
            a.Advance(1 / 60.0, 1, i / 60.0);
            var b = Bright();
            held = b >= held ? b : b + (held - b) * Math.Exp(-1 / 60.0 / ShaderLightAnimator.GuardSeconds);
            var lift = Lift(i / 60.0);
            Assert.Equal(held / b, lift, 6);         // as the keystroke's lift fades, the guard lets go over .15 s
            Assert.InRange(lift - last, -.02, .02);  // no jumps from frame to frame
            (last, most) = (lift, Math.Max(most, lift));
        }
        Assert.Equal(.15, ShaderLightAnimator.GuardSeconds);
        Assert.InRange(most, 1.03, 1.1);             // a hold while the light dims ...
        Assert.InRange(last, 1, 1.001);              // ... and none once it is steady again
        var still = Animate(On("nebula"), reduce: true, phase: ComposerLightPhase.Running);
        still.Keystroke(80);
        still.Advance(1 / 60.0, 1, 1 / 60.0);
        still.Produce(1 / 60.0, 400, 46, 23);
        Assert.Equal(1, still.Drawn[0].GuardLift);  // Reduce Motion: a still light, nothing held
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-2")]
    public void Pulses_are_born_at_a_state_change_and_at_keystrokes_and_settle_under_reduce_motion()
    {
        var a = Animate(On("unda"));
        a.Produce(0, 400, 46, 23);
        Assert.Equal([99f, 99f, 99f, 99f], a.Drawn[0].Pulse);
        a.Retarget(On("unda"), ComposerLightPhase.Typing, true, ComposerLightPalette.Default, false, 0);
        a.Produce(0, 400, 46, 23);
        Assert.Equal((0f, 1f), (a.Drawn[0].Pulse[0], a.Drawn[0].PulseAmp[0]));
        a.Keystroke(10);                          // the last ring is younger than .65 s: none
        a.Produce(0, 400, 46, 23);
        Assert.Equal(1f, a.Drawn[0].PulseAmp[0]);
        a.Advance(.05, 1, .05);
        for (var i = 0; i < 14; i++) a.Advance(.05, 1, .1 + i * .05);
        a.Keystroke(10);                          // .75 s on: a ring of .3
        a.Produce(1, 400, 46, 23);
        Assert.Equal((0f, .3f, 1f), (a.Drawn[0].Pulse[0], a.Drawn[0].PulseAmp[0], a.Drawn[0].PulseAmp[1]));
        var still = Animate(On("unda"), reduce: true);
        still.Produce(0, 400, 46, 23);
        Assert.Equal([.55f, 1.25f, 1.95f, 99f], still.Drawn[0].Pulse);
        Assert.Equal([1f, .7f, .5f, 0f], still.Drawn[0].PulseAmp);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-5")]
    public void The_text_modes_cross_over_and_their_sliders_transition()
    {
        var a = Animate(On("lumen"));
        a.Produce(0, 400, 46, 23);
        Assert.Equal([1f, 0f, 0f], a.Drawn[0].Modes);
        Assert.Equal((.35, .6), (a.Drawn[0].Behind, a.Drawn[0].Halo));
        a.Retarget(On("lumen") with { TextMode = ComposerLightTextMode.Difference, TextHalo = 1 }, ComposerLightPhase.Rest, true, ComposerLightPalette.Default, false, 0);
        a.Produce(.1, 400, 46, 23);
        Assert.InRange(a.Drawn[0].Modes[2], .3f, .99f);       // halfway over
        Assert.Equal(1, a.Drawn[0].Modes.Sum(), 5);
        Assert.InRange(a.Drawn[0].Halo, .6, .99);
        a.Produce(1, 400, 46, 23);
        Assert.Equal([0f, 0f, 1f], a.Drawn[0].Modes);
        Assert.Equal(1, a.Drawn[0].Halo, 9);
        var still = Animate(On("lumen"), reduce: true);
        still.Retarget(On("lumen") with { TextMode = ComposerLightTextMode.LightAbove }, ComposerLightPhase.Rest, true, ComposerLightPalette.Default, true, 0);
        still.Produce(0, 400, 46, 23);
        Assert.Equal([0f, 1f, 0f], still.Drawn[0].Modes); // reduce motion: at once
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-6")]
    public void The_wallpaper_veil_is_off_with_no_light_no_wallpaper_or_the_setting_off()
    {
        var wallpaper = new WallpaperState(Showing: true);
        Assert.Equal(.3, WallpaperDim.Target(On("lumen"), wallpaper, .3), 9);
        Assert.Equal(.3, WallpaperDim.Target(ComposerLightSettings.Default, wallpaper, .3), 9); // Border beam is a light too
        Assert.Equal(0, WallpaperDim.Target(ComposerLightSettings.Default with { Style = ComposerLightStyle.Off }, wallpaper, .3));
        Assert.Equal(0, WallpaperDim.Target(On("lumen"), default, .3));
        Assert.Equal(0, WallpaperDim.Target(On("lumen") with { WallpaperDim = WallpaperDimMode.Off }, wallpaper, .3));
        // the strength scales the Auto amount (50 % is the amount), up to the most it darkens
        Assert.Equal(.6, WallpaperDim.Target(On("lumen") with { WallpaperDimStrength = 1 }, wallpaper, .3), 9);
        Assert.Equal(0, WallpaperDim.Target(On("lumen") with { WallpaperDimStrength = 0 }, wallpaper, .3));
        Assert.Equal(WallpaperDim.Most, WallpaperDim.Target(On("lumen") with { WallpaperDimStrength = 1 }, wallpaper, .6), 9);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lightv2-6")]
    public void A_bright_busy_wallpaper_is_dimmed_more_than_a_dark_calm_one()
    {
        const int w = WallpaperDim.GridWidth, h = WallpaperDim.GridHeight;
        var bright = new byte[w * h * 4];
        var dark = new byte[w * h * 4];
        var random = new Random(3);
        for (var i = 0; i < w * h; i++)
        {
            bright[i * 4] = (byte)random.Next(150, 256); bright[i * 4 + 1] = (byte)random.Next(150, 256); bright[i * 4 + 2] = (byte)random.Next(80, 256); bright[i * 4 + 3] = 255;
            dark[i * 4] = 18; dark[i * 4 + 1] = 20; dark[i * 4 + 2] = 26; dark[i * 4 + 3] = 255;
        }
        var hi = WallpaperDim.Measure(bright, w, h);
        var lo = WallpaperDim.Measure(dark, w, h);
        Assert.True(hi.Luminance > .3 && hi.Busyness > .05, $"{hi}");
        Assert.True(lo.Luminance < .02 && lo.Busyness == 0, $"{lo}");
        Assert.True(WallpaperDim.Auto(hi.Luminance, hi.Busyness) > .5);
        Assert.Equal(.08, WallpaperDim.Auto(lo.Luminance, lo.Busyness), 2);
        // a bright but calm one sits between
        var calm = Enumerable.Range(0, w * h).SelectMany(_ => new byte[] { 220, 220, 220, 255 }).ToArray();
        var c = WallpaperDim.Measure(calm, w, h);
        Assert.InRange(WallpaperDim.Auto(c.Luminance, c.Busyness), WallpaperDim.Auto(lo.Luminance, lo.Busyness) + .1, WallpaperDim.Auto(hi.Luminance, hi.Busyness) - .1);
    }
}
