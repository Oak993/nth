using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4 "Locked PC or owner away"; ED-NEW-dextra-as10-16): a duty that needs the desktop and meets a
// locked PC waits with "Waiting for you to unlock". An unlocked desktop passes at once. After the unlock it goes on at once if the
// owner is idle, and asks first ("Resume now?") if he is working, asking again later after a "not yet". Stop ends the wait. The gate
// never unlocks anything: it only waits.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-16")]
public sealed class DesktopGateTests
{
    private readonly ManualTimeProvider clock = new();
    private readonly FakeDesktop desktop = new();
    private readonly List<(Guid Mission, string? Line)> notes = [];
    private readonly List<string> asked = [];
    private TaskCompletionSource<bool>? answer;
    private readonly Guid mission = Guid.NewGuid();

    private DesktopGate Gate() => new(desktop, clock, (_, title, _) =>
    {
        asked.Add(title);
        answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return answer.Task;
    }, (id, line) => notes.Add((id, line)));

    // A poll of the gate is a timer; its continuation runs on the pool, so each step waits for the gate's next timer to be set before time moves.
    private void Tick(int seconds)
    {
        for (var i = 0; i < seconds; i += 5)
        {
            SpinWait.SpinUntil(() => clock.ActiveTimers > 0, TimeSpan.FromMilliseconds(500));
            clock.Advance(TimeSpan.FromSeconds(5));
        }
        Thread.Sleep(30); // the last continuation
    }

    private void AwaitAsked(int count) => Assert.True(SpinWait.SpinUntil(() => asked.Count >= count && answer is not null, TimeSpan.FromSeconds(5)), "the gate did not ask");

    [Fact]
    public async Task An_unlocked_desktop_passes_at_once_and_says_nothing()
    {
        await Gate().WaitForDesktop(mission, "Digest", CancellationToken.None);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task A_locked_desktop_waits_with_the_unlock_line_and_an_idle_owner_lets_it_go_on_after_the_unlock()
    {
        desktop.IsLocked = true;
        var gate = Gate();
        var waiting = gate.WaitForDesktop(mission, "Digest", CancellationToken.None);
        Assert.Equal((mission, DesktopGate.WaitingLine), notes[0]);
        Assert.True(gate.IsWaiting(mission));
        Tick(60);
        Assert.False(waiting.IsCompleted); // still locked
        desktop.IsLocked = false;
        desktop.IdleFor = TimeSpan.FromMinutes(5); // he unlocked and walked away
        Tick(5);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(asked);
        Assert.False(gate.IsWaiting(mission));
        Assert.Equal((mission, (string?)null), notes[^1]);
    }

    [Fact]
    public async Task An_owner_who_is_working_is_asked_first_and_yes_lets_it_go_on()
    {
        desktop.IsLocked = true;
        var gate = Gate();
        var waiting = gate.WaitForDesktop(mission, "Digest", CancellationToken.None);
        desktop.IsLocked = false;
        desktop.IdleFor = TimeSpan.FromSeconds(3);
        Tick(5);
        AwaitAsked(1);
        Assert.Equal(["Digest"], asked);
        Assert.Equal((mission, DesktopGate.AskedLine), notes[^1]);
        Assert.False(waiting.IsCompleted);
        answer!.SetResult(true);
        Tick(5);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Not_yet_waits_and_asks_again_only_after_a_while_and_going_idle_ends_the_wait_without_asking_again()
    {
        desktop.IsLocked = true;
        var gate = Gate();
        var waiting = gate.WaitForDesktop(mission, "Digest", CancellationToken.None);
        desktop.IsLocked = false;
        desktop.IdleFor = TimeSpan.FromSeconds(3);
        Tick(5);
        AwaitAsked(1);
        answer!.SetResult(false);
        Tick(60);
        Assert.Single(asked); // not asked again at once
        Tick(10 * 60);
        AwaitAsked(2);
        Assert.Equal(2, asked.Count); // asked again after ten minutes
        desktop.IdleFor = TimeSpan.FromMinutes(3);
        Tick(10);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Locking_again_while_a_question_waits_goes_back_to_waiting_for_the_unlock()
    {
        desktop.IsLocked = true;
        var gate = Gate();
        var waiting = gate.WaitForDesktop(mission, "Digest", CancellationToken.None);
        desktop.IsLocked = false;
        desktop.IdleFor = TimeSpan.Zero;
        Tick(5);
        AwaitAsked(1);
        desktop.IsLocked = true;
        answer!.SetResult(true);
        Tick(30);
        Assert.False(waiting.IsCompleted); // the answer was yes, but the PC is locked again
        desktop.IsLocked = false;
        Tick(5);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stop_ends_the_wait_and_clears_the_line()
    {
        desktop.IsLocked = true;
        var gate = Gate();
        using var stop = new CancellationTokenSource();
        var waiting = gate.WaitForDesktop(mission, "Digest", stop.Token);
        Tick(10);
        await stop.CancelAsync();
        Tick(5);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(gate.IsWaiting(mission));
        Assert.Null(notes[^1].Line);
    }

    [Fact]
    public async Task Two_missions_wait_and_go_on_separately()
    {
        desktop.IsLocked = true;
        var gate = Gate();
        var other = Guid.NewGuid();
        var first = gate.WaitForDesktop(mission, "One", CancellationToken.None);
        var second = gate.WaitForDesktop(other, "Two", CancellationToken.None);
        Assert.True(gate.IsWaiting(mission) && gate.IsWaiting(other));
        desktop.IsLocked = false;
        desktop.IdleFor = TimeSpan.FromMinutes(5);
        Tick(5);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
