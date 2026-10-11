using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0141, OD-0144, ED-NEW-pcctl-8; DESIGN.md 4.4; critique C11). A stop covers everything,
// one app or one chat; it marks every call it covers stopped and cancels its token before it returns, and never runs or
// waits on a call's own code (a call stuck in a hung app is left behind). The stop key, Settings and sleep stop everything.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcRunControlTests
{
    [Fact]
    public void A_stop_of_everything_cancels_every_call_and_says_where_it_came_from()
    {
        var control = new PcRunControl();
        using var one = control.Watch(CancellationToken.None, "chat-1", "notepad.exe", 0x1a);
        using var two = control.Watch(CancellationToken.None, "chat-2", "excel.exe", 0x2b);
        Assert.False(one.Stopped);
        Assert.Null(one.StoppedBy);
        control.Stop(PcScope.All, PcStopSource.Pill);
        Assert.True(one.Token.IsCancellationRequested);
        Assert.True(two.Token.IsCancellationRequested);
        Assert.True(one.Stopped);
        Assert.Equal(PcStopSource.Pill, one.StoppedBy);
        Assert.Equal(PcStopSource.Pill, two.StoppedBy);
    }

    [Fact]
    public void A_per_app_stop_and_a_chat_stop_cover_only_their_own_calls()
    {
        var control = new PcRunControl();
        using var notepad = control.Watch(CancellationToken.None, "chat-1", "notepad.exe", 0x1a);
        using var excel = control.Watch(CancellationToken.None, "chat-1", "excel.exe", 0x2b);
        using var head = control.Watch(CancellationToken.None, "head-2", "paint.exe", 0x3c);
        control.Stop(PcScope.App("excel.exe"), PcStopSource.Pill);
        Assert.False(notepad.Stopped);
        Assert.True(excel.Stopped);
        Assert.False(head.Stopped);
        control.Stop(PcScope.Thread("head-2"), PcStopSource.Chat);
        Assert.False(notepad.Token.IsCancellationRequested);
        Assert.True(head.Token.IsCancellationRequested);
        Assert.Equal(PcStopSource.Chat, head.StoppedBy);
    }

    [Fact]
    public void Scopes_cover_what_they_name()
    {
        Assert.True(PcScope.All.Covers("chat-1", "a.exe"));
        Assert.True(PcScope.App("a.exe").Covers("chat-2", "a.exe"));
        Assert.False(PcScope.App("a.exe").Covers("chat-1", "b.exe"));
        Assert.True(PcScope.Thread("chat-1").Covers("chat-1", "b.exe"));
        Assert.False(PcScope.Thread("chat-1").Covers("chat-2", "b.exe"));
    }

    [Fact]
    public void The_stop_key_settings_and_sleep_stop_everything_and_raise_the_event()
    {
        var control = new PcRunControl();
        var seen = new List<(PcScope Scope, PcStopSource Source)>();
        control.Stopped += (scope, source) => seen.Add((scope, source));
        control.StopKey();
        control.SwitchedOff();
        control.Sleeping();
        control.Stop(PcScope.App("a.exe"), PcStopSource.Pill);
        Assert.Equal(4, seen.Count);
        Assert.Equal((PcScope.All, PcStopSource.StopKey), seen[0]);
        Assert.Equal((PcScope.All, PcStopSource.Settings), seen[1]);
        Assert.Equal((PcScope.All, PcStopSource.Sleep), seen[2]);
        Assert.Equal((PcScope.App("a.exe"), PcStopSource.Pill), seen[3]);
    }

    [Fact]
    public async Task A_stop_never_runs_a_calls_own_code_on_the_stopping_thread()
    {
        var control = new PcRunControl();
        using var watch = control.Watch(CancellationToken.None, "chat-1", "hung.exe", 0x1a);
        var ran = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var registration = watch.Token.Register(() =>
        {
            ran.TrySetResult(Environment.CurrentManagedThreadId);
            // A call stuck in a hung app: its own code blocks.
            release.Wait(TimeSpan.FromSeconds(5));
        });
        var stopping = Environment.CurrentManagedThreadId;
        control.Stop(PcScope.All, PcStopSource.StopKey);
        // Stop returned while the call's code is still blocked, and the token was already cancelled.
        Assert.True(watch.Token.IsCancellationRequested);
        Assert.NotEqual(stopping, await ran.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        release.Set();
    }

    [Fact]
    public void The_outer_token_cancels_the_call_without_counting_as_a_stop()
    {
        var control = new PcRunControl();
        using var outer = new CancellationTokenSource();
        using var watch = control.Watch(outer.Token, "chat-1", "a.exe", 0x1a);
        outer.Cancel();
        Assert.True(watch.Token.IsCancellationRequested);
        Assert.False(watch.Stopped);
        Assert.Null(watch.StoppedBy);
    }

    [Fact]
    public void Active_windows_list_the_running_calls_and_a_finished_call_leaves_the_list()
    {
        var control = new PcRunControl();
        var one = control.Watch(CancellationToken.None, "chat-1", "a.exe", 0x1a);
        using var two = control.Watch(CancellationToken.None, "chat-1", "a.exe", 0x1a);
        using var three = control.Watch(CancellationToken.None, "chat-2", "b.exe", 0x2b);
        Assert.Equal(2, control.ActiveWindows.Count);
        Assert.Contains((nint)0x2b, control.ActiveWindows);
        one.Dispose();
        three.Dispose();
        Assert.Equal(new nint[] { 0x1a }, control.ActiveWindows.ToArray());
        // A stop after a call ended does not touch it.
        control.Stop(PcScope.All, PcStopSource.Pill);
        Assert.False(one.Stopped);
        Assert.True(two.Stopped);
    }
}
