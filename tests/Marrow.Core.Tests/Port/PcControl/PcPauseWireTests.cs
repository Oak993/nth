// PORT-ONLY: pill v2's wire for Pause (OD-0145, ED-NEW-pcctl-12; DESIGN.md 10.1, 10.9): waiting_for_user "paused" tells the
// model not to work around the pause, the first result after a Resume carries the resumed block, stale_obs says how long
// the pause was, pc_list_apps names each app's state, and the instructions carry the pause lines.
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-12")]
public sealed class PcPauseWireTests
{
    [Fact]
    public void A_paused_app_returns_waiting_for_user_paused_with_its_token_and_the_way_to_go_on()
    {
        var result = PcResult.WaitingFor(PcWaitReason.Paused, "t:4", body: "done: 2 of 5 steps");
        var text = result.Render();

        Assert.False(result.Ok);
        Assert.StartsWith("waiting_for_user: paused. token: t:4. The user paused this app: do not retry it or reach it another way.", text, StringComparison.Ordinal);
        Assert.Contains("call pc_wait once with until \"step\" and this token", text, StringComparison.Ordinal);
        Assert.Contains("end your turn with one short line", text, StringComparison.Ordinal);
        Assert.EndsWith("\ndone: 2 of 5 steps", text, StringComparison.Ordinal);
        Assert.Equal("paused", PcWaitReason.Paused);
        Assert.Equal("question", PcWaitReason.Question);
    }

    [Fact]
    public void The_first_result_after_a_Resume_carries_the_resumed_block_after_the_window_line()
    {
        var result = PcResult.Success(new PcWindowLine("Book1 - Excel", "EXCEL.EXE", "o:7", 0, 0, 1), body: "cell B2: 41") with
        {
            Resumed = new PcResumed(180, true, 1),
        };

        Assert.Equal("ok\nwindow: \"Book1 - Excel\" (EXCEL.EXE) obs: o:7\nresumed: {\"after_ms\":180,\"reread\":true,\"changed\":1}\ncell B2: 41", result.Render());
        var block = (JsonObject)JsonNode.Parse(new PcResumed(95, true, 0).Render()["resumed: ".Length..])!;
        Assert.Equal(95, block["after_ms"]!.GetValue<long>());
    }

    [Theory]
    [InlineData(0.4, "paused 1 s; observe again")]
    [InlineData(12, "paused 12 s; observe again")]
    [InlineData(4 * 60 + 30, "paused 4 min; observe again")]
    [InlineData(2 * 3600, "paused 2 h; observe again")]
    [InlineData(2 * 3600 + 5 * 60, "paused 2 h 5 min; observe again")]
    public void Stale_obs_after_a_pause_says_how_long_the_pause_was(double seconds, string reason)
    {
        Assert.Equal(reason, PcPauseText.StaleAfterPause(TimeSpan.FromSeconds(seconds)));
        Assert.StartsWith("error: stale_obs: " + reason + ".", PcResult.Fail(PcErrorCode.StaleObs, PcPauseText.StaleAfterPause(TimeSpan.FromSeconds(seconds))).Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_listed_app_has_a_state_of_working_paused_or_idle()
    {
        Assert.Equal("working", PcPauseText.Wire(PcAppState.Working));
        Assert.Equal("paused", PcPauseText.Wire(PcAppState.Paused));
        Assert.Equal("idle", PcPauseText.Wire(PcAppState.Idle));
    }

    [Fact]
    public void The_instructions_tell_the_model_not_to_work_around_a_pause_and_to_look_again_after_a_resume()
    {
        var text = PcInstructions.Text;

        Assert.Contains("If its reason is paused, the user paused that app: do not retry it or reach it another way", text, StringComparison.Ordinal);
        Assert.Contains("call pc_wait once", text, StringComparison.Ordinal);
        Assert.Contains("end your turn with one short line", text, StringComparison.Ordinal);
        Assert.Contains("After a resume, look again before you use points", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pause", PcToolSchemas.All.Select(t => t.Name).Aggregate("", (a, b) => a + b), StringComparison.Ordinal);
    }
}
