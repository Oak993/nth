using System.Diagnostics;
using System.Text.Json;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeTimingTests
{
#if !DEBUG // TESTS.md Phase D: Release timings; this measurement is not a Debug acceptance test.
    [Fact]
    [Trait("Decision","ED-248")]
    public void Resolve_contacts_and_plan_builds_meet_the_source_headless_budgets_over_3600_frames()
    {
        var g=new StrikeGeometry(298,34,272,24); var storm=new StrikeStorm(1274); var lanes=new StrikeBubbleLanes("53,9,3,1",298,34);
        var clock=new StrikeFastClock(); var contacts=new StrikeContacts(1274,lanes,clock); var plans=new StrikePlans(1274);
        for(var n=0;n<3600;n++) { var t=n/60d; var f=storm.Resolve(g,88464+t,t,1,1); contacts.Process(g,storm.Plans,f,t,88464,1); plans.Get(g,10000+n); }
        var resolving=new double[3600]; var touching=new double[3600]; var building=new double[3600];
        for(var n=0;n<3600;n++)
        {
            var t=60+n/60d; var at=Stopwatch.GetTimestamp(); var f=storm.Resolve(g,88464+t,t,1,1); resolving[n]=Stopwatch.GetElapsedTime(at).TotalMilliseconds;
            at=Stopwatch.GetTimestamp(); contacts.Process(g,storm.Plans,f,t,88464,1); contacts.ResolveLight(g,f,t); touching[n]=Stopwatch.GetElapsedTime(at).TotalMilliseconds;
            at=Stopwatch.GetTimestamp(); plans.Get(g,36860+n); building[n]=Stopwatch.GetElapsedTime(at).TotalMilliseconds;
        }
        Array.Sort(resolving); Array.Sort(touching); Array.Sort(building);
#if DEBUG
        const string configuration="Debug";
#else
        const string configuration="Release";
#endif
        var report=new { configuration,frames=3600,resolveP99=resolving[3563],resolveMax=resolving[^1],contactsP99=touching[3563],contactsMax=touching[^1],planP99=building[3563],planMax=building[^1] };
        if(Environment.GetEnvironmentVariable("MARROW_STRIKE_TIMING_REPORT") is {Length:>0} path) File.WriteAllText(path,JsonSerializer.Serialize(report));
        var evidence=JsonSerializer.Serialize(report);
        Assert.True(report.resolveP99<=0.25,evidence); Assert.True(report.contactsP99<=0.15,evidence); Assert.True(report.planMax<=0.5,evidence);
    }
#endif
}
