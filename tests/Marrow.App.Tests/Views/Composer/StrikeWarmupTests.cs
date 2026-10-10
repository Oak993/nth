using System.Reflection;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeWarmupTests
{
    [Theory]
    [InlineData(EffortBrand.Claude,6)]
    [InlineData(EffortBrand.Silver,1)]
    [InlineData(EffortBrand.Copilot,7)]
    [Trait("Decision", "ED-252")]
    public void Warmup_draws_actual_ink_light_and_wash_passes(EffortBrand brand,int expected)
    {
        var method = typeof(StrikeWarmup).GetMethod("DrawMix",BindingFlags.NonPublic|BindingFlags.Static); Assert.NotNull(method);
        Assert.Equal(expected, Assert.IsType<int>(method.Invoke(null,[new GpuWarmContext(null,1),brand])));
    }

    [Fact]
    [Trait("Decision", "ED-252")]
    public void The_registered_strike_family_owns_and_draws_the_mix_colour_wash_and_glass_programs()
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Port.FirstRun.StrikeWarmup"); Assert.NotNull(type);
        var property = type.GetProperty("Programs", BindingFlags.Public | BindingFlags.Static); Assert.NotNull(property);
        var programs = Assert.IsAssignableFrom<IReadOnlyList<GpuProgram>>(property.GetValue(null)); Assert.NotEmpty(programs);
        Assert.All(programs, program =>
        {
            Assert.Equal("strike", program.Family); Assert.Equal(FirstRunSteps.Strike, program.Step); Assert.Contains(program, GpuWarmup.Registered);
            program.Drawing(new GpuWarmContext(null, 1));
        });
        var effect = typeof(LiquidTrack).GetField("MixEffect", BindingFlags.NonPublic | BindingFlags.Static); Assert.NotNull(effect);
        var lazy = Assert.IsType<Lazy<SKRuntimeEffect>>(effect.GetValue(null)); Assert.Contains("strikeOver", lazy.Value.Children); Assert.Contains("light", lazy.Value.Children);
        Assert.NotNull(StrikeRaster.ColourEffect.Value); Assert.NotNull(StrikeRaster.WashEffect.Value);
    }
}
