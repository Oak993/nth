using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "Windows Hello guards every change", 16.1; ED-NEW-dextra-as20-5): one check at a time;
// three failed or cancelled checks in a row lock purchases off (raised once, then the count starts again); a verified check
// resets the count; a prompt that could not be shown, or a gate that throws, passes nothing and counts nothing.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-5")]
public sealed class HelloGuardTests
{
    [Fact]
    public async Task Three_failed_checks_in_a_row_lock_out_once_and_the_count_starts_again()
    {
        var gate = new FakeHelloGate().Then(HelloResult.Failed, HelloResult.Cancelled, HelloResult.Failed, HelloResult.Failed);
        var guard = new HelloGuard(gate);
        var locked = 0;
        guard.LockedOut += () => locked++;
        Assert.Equal(HelloResult.Failed, await guard.Check("turn purchases on"));
        Assert.Equal(1, guard.Failures);
        Assert.Equal(HelloResult.Cancelled, await guard.Check("turn purchases on"));
        Assert.Equal((2, 0), (guard.Failures, locked));
        Assert.Equal(HelloResult.Failed, await guard.Check("turn purchases on"));
        Assert.Equal((0, 1), (guard.Failures, locked));
        Assert.Equal(HelloResult.Failed, await guard.Check("turn purchases on"));
        Assert.Equal((1, 1), (guard.Failures, locked));
    }

    [Fact]
    public async Task A_verified_check_resets_the_count()
    {
        var guard = new HelloGuard(new FakeHelloGate().Then(HelloResult.Failed, HelloResult.Failed, HelloResult.Verified, HelloResult.Failed, HelloResult.Failed));
        var locked = 0;
        guard.LockedOut += () => locked++;
        for (var i = 0; i < 5; i++) await guard.Check("save the new limit");
        Assert.Equal((2, 0), (guard.Failures, locked));
    }

    [Fact]
    public async Task A_prompt_that_cannot_show_or_a_gate_that_throws_passes_nothing_and_counts_nothing()
    {
        var guard = new HelloGuard(new FakeHelloGate().Then(HelloResult.Unavailable, HelloResult.Busy));
        Assert.Equal(HelloResult.Unavailable, await guard.Check("pay"));
        Assert.Equal(HelloResult.Unavailable, await guard.Check("pay")); // a gate's own Busy is no pass either
        Assert.Equal(0, guard.Failures);
        var throwing = new HelloGuard(new FakeHelloGate { Throws = new InvalidOperationException("no prompt") });
        Assert.Equal(HelloResult.Unavailable, await throwing.Check("pay"));
        Assert.Equal(0, throwing.Failures);
    }

    [Fact]
    public async Task Only_one_check_is_open_at_a_time()
    {
        var gate = new FakeHelloGate();
        gate.Hold();
        var guard = new HelloGuard(gate);
        var first = guard.Check("pay $46.80 to shop.example");
        Assert.Equal(HelloResult.Busy, await guard.Check("remove the card"));
        gate.Release(HelloResult.Verified);
        Assert.Equal(HelloResult.Verified, await first);
        Assert.Equal(new[] { "pay $46.80 to shop.example" }, gate.Reasons);
        Assert.Equal(HelloResult.Verified, await guard.Check("remove the card"));
    }
}
