using System.Globalization;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.ModelsPalette;

// PORT-ONLY: AC-3's automatic pipeline, 10 independent roots and seed authority; ED-617.
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class PaletteEngineTests
{
    [Fact]
    public async Task Ten_roots_histories_cultures_and_restarts_converge_without_keys_or_real_network()
    {
        using var roots=new ModelsPaletteRoots();string? digest=null;
        var before=CultureInfo.CurrentCulture;
        try
        {
            for(var i=0;i<10;i++)
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(new[]{"en-US","tr-TR","de-DE","ar-SA"}[i%4]);
                using var fake=new PalettePipelineFixture();var root=roots.New();
                using(var engine=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack))
                {
                    if(i%2==0){fake.FailHf=true;await engine.CheckNow();fake.FailHf=false;fake.Time.Advance(TimeSpan.FromMinutes(1));}
                    await engine.CheckNow();
                    var model=engine.Current.Models["acme/nova-1"];Assert.Equal(PaletteState.Ready,model.State);
                    digest??=engine.Current.Digest;Assert.Equal(digest,engine.Current.Digest);
                    Assert.All(fake.Http.Received,r=>Assert.Null(r.Header("Authorization")));
                    Assert.All(fake.Http.Received,r=>Assert.Contains(r.Url.Host,new[]{"openrouter.ai","huggingface.co","cdn-avatars.huggingface.co","t0.gstatic.com"}));
                }
                using var restarted=new PaletteEngine(root,fake.Http,fake,new Marrow.Testing.ManualTimeProvider(),fake.Pack);
                Assert.Equal(digest,restarted.Current.Digest);Assert.Equal(PaletteState.Known,restarted.Current.Models["acme/nova-1"].State);
            }
        }
        finally {CultureInfo.CurrentCulture=before;}
    }
    [Fact]
    public async Task Failure_never_locks_a_lower_source_or_overwrites_good_seed_evidence()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture {FailHf=true};
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.CheckNow();Assert.Equal(PaletteState.Pending,engine.Current.Models["acme/nova-1"].State);
        Assert.False(File.Exists(engine.SeedsPath));
        fake.FailHf=false;fake.Time.Advance(TimeSpan.FromMinutes(1));await engine.CheckNow();var digest=engine.Current.Digest;
        var bytes=File.ReadAllBytes(engine.SeedsPath);
        fake.FailModels=true;fake.FailHf=true;await engine.CheckNow();
        Assert.Equal(digest,engine.Current.Digest);Assert.Equal(bytes,File.ReadAllBytes(engine.SeedsPath));
    }
    [Fact]
    public async Task A_new_global_input_key_with_failing_source_drops_the_old_seed_to_pending()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.CheckNow();Assert.Equal(PaletteState.Ready,engine.Current.Models["acme/nova-1"].State);
        fake.Org="acme-new-org";fake.FailHf=true;fake.Date="Wed, 07 Oct 2026 12:20:00 GMT";
        fake.Time.Advance(TimeSpan.FromHours(6));await engine.CheckNow();
        Assert.Equal(PaletteState.Pending,engine.Current.Models["acme/nova-1"].State);
    }
    [Fact]
    public async Task Reverification_needs_two_distinct_server_days_and_never_two_refreshes_in_one_day()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.CheckNow();var old=engine.Current.Models["acme/nova-1"].Palette;
        fake.ImageVersion=1;fake.Date="Thu, 08 Oct 2026 06:20:00 GMT";fake.Time.Advance(TimeSpan.FromDays(1));
        await engine.CheckNow();await engine.CheckNow();Assert.Equal(old,engine.Current.Models["acme/nova-1"].Palette);
        fake.Date="Fri, 09 Oct 2026 06:20:00 GMT";fake.Time.Advance(TimeSpan.FromDays(1));await engine.CheckNow();
        Assert.NotEqual(old,engine.Current.Models["acme/nova-1"].Palette);
    }
}
