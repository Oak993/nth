using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PalettePollJitterTests
{
    [Fact]
    public async Task Boundary_poll_jitter_spreads_installs_within_the_locked_one_to_ten_minute_window()
    {
        using var roots=new ModelsPaletteRoots();var delays=new List<double>();
        for(var n=0;n<10;n++)
        {
            using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
            engine.Start();await engine.CheckNow();var boundaryDelay=(engine.Current.Boundary+1)*21600-engine.LastCheckedAtServerSeconds;
            var poll=fake.Time.TimerDeadlines.Max().TotalSeconds;Assert.InRange(poll-boundaryDelay,60,600);delays.Add(poll);
        }
        Assert.True(delays.Distinct().Count()>1);
    }
}
