using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a and 13 unit U4, VELOPACK_CHECK.md "Old branches" items 2 and 3
// (ED-NEW-inst-u4-3; amends ED-247). The hold a setup keeps while Velopack's Setup.exe runs: a second setup for the same root
// learns it is not alone (silent exit 4), an nth starting in that root waits for it instead of opening a window Velopack
// would kill, and a setup that dies frees the wait. The roots are random temporary paths, so no test touches a real install.
public sealed class InstallHoldTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "nth-hold-test-" + Guid.NewGuid().ToString("N"), "Programs", "nth");

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_name_follows_the_root_whatever_its_case_or_trailing_separator()
    {
        var root = NewRoot();

        Assert.Equal(InstallHold.MutexName(root), InstallHold.MutexName(root + Path.DirectorySeparatorChar));
        Assert.Equal(InstallHold.MutexName(root), InstallHold.MutexName(root.ToUpperInvariant()));
        Assert.NotEqual(InstallHold.MutexName(root), InstallHold.MutexName(NewRoot()));
        Assert.Matches(@"\ALocal\\nth\.Installing\.[0-9A-F]{16}\z", InstallHold.MutexName(root));
        Assert.Throws<ArgumentException>(() => InstallHold.MutexName(" "));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_second_setup_for_the_same_root_is_told_another_install_is_in_progress()
    {
        var root = NewRoot();

        using var first = InstallHold.TryAcquire(root);
        Assert.NotNull(first);
        Assert.True(InstallHold.IsHeld(root));
        Assert.Null(InstallHold.TryAcquire(root));
        using var other = InstallHold.TryAcquire(NewRoot()); // another root is its own matter
        Assert.NotNull(other);
        first.Dispose();
        Assert.False(InstallHold.IsHeld(root));
        using var next = InstallHold.TryAcquire(root);
        Assert.NotNull(next);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void Nothing_to_wait_for_when_no_setup_holds_the_root()
    {
        var root = NewRoot();
        var started = DateTime.UtcNow;

        Assert.True(InstallHold.WaitWhileHeld(root, TimeSpan.FromSeconds(30)));
        Assert.False(InstallHold.IsHeld(root));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task An_nth_that_starts_during_the_install_waits_until_the_setup_lets_go()
    {
        var root = NewRoot();
        var hold = InstallHold.TryAcquire(root);
        Assert.NotNull(hold);

        var waiting = Task.Run(() => InstallHold.WaitWhileHeld(root, TimeSpan.FromSeconds(30)));
        await Task.Delay(400);
        Assert.False(waiting.IsCompleted); // still held: no window yet

        // The setup finishes on a different thread from the one that took the hold, as an async setup does.
        await Task.Run(hold.Dispose);

        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(InstallHold.IsHeld(root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void The_wait_gives_up_after_its_limit_and_says_the_root_is_still_held()
    {
        var root = NewRoot();
        using var hold = InstallHold.TryAcquire(root);

        Assert.False(InstallHold.WaitWhileHeld(root, TimeSpan.FromMilliseconds(250)));
        Assert.True(InstallHold.IsHeld(root)); // the wait took nothing
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_start_wait_is_ten_minutes_at_most()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), InstallHold.MaxStartWait);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Several_waiting_copies_all_carry_on_when_the_setup_lets_go()
    {
        var root = NewRoot();
        var hold = InstallHold.TryAcquire(root);
        Assert.NotNull(hold);
        var waiting = Enumerable.Range(0, 3).Select(_ => Task.Run(() => InstallHold.WaitWhileHeld(root, TimeSpan.FromSeconds(30)))).ToArray();

        await Task.Delay(300);
        hold.Dispose();

        var results = await Task.WhenAll(waiting).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(results, result => Assert.True(result));
    }
}
