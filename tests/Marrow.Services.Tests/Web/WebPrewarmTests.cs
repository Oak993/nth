using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.3 pre-warm: only when it is likely to be used (the first hover of a Visus control in a session, or 3 s after
// first paint for people who opened Visus in the last 7 days), with the environment's own options, closed after 10
// minutes unused; the first live view takes over from it.
[Trait("Decision", "ED-NEW-web-2")]
[Trait("Feature", "W-7")]
public sealed class WebPrewarmTests
{
    [Fact]
    public void The_first_hover_of_a_Visus_control_prewarms_once_with_the_shared_environment_options()
    {
        using var rig = new WebRig();
        rig.Surface.NoteVisusControlHover();
        rig.Surface.NoteVisusControlHover();

        var options = Assert.Single(rig.Engine.Prewarms);
        Assert.Equal(rig.Surface.Profiles.For(WebSurfaceKind.VisusTab).EnvironmentKey, options.EnvironmentKey);
        Assert.True(rig.Engine.IsPrewarmed);
    }

    [Fact]
    public void Recent_Visus_users_get_a_prewarm_three_seconds_after_first_paint()
    {
        using var rig = new WebRig();
        rig.Surface.NoteFirstPaint(rig.Time.GetUtcNow() - TimeSpan.FromDays(6));
        rig.Time.Advance(TimeSpan.FromMilliseconds(2999));
        rig.Surface.Tick();
        Assert.Empty(rig.Engine.Prewarms);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        rig.Surface.Tick();
        Assert.Single(rig.Engine.Prewarms);
    }

    [Fact]
    public void Nobody_else_gets_a_prewarm_at_first_paint()
    {
        using var rig = new WebRig();
        rig.Surface.NoteFirstPaint(rig.Time.GetUtcNow() - TimeSpan.FromDays(8));
        rig.Surface.NoteFirstPaint(null);
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        rig.Surface.Tick();
        Assert.Empty(rig.Engine.Prewarms);
    }

    [Fact]
    public void An_unused_prewarm_closes_after_ten_minutes()
    {
        using var rig = new WebRig();
        rig.Surface.NoteVisusControlHover();
        rig.Time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        rig.Surface.Tick();
        Assert.True(rig.Engine.IsPrewarmed);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Surface.Tick();
        Assert.False(rig.Engine.IsPrewarmed);
        Assert.Equal(1, rig.Engine.PrewarmReleases);
    }

    [Fact]
    public void The_first_live_view_takes_over_from_the_prewarm()
    {
        using var rig = new WebRig();
        rig.Surface.NoteVisusControlHover();
        rig.OpenLive(shown: true);
        Assert.False(rig.Engine.IsPrewarmed);
        Assert.Equal(1, rig.Engine.PrewarmReleases);
    }

    [Fact]
    public void No_prewarm_once_a_view_is_open_or_without_a_runtime()
    {
        using var rig = new WebRig();
        rig.OpenLive();
        rig.Surface.NoteVisusControlHover();
        Assert.Empty(rig.Engine.Prewarms);

        using var missing = new WebRig(installed: null);
        missing.Surface.NoteVisusControlHover();
        Assert.Empty(missing.Engine.Prewarms);
    }
}
