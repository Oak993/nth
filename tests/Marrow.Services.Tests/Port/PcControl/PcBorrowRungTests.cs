using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;
using PcInputKind = Marrow.Services.Port.PcControl.PcInputKind;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.6, 2.7, 2.8). Rung R4 on the lease's fakes: it
// offers nothing while real input is off or unproven, a pointer way for clicks, wheels and drags and a focus way for keys
// and text; text goes in borrows of 64 characters with the boundary check between; replace mode selects all first; a drag
// longer than the cap is refused; and the borrow's answers become the step's.
[Trait("Decision", "ED-NEW-pcctl-18")]
public sealed class PcBorrowRungTests
{
    private static readonly PcWindowInfo Paint = new(PcBorrowRig.Target, "Untitled - Paint", "MSPaintApp", 42, "c:\\windows\\mspaint.exe");

    [Fact]
    public void It_refuses_while_real_input_is_off_or_unproven()
    {
        var rig = new PcBorrowRig();
        var click = new PcInputRequest(PcInputKind.Click, Paint) { Point = new PcPoint(10, 10) };

        var off = new PcBorrowRung(() => PcSettings.Default, rig.Lease).Offer(click, PcFramework.Win32);
        var unbuilt = new PcBorrowRung(On).Offer(click, PcFramework.Win32);
        var unproven = new PcBorrowRung(On, rig.Lease).Offer(click, PcFramework.Win32);

        Assert.Equal(PcErrorCode.NeedsRealInput, off.Refusal);
        Assert.Equal(PcRealInputReason.Off, off.Reason);
        Assert.Equal(PcErrorCode.NeedsRealInput, unbuilt.Refusal);
        Assert.Equal(PcRealInputReason.ActivationUnproven, unbuilt.Reason);
        Assert.Equal(PcErrorCode.NeedsRealInput, unproven.Refusal);
        Assert.Equal(PcRealInputReason.ActivationUnproven, unproven.Reason);
        Assert.Empty(new PcBorrowRung(On, rig.ProveAll().Lease).Offer(new PcInputRequest(PcInputKind.Command, Paint), PcFramework.Win32).Plans);
    }

    [Fact]
    public async Task A_click_borrows_the_mouse_at_the_elements_centre()
    {
        var rig = new PcBorrowRig().ProveAll();
        var button = new PcUiNode("7", "button", "Save") { Bounds = new PcRect(100, 100, 40, 20) };
        var plan = Assert.Single(new PcBorrowRung(On, rig.Lease).Offer(new PcInputRequest(PcInputKind.Click, Paint) { Element = button }, PcFramework.Win32).Plans);

        var outcome = await plan.Run(CancellationToken.None);

        Assert.True(outcome.Done);
        Assert.Equal(PcRung.R4, plan.Rung);
        Assert.Equal("borrow_click", plan.Method);
        Assert.False(plan.Writes);
        var move = rig.Host.Sent[0].Events[0];
        Assert.Equal(new PcPoint(120, 110), CoordinateMapper.FromAbsolute(move.Dx, move.Dy, rig.Host.Desktop()));
    }

    [Fact]
    public async Task Text_goes_in_borrows_of_64_characters_with_the_boundary_check_between()
    {
        var rig = new PcBorrowRig().ProveAll();
        var checks = new List<(int Done, int Total)>();
        var text = new string('a', 100) + "\U0001F600" + new string('b', 30);
        var field = new PcUiNode("9", "edit", "Name");
        var request = new PcInputRequest(PcInputKind.Type, Paint)
        {
            Element = field,
            Text = text,
            Boundary = (done, total, _) =>
            {
                checks.Add((done, total));
                return Task.FromResult<string?>(null);
            },
        };
        var plan = Assert.Single(new PcBorrowRung(On, rig.Lease).Offer(request, PcFramework.Win32).Plans);

        var outcome = await plan.Run(CancellationToken.None);

        Assert.True(outcome.Done);
        Assert.True(plan.Writes);
        Assert.Equal(3, rig.Host.Sent.Count);
        Assert.Equal(new[] { (64, text.Length), (128, text.Length) }, checks);
        Assert.Equal(new[] { "begin", "end", "begin", "end", "begin", "end" }, rig.Hold.Log);
    }

    [Fact]
    public async Task A_boundary_that_finds_the_field_changed_stops_the_typing()
    {
        var rig = new PcBorrowRig().ProveAll();
        var request = new PcInputRequest(PcInputKind.Type, Paint)
        {
            Text = new string('x', 70),
            Boundary = (_, _, _) => Task.FromResult<string?>("changed while paused"),
        };
        var plan = Assert.Single(new PcBorrowRung(On, rig.Lease).Offer(request, PcFramework.Win32).Plans);

        var outcome = await plan.Run(CancellationToken.None);

        Assert.Equal(PcErrorCode.ExpectFailed, outcome.Error);
        Assert.Equal("changed while paused", outcome.Reason);
        Assert.Single(rig.Host.Sent);
    }

