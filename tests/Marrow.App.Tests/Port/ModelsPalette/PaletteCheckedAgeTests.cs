using Marrow.App.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-16")]
public sealed class PaletteCheckedAgeTests
{
    [Fact]
    public async Task Opening_Colours_reports_the_actual_earlier_check_instead_of_the_page_creation_time()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        fake.Time.Advance(TimeSpan.FromMinutes(5));using var controller=new PaletteColoursController(engine,()=>0,fake.Time);
        Assert.Equal("Checked 5 minutes ago · 2 models from 2 makers",controller.View.Detail);
    }
    [Fact]
    public async Task An_automatic_check_updates_the_resting_age_even_when_the_palette_digest_does_not_change()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        using var controller=new PaletteColoursController(engine,()=>0,fake.Time);fake.Time.Advance(TimeSpan.FromMinutes(5));controller.Tick();Assert.StartsWith("Checked 5 minutes ago",controller.View.Detail,StringComparison.Ordinal);
        var digest=engine.Current.Digest;await engine.CheckNow();Assert.Equal(digest,engine.Current.Digest);controller.Tick();Assert.Equal("Checked just now · 2 models from 2 makers",controller.View.Detail);
    }
}
