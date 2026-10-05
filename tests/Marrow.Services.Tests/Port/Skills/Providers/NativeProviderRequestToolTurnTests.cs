// SK-11f / DL-14 end to end (skills-ONDEMAND-PLAN.md 5.5 R4): the model asks for a tool through request_tool; the turn waits for the user's answer and the model
// carries on in the same turn. The scripted answerer plays the committed transcripts; the offer is a stand-in that has no downloader.
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class NativeProviderRequestToolTurnTests
{
    private static NativeSessionRig Rig(string[] transcripts, FakeToolOffer offer, List<ToolEvent> taps, RuntimeMode mode = RuntimeMode.FullAccess) =>
        NativeSessionRig.Create(transcripts, mode, configure: session =>
        {
            session.OnToolEvent = taps.Add;
            session.SkillTools = new NativeSkillTools(null, offer: offer);
        });

    private static string LastToolMessage(NativeSessionRig rig, int request) => rig.Json(request)["messages"]!.JSONValueArray![^1]["content"]!.JSONValueString!;

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_turn_waits_for_the_answer_and_the_model_reads_installed_at_the_path_in_the_same_turn()
    {
        var offer = new FakeToolOffer(true, "soffice");
        var taps = new List<ToolEvent>();
        await using var rig = Rig(["request-tool-call.sse", "final-answer.sse"], offer, taps);
        await rig.Begin();
        var turn = rig.StartTurn("convert report.docx");
        rig.RunUntil(() => offer.Asks.Count == 1);
        Assert.False(turn.IsCompleted);
        Assert.Single(rig.RequestBodies); // the model has not been called again: it is waiting for the tool result

        var path = Path.Combine("C:", "Marrow Code", "addons", "bin", "soffice.exe");
        offer.Answer(ToolRequestOutcome.Installed(path));
        rig.RunUntil(() => turn.IsCompleted);
        await turn;

        Assert.Equal([("soffice", "convert report.docx to PDF")], offer.Asks);
        Assert.Equal($"installed at {path}", LastToolMessage(rig, 1));
        Assert.Equal("call-4", rig.Json(1)["messages"]!.JSONValueArray![^1]["tool_call_id"]!.JSONValueString);
        Assert.Equal([new ToolRequested("soffice", "convert report.docx to PDF")], taps);
        Assert.Equal(TurnStatus.Completed, Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]).Status);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_decline_reaches_the_model_as_declined()
    {
        var offer = new FakeToolOffer(true, "soffice");
        await using var rig = Rig(["request-tool-call.sse", "final-answer.sse"], offer, []);
        await rig.Begin();
        var turn = rig.StartTurn("convert report.docx");
        rig.RunUntil(() => offer.Asks.Count == 1);
        offer.Answer(ToolRequestOutcome.Declined);
        rig.RunUntil(() => turn.IsCompleted);
        await turn;
        Assert.Equal("declined", LastToolMessage(rig, 1));
    }

    [Fact, Trait("Rule", "R-TEST-10")]
    public async Task A_name_outside_the_catalogue_is_refused_without_asking_the_user()
    {
        var offer = new FakeToolOffer(true, "soffice");
        var taps = new List<ToolEvent>();
        await using var rig = Rig(["request-tool-outside-call.sse", "final-answer.sse"], offer, taps);
        await rig.Begin();
        await rig.Turn("clean up");
        Assert.Equal("Error: rm is not in the catalogue.", LastToolMessage(rig, 1));
        Assert.Empty(offer.Asks);
        Assert.Empty(taps);
        Assert.Equal(ToolCall.Status.Failed, Assert.Single(rig.Events.OfType<ProviderEvent.ToolUpdated>()).Update.Status);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_request_adds_only_the_request_tool_definition_and_never_asks_for_approval()
    {
        var offer = new FakeToolOffer(true, "soffice");
        await using var rig = Rig(["request-tool-call.sse", "final-answer.sse"], offer, [], RuntimeMode.Supervised);
        await rig.Begin();
        var turn = rig.StartTurn("convert report.docx", RuntimeMode.Supervised);
        rig.RunUntil(() => offer.Asks.Count == 1);
        offer.Answer(ToolRequestOutcome.Declined);
        rig.RunUntil(() => turn.IsCompleted);
        await turn;

        Assert.DoesNotContain(rig.Events, e => e is ProviderEvent.Approval);
        var now = rig.Json(0);
        Assert.Equal(File.ReadAllText(NativeSessionRig.GoldenPath("golden-prompt-supervised-build.txt")), now["messages"]![0]!["content"]!.JSONValueString); // no skills: no block
        var names = now["tools"]!.JSONValueArray!.Select(t => t["function"]!["name"]!.JSONValueString!).ToArray();
        Assert.Equal(["read_file", "list_files", "search_text", "write_file", "edit_file", "run_command", "request_tool"], names);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Stopping_the_turn_while_the_user_is_deciding_cancels_the_wait_and_ends_the_turn_interrupted()
    {
        var offer = new FakeToolOffer(true, "soffice");
        await using var rig = Rig(["request-tool-call.sse", "final-answer.sse"], offer, []);
        await rig.Begin();
        var turn = rig.StartTurn("convert report.docx");
        rig.RunUntil(() => offer.Asks.Count == 1);
        await rig.Session.Interrupt();
        rig.RunUntil(() => turn.IsCompleted);
        await Record.ExceptionAsync(() => turn);

        Assert.True(offer.CancelledWhileWaiting);
        Assert.Single(rig.RequestBodies);
        Assert.Equal(TurnStatus.Interrupted, Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]).Status);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task An_offer_that_fails_comes_back_as_an_error_row_and_the_turn_goes_on()
    {
        var offer = new FakeToolOffer(true, "soffice");
        await using var rig = Rig(["request-tool-call.sse", "final-answer.sse"], offer, []);
        await rig.Begin();
        var turn = rig.StartTurn("convert report.docx");
        rig.RunUntil(() => offer.Asks.Count == 1);
        offer.Fail(new InvalidOperationException("the offer card could not be shown"));
        rig.RunUntil(() => turn.IsCompleted);
        await turn;

        Assert.Equal("Error: the offer card could not be shown", LastToolMessage(rig, 1));
        Assert.Equal(ToolCall.Status.Failed, Assert.Single(rig.Events.OfType<ProviderEvent.ToolUpdated>()).Update.Status);
        Assert.Equal(TurnStatus.Completed, Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]).Status);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_call_to_request_tool_after_nothing_is_installed_any_more_is_the_unknown_tool_again()
    {
        var offer = new AvailabilityFlip("soffice");
        await using var rig = NativeSessionRig.Create(["request-tool-call.sse", "final-answer.sse"], configure: session => session.SkillTools = new NativeSkillTools(null, offer: offer));
        await rig.Begin();
        offer.IsAvailable = false; // the user removed the last item after the request went out
        await rig.Turn("convert report.docx");
        Assert.Equal("Error: unknown tool request_tool.", LastToolMessage(rig, 1));
    }
}
