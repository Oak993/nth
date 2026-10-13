using Marrow.App.Tests.Fixtures;
using Marrow.App.Tray;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 7.4 "One tray icon for nth", 12.6, 16.1 AppTray; ED-NEW-dextra-as6-10): one tray icon for nth,
// with items contributed by the features. It shows while the window is hidden, always if the owner sets it, or while a contributor
// is live; it wears the busiest state; items run the latest action behind their id; and Stop belongs to a service, so it works
// with the window closed. The icon itself is behind a stand-in here; the Native tests cover the real one.
[Trait("Decision", "ED-NEW-dextra-as6-10")]
[Trait("Feature", "AS-6")]
public sealed class AppTrayTests
{
    [Fact]
    public void The_icon_shows_while_the_window_is_hidden_and_goes_when_it_comes_back()
    {
        var host = new FakeTrayHost();
        var hidden = false;
        var tray = new AppTray(host, () => hidden, open: () => { });
        using var ignored = tray.Contribute("Assistant", () => new TrayContribution([new TrayItem("a.stop", "Stop Assistant", () => { })]));
        Assert.False(tray.Visible);
        Assert.Empty(host.Shown);

        hidden = true;
        tray.Refresh();
        Assert.True(tray.Visible);
        Assert.Equal("Open nth|Stop Assistant", host.Menu());
        Assert.Equal(TrayState.Ready, host.Last!.State);

        hidden = false;
        tray.Refresh();
        Assert.False(tray.Visible);
        Assert.Equal(1, host.Hides);
        tray.Refresh(); // nothing changed: no second call
        Assert.Equal(1, host.Hides);
        Assert.Single(host.Shown);
    }

    [Fact]
    public void The_owners_setting_shows_it_always_and_a_live_contributor_shows_it_too()
    {
        var host = new FakeTrayHost();
        var always = false;
        var live = false;
        var tray = new AppTray(host, () => false, () => always);
        tray.Contribute("Tactus", () => new TrayContribution([new TrayItem("t.stop", "Stop Tactus", () => { })], Live: live, State: TrayState.Working));
        Assert.False(tray.Visible);
        live = true;
        tray.Refresh();
        Assert.True(tray.Visible);
        Assert.Equal(TrayState.Working, host.Last!.State);
        live = false;
        tray.Refresh();
        Assert.False(tray.Visible);
        always = true;
        tray.Refresh();
        Assert.True(tray.Visible);
        Assert.Equal("Stop Tactus", host.Menu());
    }

    [Fact]
    public void Every_feature_adds_its_own_group_in_the_order_it_came_and_taking_it_away_removes_it()
    {
        var host = new FakeTrayHost();
        var tray = new AppTray(host, () => true, open: () => { });
        var assistant = tray.Contribute("Assistant", () => new TrayContribution([
            new TrayItem("a.stop", "Stop Assistant", () => { }),
            new TrayItem("a.pause", "Pause all", () => { }),
        ]));
        var tactus = tray.Contribute("Tactus", () => new TrayContribution([new TrayItem("t.pause", "Pause Tactus", () => { }), new TrayItem("t.stop", "Stop Tactus", () => { })]));
        tray.Contribute("Empty", () => new TrayContribution([]));
        Assert.Equal(["nth", "Assistant", "Tactus"], host.Last!.Groups.Select(g => g.Section));
        Assert.Equal("Open nth|Stop Assistant|Pause all|Pause Tactus|Stop Tactus", host.Menu());

        tactus.Dispose();
        tactus.Dispose(); // safe to repeat
        Assert.Equal("Open nth|Stop Assistant|Pause all", host.Menu());
        assistant.Dispose();
        Assert.Equal("Open nth", host.Menu());
    }

    [Fact]
    public void The_icon_wears_the_busiest_state_needs_you_then_working_then_paused()
    {
        var host = new FakeTrayHost();
        var a = TrayState.Paused;
        var b = TrayState.Ready;
        var tray = new AppTray(host, () => true);
        tray.Contribute("A", () => new TrayContribution([new TrayItem("a", "A", () => { })], State: a));
        tray.Contribute("B", () => new TrayContribution([new TrayItem("b", "B", () => { })], State: b));
        Assert.Equal(TrayState.Paused, host.Last!.State);
        b = TrayState.Working;
        tray.Refresh();
        Assert.Equal(TrayState.Working, host.Last!.State);
        a = TrayState.NeedsYou;
        tray.Refresh();
        Assert.Equal(TrayState.NeedsYou, host.Last!.State);
        a = TrayState.Ready;
        b = TrayState.Ready;
        tray.Refresh();
        Assert.Equal(TrayState.Ready, host.Last!.State);
    }

    [Fact]
    public void A_label_that_flips_reaches_the_icon_and_a_click_runs_the_latest_action_behind_its_id()
    {
        var host = new FakeTrayHost();
        var paused = false;
        var runs = new List<string>();
        var tray = new AppTray(host, () => true);
        tray.Contribute("Assistant", () => new TrayContribution([
            paused
                ? new TrayItem("a.toggle", "Resume all", () => runs.Add("resume"))
                : new TrayItem("a.toggle", "Pause all", () => runs.Add("pause")),
            new TrayItem("a.off", "Pause duties for 1 hour", () => runs.Add("never"), Enabled: false),
        ]));
        Assert.Equal("Pause all|Pause duties for 1 hour (off)", host.Menu());
        var before = host.Shown.Count;
        // The model the icon already holds still runs the current action after the label flips.
        var stale = host.Last!.Groups[0].Items[0];
        paused = true;
        tray.Refresh();
        Assert.Equal(before + 1, host.Shown.Count);
        Assert.Equal("Resume all|Pause duties for 1 hour (off)", host.Menu());
        stale.Run();
        host.Click("a.toggle");
        Assert.Equal(["resume", "resume"], runs);
        Assert.True(tray.Invoke("a.toggle"));
        Assert.False(tray.Invoke("nothing.here"));
    }

    [Fact]
    public void Stop_runs_with_the_window_closed_because_the_item_belongs_to_a_service_and_a_failing_item_is_reported()
    {
        var host = new FakeTrayHost();
        var stops = 0;
        var faults = new List<string>();
        var tray = new AppTray(host, () => true);
        tray.Faulted += faults.Add;
        tray.Contribute("Assistant", () => new TrayContribution([new TrayItem("a.stop", "Stop Assistant", () => stops++)], Live: true));
        tray.Contribute("Broken", () => throw new InvalidOperationException("no items today"));
        tray.Contribute("Fails", () => new TrayContribution([new TrayItem("f.run", "Fails", () => throw new InvalidOperationException("boom"))]));
        Assert.Contains(faults, f => f == "Broken could not add its items: no items today");
        host.Click("a.stop");
        Assert.Equal(1, stops);
        host.Click("f.run");
        Assert.Contains("f.run failed: boom", faults);
        Assert.Equal(1, stops);
        // The window never took part: nothing in the tray asked it for anything.
        Assert.Equal("Stop Assistant|Fails", host.Menu());
    }

    [Fact]
    public void Disposing_the_tray_hides_nothing_twice_and_lets_the_icon_go()
    {
        var host = new FakeTrayHost();
        var tray = new AppTray(host, () => true);
        tray.Contribute("Assistant", () => new TrayContribution([new TrayItem("a", "A", () => { })]));
        tray.Dispose();
        Assert.True(host.Disposed);
        Assert.False(tray.Visible);
        tray.Refresh();
        Assert.Single(host.Shown);
    }
}
