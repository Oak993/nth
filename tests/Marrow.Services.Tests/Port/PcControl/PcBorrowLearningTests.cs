using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-45; DESIGN.md 2.6 "Self-check (capability learning)", 2.7 steps 5
// and 6, critiques C9 and C16). The borrow's activation and restore ways, learned per target (exe, version, framework):
// a way not tried yet is probed in DESIGN's order, a finished borrow proves its way and the restore, a target that did not
// come in front refuses that way for 7 days, a failed restore turns the target off, and nothing carries over to another
// exe, version or framework, or from the older per-exe and "every app" proofs.
[Trait("Decision", "ED-NEW-pcctl-45")]
public sealed class PcBorrowLearningTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly PcProofTarget Notepad = new("notepad.exe", "10.0.26100.1", PcFramework.Win32);

    private readonly PcCapabilities capabilities;
    private DateTimeOffset now = Start;

    public PcBorrowLearningTests() => capabilities = new PcCapabilities(() => now);

    private static PcBorrowOutcome Done(bool restored = true) => new(PcBorrowStatus.Done) { ForegroundRestored = restored };

    private static PcBorrowOutcome NotInFront() => new(PcBorrowStatus.NotSent, "the window did not come in front; nothing was typed") { Activated = false };

    private PcLearned State(PcProofTarget target, string method) => capabilities.State(PcBorrowProofs.Key(target, method));

    [Fact]
    public void Nothing_learned_yet_probes_the_caption_for_a_focus_step_and_the_click_for_a_pointer_step()
    {
        Assert.Equal(PcActivation.Caption, PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Focus));
        Assert.Equal(PcActivation.Pointer, PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Pointer));
        Assert.Null(PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Focus));
        Assert.Null(PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Pointer));
    }

    [Fact]
    public void A_finished_borrow_proves_its_way_and_the_restore()
    {
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, Done());

        Assert.Equal(PcLearned.Passed, State(Notepad, PcBorrowProofs.Caption));
        Assert.Equal(PcLearned.Passed, State(Notepad, PcBorrowProofs.Restore));
        Assert.Equal(PcLearned.Unknown, State(Notepad, PcBorrowProofs.Foreground));
    }

    [Fact]
    public void A_target_that_did_not_come_in_front_refuses_that_way_and_the_next_borrow_tries_SetForegroundWindow()
    {
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, NotInFront());

        Assert.Equal(PcLearned.Refused, State(Notepad, PcBorrowProofs.Caption));
        Assert.Equal(PcLearned.Unknown, State(Notepad, PcBorrowProofs.Restore));
        Assert.Equal(PcActivation.Foreground, PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Focus));
    }

    [Fact]
    public void With_every_activation_refused_the_answer_is_activation_unproven_until_the_refusals_expire()
    {
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, NotInFront());
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Foreground, NotInFront());

        Assert.Null(PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Focus));
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Focus));

        now = Start + PcCapabilities.RefusedFor - TimeSpan.FromMinutes(1);
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Focus));

        now = Start + PcCapabilities.RefusedFor;
        Assert.Null(PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Focus));
        Assert.Equal(PcActivation.Caption, PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Focus));
    }

    [Fact]
    public void A_pointer_step_whose_click_did_not_bring_the_target_in_front_is_refused()
    {
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Pointer, NotInFront());

        Assert.Null(PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Pointer));
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Pointer));
        Assert.Null(PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Focus));
    }

    [Fact]
    public void A_restore_that_did_not_bring_the_user_window_back_turns_the_target_off()
    {
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, Done(restored: false));

        Assert.Equal(PcLearned.Passed, State(Notepad, PcBorrowProofs.Caption));
        Assert.Equal(PcLearned.Refused, State(Notepad, PcBorrowProofs.Restore));
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Focus));
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Pointer));
    }

    [Fact]
    public void A_way_that_worked_and_then_failed_is_refused()
    {
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, Done());
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, NotInFront());

        Assert.Equal(PcLearned.Refused, State(Notepad, PcBorrowProofs.Caption));
        Assert.Equal(PcActivation.Foreground, PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Focus));
    }

    [Fact]
    public void A_proven_way_is_used_before_a_way_not_tried_yet()
    {
        capabilities.Passed(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Foreground));

        Assert.Equal(PcActivation.Foreground, PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Focus));
    }

    [Theory]
    [InlineData("wordpad.exe", "10.0.26100.1", PcFramework.Win32)]
    [InlineData("notepad.exe", "10.0.26100.2", PcFramework.Win32)]
    [InlineData("notepad.exe", "10.0.26100.1", PcFramework.Xaml)]
    public void A_proof_never_carries_over_to_another_exe_version_or_framework(string exe, string version, PcFramework framework)
    {
        var other = new PcProofTarget(exe, version, framework);
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Foreground, Done());
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, NotInFront());

        foreach (var method in new[] { PcBorrowProofs.Pointer, PcBorrowProofs.Caption, PcBorrowProofs.Foreground, PcBorrowProofs.Restore })
            Assert.Equal(PcLearned.Unknown, State(other, method));
        Assert.Equal(PcActivation.Caption, PcBorrowProofs.Activation(capabilities, other, PcBorrowKind.Focus));
    }

    [Fact]
    public void The_older_per_exe_and_every_app_proofs_and_the_learned_ones_do_not_mix()
    {
        foreach (var method in new[] { PcBorrowProofs.Pointer, PcBorrowProofs.Caption, PcBorrowProofs.Foreground, PcBorrowProofs.Restore })
        {
            capabilities.Passed(PcBorrowProofs.Key("notepad.exe", method));
            capabilities.Passed(PcBorrowProofs.Key(PcBorrowProofs.AnyApp, method));
            Assert.Equal(PcLearned.Unknown, State(Notepad, method));
        }

        var fresh = new PcCapabilities(() => now);
        PcBorrowProofs.Learn(fresh, Notepad, PcActivation.Caption, Done());
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(fresh, "notepad.exe", PcBorrowKind.Focus));
    }

    [Theory]
    [InlineData("")]
    [InlineData(PcBorrowProofs.AnyApp)]
    public void A_target_without_a_real_exe_is_never_borrowed_and_teaches_nothing(string exe)
    {
        var nobody = new PcProofTarget(exe, "1.0", PcFramework.Win32);

        PcBorrowProofs.Learn(capabilities, nobody, PcActivation.Caption, Done());

        Assert.Null(PcBorrowProofs.Activation(capabilities, nobody, PcBorrowKind.Focus));
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, nobody, PcBorrowKind.Pointer));
        Assert.Equal("[]", capabilities.Export());
    }

    [Fact]
    public void Not_now_the_gate_a_covered_point_or_a_control_that_would_not_take_the_focus_teach_nothing()
    {
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, new PcBorrowOutcome(PcBorrowStatus.Deferred));
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, new PcBorrowOutcome(PcBorrowStatus.NeedsRealInput, PcRealInputReason.ActivationUnproven));
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Pointer, new PcBorrowOutcome(PcBorrowStatus.NotSent, "the point is covered by another window") { Code = PcErrorCode.OccludedPoint });
        PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, new PcBorrowOutcome(PcBorrowStatus.NotSent, "the control could not take the focus"));

        Assert.Equal("[]", capabilities.Export());
    }

    [Fact]
    public void A_target_is_its_lower_case_exe_its_version_and_its_framework_with_the_framework_in_the_control_slot()
    {
        var window = new PcWindowInfo(7, "Untitled - Notepad", "Notepad", 42, @"C:\Windows\System32\Notepad.EXE") { Version = "10.0.26100.1" };

        var target = PcProofTarget.Of(window, PcFramework.Win32);

        Assert.Equal(Notepad, target);
        Assert.Equal(new PcCapabilityKey("notepad.exe", "10.0.26100.1", PcRung.R4, "activate_caption", "Win32"), PcBorrowProofs.Key(target, PcBorrowProofs.Caption));
    }
}
