using Marrow.App.Port.Ignis.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Ignis;

// ED-NEW-perf-idle-1: a warm Kindle capsule re-reads the clock once a second (the countdown). That re-read used to start two 0.6 s
// animation runs and make a new glow brush every second, so main woke at the display rate for most of every idle second
// of an hour. Now the clock's slow fall of the heat (0.0002 a second) lands without a run, the brush is made when the
// theme changes, and the tick stops while no window of the app can be seen. The frame clock and the clock are driven by hand.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-kindlehover-1")]
public sealed class KindleCapsuleIdleTests(HeadlessSession headless)
{
    private static IgnisChatStatus Warm(TestRuntime runtime) =>
        IgnisChatStatus.Unknown(KindleFakeController.Chat, IgnisMode.WarmAndTidy, true) with
        {
            Provider = ProviderKind.Claude, Cache = IgnisCacheState.Warm, ExpiresAt = runtime.Time.GetUtcNow() + TimeSpan.FromMinutes(42), Ttl = TimeSpan.FromHours(1),
            ContextUsed = 244_000, ContextWindow = 1_000_000, TidyAt = 400_000, CanKeepWarm = true, CanTidy = true,
        };

    [Fact]
    public void The_once_a_second_re_read_starts_no_animation_run_and_makes_no_new_glow_brush() => headless.OnUiThread(() =>
    {
        using var rig = new KindleCapsuleRig(Warm);
        var capsule = rig.Capsule;
        var brush = rig.Runtime.OnMain(() => capsule.GlowBrush);
        Assert.NotNull(brush);
        var start = rig.Runtime.OnMain(() => capsule.Glyph.Heat);
        for (var second = 1; second <= 120; second++)
        {
            rig.Runtime.OnMain(() =>
            {
                rig.Runtime.Time.Advance(TimeSpan.FromSeconds(1));
                capsule.Refresh();
            });
            rig.Step(16);
            rig.Runtime.OnMain(() =>
            {
                Assert.False(capsule.IsEasing, "second " + second + ": the re-read started a run");
                Assert.Same(brush, capsule.GlowBrush);
            });
        }
        rig.Runtime.OnMain(() =>
        {
            // two minutes on, the heat has followed the clock (to within one landing step)
            var expected = KindleCapsule.HeatOf(rig.Controller.Current, SwiftDate.Now);
            Assert.True(expected < start - .01, "the heat fell");
            Assert.InRange(Math.Abs(capsule.Glyph.Heat - expected), 0, KindleGlyph.HeatStep);
        });
        for (var i = 0; i < 30; i++) rig.Step(60); // the roll of the minute digit lands
        Assert.Equal("40m", rig.Runtime.OnMain(() => capsule.Text));
    });

    [Fact]
    public void A_change_of_the_cache_still_eases_and_a_run_in_flight_is_not_restarted_by_the_tick() => headless.OnUiThread(() =>
    {
        using var rig = new KindleCapsuleRig(Warm);
        var capsule = rig.Capsule;
        // the cache goes cold: heat .79 to 0 is a movement, so it eases (0.6 s)
        rig.Runtime.OnMain(() => rig.Controller.Set(rig.Controller.Current with { Cache = IgnisCacheState.Cold, ExpiresAt = null }));
        rig.Step(16);
        rig.Runtime.OnMain(() =>
        {
            Assert.True(capsule.IsEasing);
            Assert.InRange(capsule.Glyph.Heat, .001, .78);
        });
        var mid = rig.Runtime.OnMain(() => capsule.Glyph.Heat);
        // a re-read half way through the run changes nothing about it: the heat keeps falling on the same curve
        rig.Runtime.OnMain(capsule.Refresh);
        rig.Step(16);
        rig.Runtime.OnMain(() => Assert.True(capsule.Glyph.Heat <= mid));
        for (var i = 0; i < 20; i++) rig.Step(60);
        rig.Runtime.OnMain(() =>
        {
            Assert.False(capsule.IsEasing);
            Assert.Equal(0, capsule.Glyph.Heat);
        });
    });

    [Fact]
    public void The_tick_stops_while_no_window_can_be_seen_and_the_countdown_is_current_when_one_can() => headless.OnUiThread(() =>
    {
        WindowPresence.TestFactory = window => new WindowPresence(window, true);
        try
        {
            using var rig = new KindleCapsuleRig(Warm);
            var capsule = rig.Capsule;
            var presence = rig.Runtime.OnMain(() => WindowPresence.For(capsule));
            Assert.NotNull(presence);
            rig.Runtime.OnMain(() =>
            {
                Assert.True(capsule.IsTicking);
                Assert.Equal("42m", capsule.Text);
                presence.SetSeen(false);
                Assert.False(capsule.IsTicking);
                rig.Runtime.Time.Advance(TimeSpan.FromMinutes(10));
                capsule.Refresh(); // an event of the controller while unseen: it reads, and still does not tick
                Assert.False(capsule.IsTicking);
                presence.SetSeen(true);
                Assert.True(capsule.IsTicking);
            });
            for (var i = 0; i < 30; i++) rig.Step(60);
            Assert.Equal("32m", rig.Runtime.OnMain(() => capsule.Text));
            // a cache that ran out while unseen reads cold the moment a window can be seen
            rig.Runtime.OnMain(() =>
            {
                presence.SetSeen(false);
                rig.Runtime.Time.Advance(TimeSpan.FromMinutes(40));
                presence.SetSeen(true);
            });
            for (var i = 0; i < 30; i++) rig.Step(60);
            rig.Runtime.OnMain(() =>
            {
                Assert.Equal("Cold", capsule.Text);
                Assert.False(capsule.IsTicking);
            });
        }
        finally { WindowPresence.TestFactory = null; }
    });
}
