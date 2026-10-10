using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Port.ModelsPalette;

public sealed class PaletteEngineClockTests
{
    [Fact]
    [Trait("Decision", "ED-622")]
    public void The_models_palette_debounce_uses_the_bound_clock_and_stops_with_the_model()
    {
        using var harness = new AppModelHarness();
        using var fake = new PalettePipelineFixture();
        using var transport = URLSession.Transport.Override(fake.Http);
        var model = harness.MakeModel();
        try
        {
            harness.Main.OnMain(() => model.PaletteEngine.Start());
            Assert.Equal(1, harness.Main.Time.ActiveTimers);
            Assert.Empty(fake.Http.Received);
            harness.Main.OnMain(model.StopPaletteChecks);
            Assert.Equal(0, harness.Main.Time.ActiveTimers);
            harness.Main.AdvanceMilliseconds(250);
            Assert.Empty(fake.Http.Received);
        }
        finally { harness.Main.OnMain(model.StopPaletteChecks); }
    }
}
