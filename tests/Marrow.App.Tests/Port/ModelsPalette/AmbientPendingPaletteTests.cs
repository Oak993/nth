using Avalonia;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.ModelsPalette;

[Collection("Headless")]
[Trait("Decision", "ED-248")]
public sealed class AmbientPendingPaletteTests(HeadlessSession headless)
{
    [Fact]
    public async Task A_pending_palette_that_has_landed_on_its_quiet_fallback_does_not_exempt_idle_shutdown()
    {
        using var roots = new ModelsPaletteRoots(); using var fake = new PalettePipelineFixture();
        using var engine = new PaletteEngine(roots.New(), fake.Http, fake, fake.Time, fake.Pack);
        await engine.Observe(["unlisted/model-1"]);
        var selected = new[] { new PaletteRenderSelection("unlisted/model-1", "max") };
        var target = await engine.PrepareRender(selected, [], false, (s, c) => PaletteTrackTarget.Create(s, selected, [], false, c!));
        headless.OnUiThread(() =>
        {
            using var clock = SwiftClock.Provider.Override(fake.Time); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var effect = new TrackEffect(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple));
            EnvironmentValues.SetAccessibilityReduceMotion(effect, true);
            effect.PrepareScene(new Size(300, 34)); effect.SetPalette(target);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var first)); Assert.True(first.Palette!.Loading);
            Assert.True(effect.WantsInteractionFrames);
            fake.Time.Advance(TimeSpan.FromSeconds(3)); effect.ReadMixSignal(new KnobSignal(), 3, 0, SwiftDate.Now);
            Assert.True(((LiquidTrack.ILiveSource)effect).TryFrame(out var held)); Assert.False(held.Palette!.Water); Assert.False(held.Palette.Loading);
            fake.Time.Advance(TimeSpan.FromSeconds(30)); effect.ReadMixSignal(new KnobSignal(), 33, 0, SwiftDate.Now);
            Assert.False(effect.WantsInteractionFrames);
        });
    }
}
