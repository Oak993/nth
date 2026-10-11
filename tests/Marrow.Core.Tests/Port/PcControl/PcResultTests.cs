using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.1 result envelope and 2.2 stable error codes, each with one next step.
[Trait("Decision", "ED-NEW-pcctl-1")]
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
        foreach (var design in new[] { "not_approved", "denied_app", "elevated", "secure_desktop", "locked", "password_field", "stale_obs", "stale_element", "occluded_point", "window_gone", "stopped_by_user", "user_took_over", "needs_confirmation", "timeout", "bad_args", "disabled" })
            Assert.Contains(design, wires);
    }

    [Fact]
    public void Window_titles_are_quoted_with_escapes_and_scale_uses_invariant_digits()
    {
        var line = new PcWindowLine("Say \"hi\" \\ there", "app.exe", null, 100, 50, 0.33333);
        Assert.Equal("window: \"Say \\\"hi\\\" \\\\ there\" (app.exe) size: 100x50 scale: 0.333", line.Render());
        Assert.Equal("window: \"t\" (a.exe)", new PcWindowLine("t", "a.exe", null, 0, 0, 1).Render());
    }
}