    [Fact]
    public async Task Replace_mode_selects_the_fields_text_first()
    {
        var rig = new PcBorrowRig().ProveAll();
        var plan = Assert.Single(new PcBorrowRung(On, rig.Lease).Offer(new PcInputRequest(PcInputKind.Type, Paint) { Text = "hi", Insert = false }, PcFramework.Win32).Plans);

        await plan.Run(CancellationToken.None);

        var keys = rig.Host.Sent[0].Events;
        Assert.Equal(new ushort[] { 0x11, 0x41, 0x41, 0x11 }, keys.Take(4).Select(e => e.Vk));
        Assert.Equal(4, keys.Count(e => (e.Flags & PcInputPlan.KEY_UNICODE) != 0));
    }

    [Fact]
    public async Task Keys_without_an_element_go_to_the_apps_own_focus()
    {
        var rig = new PcBorrowRig().ProveAll();
        var request = new PcInputRequest(PcInputKind.Key, Paint) { Chord = KeyChordParser.Parse("Ctrl+B").Chord, KeysText = "Ctrl+B" };
        var plan = Assert.Single(new PcBorrowRung(On, rig.Lease).Offer(request, PcFramework.Win32).Plans);

        var outcome = await plan.Run(CancellationToken.None);

        Assert.True(outcome.Done);
        Assert.Equal("pressed Ctrl+B with the real keyboard", plan.Detail);
        Assert.Equal(new ushort[] { 0x11, 0x42, 0x42, 0x11 }, rig.Host.Sent[0].Events.Select(e => e.Vk));
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
    }

    [Fact]
    public async Task A_drag_longer_than_the_cap_is_refused_before_it_holds_anything()
    {
        var rig = new PcBorrowRig().ProveAll();
        var path = Enumerable.Range(0, 40).Select(i => new PcPoint(10 + i, 10)).ToList();
        var plan = Assert.Single(new PcBorrowRung(On, rig.Lease).Offer(new PcInputRequest(PcInputKind.Drag, Paint) { Path = path }, PcFramework.Win32).Plans);

        var outcome = await plan.Run(CancellationToken.None);

        Assert.Equal(PcErrorCode.NotSupported, outcome.Error);
        Assert.Empty(rig.Hold.Log);
    }

    [Fact]
    public void The_borrows_answers_become_the_steps()
    {
        Assert.True(PcBorrowRung.Outcome(new PcBorrowOutcome(PcBorrowStatus.Done)).Done);
        Assert.Equal(PcErrorCode.DeferredByUser, PcBorrowRung.Outcome(new PcBorrowOutcome(PcBorrowStatus.Deferred)).Error);
        Assert.Equal(PcErrorCode.NeedsRealInput, PcBorrowRung.Outcome(new PcBorrowOutcome(PcBorrowStatus.NeedsRealInput, "monitor unhealthy")).Error);
        Assert.Equal(PcErrorCode.NeedsRealInput, PcBorrowRung.Outcome(new PcBorrowOutcome(PcBorrowStatus.NotSent, "not in front")).Error);
        Assert.Equal(PcErrorCode.OccludedPoint, PcBorrowRung.Outcome(new PcBorrowOutcome(PcBorrowStatus.NotSent) { Code = PcErrorCode.OccludedPoint }).Error);
        Assert.Equal(PcErrorCode.Timeout, PcBorrowRung.Outcome(new PcBorrowOutcome(PcBorrowStatus.Failed) { Code = PcErrorCode.Timeout }).Error);
        Assert.Equal(PcErrorCode.NotSupported, PcBorrowRung.Outcome(new PcBorrowOutcome(PcBorrowStatus.Failed)).Error);
    }

    [Theory]
    [InlineData(PcScrollUnit.Line, 1.0, 1)]
    [InlineData(PcScrollUnit.Line, 3.0, 1)]
    [InlineData(PcScrollUnit.Line, 4.0, 2)]
    [InlineData(PcScrollUnit.Page, 1.0, 5)]
    [InlineData(PcScrollUnit.Page, 0.5, 3)]
    [InlineData(PcScrollUnit.Line, 0.0, 1)]
    public void Wheel_notches_follow_windows_three_lines_a_notch(PcScrollUnit unit, double amount, int notches)
    {
        Assert.Equal(notches, PcBorrowRung.Notches(unit, amount));
    }

    [Fact]
    public void Pieces_never_split_a_surrogate_pair()
    {
        var text = new string('a', 63) + "\U0001F600" + "b";

        var pieces = PcBorrowRung.Pieces(text);

        Assert.Equal(new[] { 63, 3 }, pieces.Select(p => p.Length));
        Assert.Equal(text, string.Concat(pieces));
        Assert.Empty(PcBorrowRung.Pieces(""));
    }

    [Fact]
    public void A_drag_is_down_moves_and_up_with_the_hold_before_the_first_move()
    {
        var (arrays, gaps) = PcBorrowRung.DragArrays([new PcPoint(0, 0), new PcPoint(5, 5), new PcPoint(9, 9)], [0x10], 200, new PcRect(0, 0, 100, 100), vk => vk);

        Assert.Equal(4, arrays.Count);
        Assert.Equal(PcInputPlan.LEFTDOWN, arrays[0][^1].Flags);
        Assert.Equal(0x10, arrays[0][1].Vk);
        Assert.Equal(PcInputPlan.LEFTUP, arrays[3][0].Flags);
        Assert.Equal(new[] { 200, InputInjector.DragStepMs, InputInjector.DragStepMs }, gaps);
    }

    private static PcSettings On() => PcSettings.Default with { Enabled = true, RealInput = true };
}
