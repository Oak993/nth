using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikePlanIntegrationTests
{
    [Fact]
    [Trait("Decision","ED-248")]
    public void The_warmed_full_resolver_contact_and_knob_path_builds_at_most_one_plan_on_a_clean_reentry()
    {
        var g=new StrikeGeometry(298,34,272,24); var storm=new StrikeStorm(1274); var clock=new StrikeFastClock(); var lanes=new StrikeBubbleLanes("53,9,3,1",298,34);
        var contacts=new StrikeContacts(1274,lanes,clock); var signal=new KnobSignal(); var entry=new StrikeReentry(1274,new ScriptedStrikeDraws([0.2,0.4,0.7,0.8,0.3,0.9])); entry.BeforeFrame(0);
        for(var n=0;n<3600;n++) {var t=n/240d; var f=storm.Resolve(g,t+entry.Offset,t,1,1); contacts.Process(g,storm.Plans,f,t,entry.Offset,1); signal.ReadMix(g,storm.Plans,t+entry.Offset,t,1);}
        var jumped=false; var max=0L; var worst=0;
        for(var n=0;n<3600;n++)
        {
            var t=15+n/240d; if(n==500)entry.Request(); var offset=entry.Offset; entry.BeforeFrame(t); if(entry.Offset!=offset)jumped=true;
            var before=storm.Plans.Builds; var f=storm.Resolve(g,t+entry.Offset,t,1,1); contacts.Process(g,storm.Plans,f,t,entry.Offset,1); signal.ReadMix(g,storm.Plans,t+entry.Offset,t,1);
            var count=storm.Plans.Builds-before; if(count>max){max=count;worst=n;}
        }
        Assert.True(jumped); Assert.True(max<=1,$"SPEC D-03: frame {worst} built {max} plans through resolve/contact/knob; clean reentry is not a geometry change.");
    }
}
