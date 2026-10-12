using Marrow.Setup.Platform;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Platform;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, step 1, and 10, "Single instance"; unit U1 (ED-NEW-inst-u1-1). One
// setup window per user session (a named lock, Local\nth Setup): a second start finds the first window and brings it to
// the front, then exits 0. The tests use their own lock names, so a real setup's lock is never touched.
public sealed class SingleInstanceTests
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_lock_is_Local_nth_Setup()
    {
        Assert.Equal(@"Local\nth Setup", SingleInstance.SetupLockName);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_second_start_does_not_get_the_lock_until_the_first_lets_go()
    {
        var name = UniqueName();
        using (var first = SingleInstance.TryAcquire(name))
        {
            Assert.NotNull(first);
            Assert.Null(SingleInstance.TryAcquire(name));
        }
        using var again = SingleInstance.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_second_start_brings_the_published_first_window_forward()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name)!;
        first.Publish(0x1234, 4321);
        var focuser = new FakeWindowFocuser();

        Assert.True(SingleInstance.FocusExisting(focuser, TimeSpan.FromSeconds(1), name));

        Assert.Equal(new[] { ((nint)0x1234, 4321) }, focuser.Focused);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_second_start_waits_for_a_first_window_that_is_still_opening()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name)!;
        var focuser = new FakeWindowFocuser();
        var publisher = Task.Run(async () =>
        {
            await Task.Delay(150);
            first.Publish(0x99, 7);
        });

        Assert.True(SingleInstance.FocusExisting(focuser, TimeSpan.FromSeconds(5), name));
        await publisher;

        Assert.Equal(new[] { ((nint)0x99, 7) }, focuser.Focused);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_first_window_that_never_appears_is_given_up_on_in_time()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name)!;
        var focuser = new FakeWindowFocuser();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(SingleInstance.FocusExisting(focuser, TimeSpan.FromMilliseconds(200), name));

        Assert.Empty(focuser.Focused);
        Assert.InRange(watch.Elapsed.TotalMilliseconds, 150, 3000);
    }

    private static string UniqueName() => @"Local\nth Setup test " + Guid.NewGuid().ToString("N");
}
