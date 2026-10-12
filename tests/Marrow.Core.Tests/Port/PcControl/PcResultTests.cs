using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.1 result envelope and 2.2 stable error codes, each with one next step.
[Trait("Decision", "ED-NEW-pcctl-1")]
[Trait("Decision", "ED-NEW-pcctl-7")]
public sealed class PcResultTests
{
    [Fact]
    public void A_success_renders_ok_then_the_window_line_then_the_body()
    {
        var result = PcResult.Success(new PcWindowLine("Untitled - Notepad", "notepad.exe", "o:41", 1280, 812, 0.8), "[1] window \"x\"\n");
        Assert.Equal("ok\nwindow: \"Untitled - Notepad\" (notepad.exe) obs: o:41 size: 1280x812 scale: 0.8\n[1] window \"x\"", result.Render());
    }

    [Fact]
    public void An_error_renders_code_reason_and_one_recovery_step()
    {
        var result = PcResult.Fail(PcErrorCode.StaleElement, "element 12 changed");
        Assert.Equal("error: stale_element: element 12 changed. Call pc_observe and pick again.", result.Render());
        Assert.False(result.Ok);
    }

    [Fact]
    public void Every_error_code_has_a_distinct_snake_case_wire_name_and_a_recovery_step()
    {
        var codes = Enum.GetValues<PcErrorCode>();
        var wires = codes.Select(PcErrorCodes.Wire).ToList();
        Assert.Equal(wires.Count, wires.Distinct(StringComparer.Ordinal).Count());
        foreach (var wire in wires) Assert.Matches("^[a-z]+(_[a-z]+)*$", wire);
        foreach (var code in codes) Assert.False(string.IsNullOrWhiteSpace(PcErrorCodes.Recovery(code)));
        foreach (var design in new[] { "not_approved", "denied_app", "elevated", "secure_desktop", "locked", "password_field", "stale_obs", "stale_element", "occluded_point", "window_gone", "stopped_by_user", "needs_confirmation", "timeout", "bad_args", "disabled", "ambiguous_target", "expect_failed", "deferred_by_user", "declined_by_user", "user_typing_here", "busy_window", "needs_real_input", "not_in_workspace" })
            Assert.Contains(design, wires);
        // User input never stops Tactus, so nothing can say the user took over (DESIGN.md 2.2, ED-NEW-pcctl-7).
        Assert.DoesNotContain("user_took_over", wires);
    }

    [Fact]
    public void Unverified_is_a_status_that_is_neither_ok_nor_an_error()
    {
        var result = PcResult.Unverified("only unrelated parts of the window changed", body: "done: invoke button \"Next\"");
        Assert.False(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal("unverified: only unrelated parts of the window changed. Call pc_observe or give an expect; do not assume it worked.\ndone: invoke button \"Next\"", result.Render());
    }

    [Fact]
    public void Waiting_for_user_is_a_status_with_its_reason_a_token_and_the_steps_done()
    {
        var result = PcResult.WaitingFor(PcWaitReason.Approval, "s:3", body: "1. pc_click: done");
        Assert.False(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal(PcStatus.WaitingForUser, result.Status);
        Assert.Equal("waiting_for_user: approval. token: s:3. Do other steps, then call pc_wait with until \"step\" and this token.\n1. pc_click: done", result.Render());
        Assert.Equal(new[] { "approval", "hands_off", "typing_here", "confirm_card", "batch_budget" },
            new[] { PcWaitReason.Approval, PcWaitReason.HandsOff, PcWaitReason.TypingHere, PcWaitReason.ConfirmCard, PcWaitReason.BatchBudget });
        Assert.Equal(new[] { "real input off", "not allowed for this app", "foreground protected", "monitor unhealthy", "activation unproven" },
            new[] { PcRealInputReason.Off, PcRealInputReason.NotAllowed, PcRealInputReason.ForegroundProtected, PcRealInputReason.MonitorUnhealthy, PcRealInputReason.ActivationUnproven });
    }

    [Fact]
    public void Window_titles_are_quoted_with_escapes_and_scale_uses_invariant_digits()
    {
        var line = new PcWindowLine("Say \"hi\" \\ there", "app.exe", null, 100, 50, 0.33333);
        Assert.Equal("window: \"Say \\\"hi\\\" \\\\ there\" (app.exe) size: 100x50 scale: 0.333", line.Render());
        Assert.Equal("window: \"t\" (a.exe)", new PcWindowLine("t", "a.exe", null, 0, 0, 1).Render());
    }
}
