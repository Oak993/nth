// PORT-ONLY: Tactus, what the lease tells the pill about a borrow (ED-NEW-pcctl-38; DESIGN.md 2.7 "Borrow sequence", 4.5
// "Hands off", "Done", "Borrow chip"). A wait carries the borrow's kind and words; a shown wait says when its borrow starts
// holding the user's input, and that it ran when its wait ends; a borrow that went at once hands the chip its wait; a still
// moment is the caller's wait, with no kind. Fakes only.
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class RealInputLeaseRequestTests
{
    private static readonly PcStepWords ClickSave = new("click Save", "clicked Save", "one click");

    [Fact]
    public async Task A_waited_borrow_shows_its_words_says_when_it_runs_and_that_it_ran()
    {
        var rig = new PcBorrowRig().ProveAll();
        var seen = new List<string>();
        PcBorrowWait? shown = null;
        rig.Lease.Waiting += wait =>
        {
            shown ??= wait;
            seen.Add("waiting " + wait.Running);
        };
        rig.Lease.RunningChanged += wait => seen.Add("running " + wait.Running + " " + ReferenceEquals(wait, shown));
        rig.Lease.WaitEnded += wait => seen.Add("ended " + wait.Ran + " " + ReferenceEquals(wait, shown));
        rig.Lease.Used += _ => seen.Add("used");
        rig.UserInput(rig.Move(5, 5));

        var outcome = await rig.Drive(rig.Lease.Borrow(rig.Click() with { Words = ClickSave }, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.NotNull(shown);
        Assert.Equal(PcBorrowKind.Pointer, shown.Kind);
        Assert.Equal(ClickSave, shown.Words);
        Assert.Equal(PcBorrowRig.Paint, shown.Stream);
        Assert.Equal(["waiting False", "running True True", "ended True True"], seen);
    }

    [Fact]
    public async Task A_borrow_that_goes_at_once_hands_the_chip_its_wait_and_shows_nothing_else()
    {
        var rig = new PcBorrowRig().ProveAll();
        var used = new List<PcBorrowWait>();
        var other = 0;
        rig.Lease.Used += used.Add;
        rig.Lease.Waiting += _ => other++;
        rig.Lease.RunningChanged += _ => other++;
        rig.Lease.WaitEnded += _ => other++;

        var outcome = await rig.Lease.Borrow(rig.Keys(PcBorrowRig.Paint, 0x41) with { Words = new("press A", "pressed A", "A") }, CancellationToken.None);

        Assert.True(outcome.Done);
        var chip = Assert.Single(used);
        Assert.Equal(PcBorrowKind.Focus, chip.Kind);
        Assert.Equal("A", chip.Words!.Chip);
        Assert.Equal("keys in mspaint", chip.Detail);
        Assert.True(chip.Ran);
        Assert.Equal(0, other);
    }

    [Fact]
    public async Task A_still_moment_waits_as_the_caller_s_wait_with_no_kind()
    {
        var rig = new PcBorrowRig().ProveAll();
        var waits = new List<PcBorrowWait>();
        rig.Lease.Waiting += waits.Add;
        rig.UserInput(rig.Move(5, 5));
        var wait = new PcBorrowWait(PcBorrowRig.Excel, "a step in Excel") { Words = new("scroll", "scrolled", "a scroll") };

        var still = rig.Lease.Still(wait, null, CancellationToken.None);
        for (var i = 0; i < 400 && !still.IsCompleted; i++)
        {
            await PcTestWait.Until(() => still.IsCompleted || rig.Clock.Pending > 0);
            if (still.IsCompleted) break;
            rig.Clock.Advance(RealInputLease.PollMs);
            await Task.Yield();
        }

        Assert.Equal(PcBorrowStatus.Done, await still.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(wait, Assert.Single(waits));
        Assert.Null(wait.Kind);
        Assert.False(wait.Running);
        Assert.False(wait.Ran);
    }
}
