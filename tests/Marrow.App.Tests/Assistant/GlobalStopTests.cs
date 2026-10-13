using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 12.6 "One stop chord, one owner"; ED-NEW-dextra-as6-2): the one stop chord. It is registered
// whenever any work holds a lease and released when none does; a chord another app holds, or one that is not a valid chord,
// fails closed (the work does not start, and the line says so); a start that waits goes on by itself once the key works; one
// press tells every subscriber, none waits on another, and a subscriber that throws does not hide the press from the rest.
[Trait("Decision", "ED-NEW-dextra-as6-2")]
[Trait("Feature", "AS-6")]
public sealed class GlobalStopTests
{
    private static readonly PcHotkey Default = PcHotkey.StopDefault;

    private static (GlobalStop Stop, FakeStopChordHost Host, ManualTimeProvider Time) Rig(string? setting = null)
    {
        var host = new FakeStopChordHost();
        var time = new ManualTimeProvider();
        return (new GlobalStop(host, () => setting, time), host, time);
    }

    [Fact]
    public void The_chord_is_registered_when_the_first_work_starts_and_released_when_the_last_ends()
    {
        var (stop, host, _) = Rig();
        Assert.False(stop.Armed);
        Assert.Equal("Ctrl+Alt+Esc", stop.Chord);
        Assert.True(stop.TryAcquire("mission:a", out var a));
        Assert.True(stop.TryAcquire("duty:b", out var b));
        Assert.True(stop.TryAcquire("tactus:stream", out var c));
        Assert.Equal(Default, host.Registered);
        Assert.Equal(1, host.Registrations);
        Assert.Equal(["mission:a", "duty:b", "tactus:stream"], stop.Scopes);

        a!.Dispose();
        a.Dispose(); // safe to repeat
        b!.Dispose();
        Assert.True(stop.Armed);
        Assert.Equal(Default, host.Registered);
        c!.Dispose();
        Assert.False(stop.Armed);
        Assert.Null(host.Registered);
        Assert.Equal(["register Ctrl+Alt+Esc", "unregister"], host.Calls);
        // Later work registers it again.
        Assert.True(stop.TryAcquire("voice:reply", out var d));
        Assert.Equal(2, host.Registrations);
        d!.Dispose();
    }

    [Fact]
    public void A_chord_another_app_holds_fails_closed_with_the_line_and_the_tray_still_stops()
    {
        var (stop, host, _) = Rig();
        host.Taken.Add(Default);
        var statuses = new List<StopKeyStatus>();
        stop.StatusChanged += statuses.Add;

        Assert.False(stop.TryAcquire("mission:a", out var lease));
        Assert.Null(lease);
        Assert.Empty(stop.Scopes);
        var status = stop.Status;
        Assert.True(status.Taken);
        Assert.False(status.Works);
        Assert.Equal("Stop key unavailable: Ctrl+Alt+Esc is taken. Change it in Settings", status.Unavailable);
        Assert.Equal(status, statuses.Single());
        // The press path does not need a registration: the tray, the header and voice still stop.
        var heard = new List<StopPress>();
        stop.Subscribe("Dextra", heard.Add);
        stop.Press(StopSource.Tray);
        Assert.Equal([StopSource.Tray], heard.Select(p => p.Source));
    }

