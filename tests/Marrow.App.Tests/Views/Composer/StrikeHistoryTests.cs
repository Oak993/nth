using System.Reflection;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeHistoryTests
{
    [Fact]
    [Trait("Decision","ED-248")]
    public void After_two_hours_a_contact_reset_uses_lane_history_and_matches_a_full_restart()
    {
        var property=typeof(StrikeBubbleLanes).GetProperty("Restarts",BindingFlags.Public|BindingFlags.Instance); Assert.NotNull(property);
        var lanes=new StrikeBubbleLanes("53,9,3,1",298,34); var cold=new StrikeBubbleLanes("53,9,3,1",298,34); var clock=new StrikeFastClock(); var g=new StrikeGeometry(298,34,272,24);
        for(var t=0;t<=7200;t+=10) for(var i=0;i<lanes.Count;i++) lanes.Now(i,clock.ClockB(t),1);
        var before=Assert.IsType<int>(property.GetValue(lanes)); var warmContacts=new StrikeContacts(1274,lanes,clock); var coldContacts=new StrikeContacts(1274,cold,clock); var warm=new StrikeStorm(1274); var fresh=new StrikeStorm(1274);
        foreach(var time in new[]{7200d,7201d,7201+1/60d})
        {
            var a=warm.Resolve(g,time+88486.63,time,1,1); var b=fresh.Resolve(g,time+88486.63,time,1,1);
            warmContacts.Process(g,warm.Plans,a,time,88486.63,1); coldContacts.Process(g,fresh.Plans,b,time,88486.63,1);
            Assert.Equal(coldContacts.NewBursts.Take(coldContacts.NewCount),warmContacts.NewBursts.Take(warmContacts.NewCount));
            for(var i=0;i<lanes.Count;i++) Assert.Equal(cold.Now(i,clock.ClockB(time),1),lanes.Now(i,clock.ClockB(time),1));
        }
        Assert.Equal(before,Assert.IsType<int>(property.GetValue(lanes)));
    }
}