    [Theory]
    [InlineData("Esc")]
    [InlineData("Win+Esc")]
    [InlineData("Ctrl+Alt+K")]
    public void A_chord_that_is_not_a_valid_chord_is_never_registered(string setting)
    {
        var (stop, host, _) = Rig(setting);
        // Ctrl+Alt+K types a character on some layouts (AltGr).
        host.Types.Add(new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 'K'));
        Assert.False(stop.TryAcquire("mission:a", out _));
        Assert.Empty(host.Calls);
        Assert.False(stop.Status.Works);
        Assert.NotEqual(PcChordProblem.None, stop.Status.Problem);
        Assert.False(stop.Status.Taken);
    }

    [Fact]
    public void An_empty_or_unreadable_setting_means_the_default_and_a_good_one_is_used()
    {
        var (blank, _, _) = Rig("   ");
        Assert.Equal("Ctrl+Alt+Esc", blank.Chord);
        var (junk, junkHost, _) = Rig("not a chord");
        Assert.True(junk.TryAcquire("mission:a", out var lease));
        Assert.Equal(Default, junkHost.Registered);
        lease!.Dispose();
        var (own, ownHost, _) = Rig("ctrl+shift+f9");
        Assert.Equal("Ctrl+Shift+F9", own.Chord);
        Assert.True(own.TryAcquire("mission:a", out var held));
        Assert.Equal(new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Shift, 0x78), ownHost.Registered);
        held!.Dispose();
    }

    [Fact]
    public async Task A_start_that_waits_goes_on_by_itself_once_the_key_works_and_a_cancelled_wait_leaves_nothing()
    {
        var holder = "setting";
        var host = new FakeStopChordHost();
        var time = new ManualTimeProvider();
        var stop = new GlobalStop(host, () => holder == "setting" ? "Ctrl+Alt+Esc" : "Ctrl+Alt+F10", time);
        host.Taken.Add(Default);

        using var cancel = new CancellationTokenSource();
        var waiting = stop.AcquireAsync("mission:a");
        var cancelled = stop.AcquireAsync("mission:b", cancel.Token);
        Assert.False(waiting.IsCompleted);
        Assert.Equal(1, time.ActiveTimers);
        time.Advance(GlobalStop.RetryEvery); // still taken
        Assert.False(waiting.IsCompleted);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        // The owner changes the chord in Settings, and the waiting start tries again.
        holder = "changed";
        stop.RetryWaiters();
        var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 0x79), host.Registered);
        Assert.Equal(["mission:a"], stop.Scopes);
        Assert.Equal(0, time.ActiveTimers); // no waiter left: no retry timer
        lease.Dispose();
        Assert.Null(host.Registered);
    }

    [Fact]
    public async Task The_other_app_letting_go_is_noticed_by_the_retry_timer()
    {
        var (stop, host, time) = Rig();
        host.Taken.Add(Default);
        var waiting = stop.AcquireAsync("duty:a");
        time.Advance(GlobalStop.RetryEvery);
        Assert.False(waiting.IsCompleted);
        host.Taken.Clear();
        time.Advance(GlobalStop.RetryEvery);
        var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(Default, host.Registered);
        lease.Dispose();
    }

    [Fact]
    public void One_press_tells_every_subscriber_in_order_and_one_that_throws_does_not_hide_it_from_the_rest()
    {
        var (stop, host, _) = Rig();
        Assert.True(stop.TryAcquire("mission:a", out var lease));
        var heard = new List<string>();
        var faults = new List<string>();
        stop.Faulted += faults.Add;
        stop.Subscribe("Tactus", p => heard.Add("Tactus " + p.Number));
        stop.Subscribe("Broken", _ => throw new InvalidOperationException("no stop path"));
        var gone = stop.Subscribe("Voice", p => heard.Add("Voice " + p.Number));
        stop.Subscribe("Dextra", p => heard.Add("Dextra " + p.Source));

        host.Press();
        Assert.Equal(["Tactus 1", "Voice 1", "Dextra Chord"], heard);
        Assert.Equal(["Broken could not stop: no stop path"], faults);

        gone.Dispose();
        stop.Press(StopSource.Header);
        Assert.Equal(["Tactus 1", "Voice 1", "Dextra Chord", "Tactus 2", "Dextra Header"], heard);
        // Another id on the host is not the stop chord.
        host.Press(99);
        Assert.Equal(5, heard.Count);
        lease!.Dispose();
    }

    [Fact]
    public async Task A_press_never_waits_on_the_registration_it_runs_while_another_thread_arms_the_chord()
    {
        var host = new BlockingRegisterHost();
        var stop = new GlobalStop(host, () => null);
        var heard = 0;
        stop.Subscribe("Dextra", _ => heard++);
        var starting = Task.Run(() => stop.TryAcquire("mission:a", out _));
        Assert.True(host.InRegister.Wait(TimeSpan.FromSeconds(10)));
        // The registration is in flight on another thread, held open: the press still reaches its subscribers at once.
        stop.Press(StopSource.Tray);
        Assert.Equal(1, heard);
        host.Finish.Set();
        Assert.True(await starting.WaitAsync(TimeSpan.FromSeconds(10)));
        stop.Dispose();
    }

    [Fact]
    public void Disposing_releases_the_chord_cancels_waiters_and_forgets_subscribers()
    {
        var (stop, host, _) = Rig();
        Assert.True(stop.TryAcquire("mission:a", out _));
        var heard = 0;
        stop.Subscribe("Dextra", _ => heard++);
        stop.Dispose();
        Assert.Null(host.Registered);
        stop.Press(StopSource.Tray);
        host.Press();
        Assert.Equal(0, heard);
        Assert.False(stop.TryAcquire("mission:b", out _));
    }
}
