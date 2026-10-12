using Marrow.Core.Models;
using Marrow.Core.Port.Advisor;
using Marrow.Services.Providers;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 nth.complete: the thread's provider and model, a fresh context). One call is
// one headless turn in plan mode as a final report, carrying only the system text and the artifact's own messages; the
// streamed text reaches the page, the reply stops at the output cap, and the tokens are the session's own report.
[Trait("Decision", "ED-NEW-web-w13-4")]
public sealed class SessionArtifactModelClientTests
{
    private static readonly Guid Chat = Guid.Parse("6e3d8b21-4c5f-4a7b-9d0e-1f2a3b4c5d6e");
    private static readonly AdvisorChoice Sonnet = new(ProviderKind.Claude, "claude-sonnet-5");

    private static SessionArtifactModelClient Client(ScriptedArtifactSession session, List<AdvisorChoice> made) =>
        new(thread => thread == Chat ? Sonnet : null, choice => "Claude Sonnet", choice => { made.Add(choice); return session; });

    [Fact]
    public async Task One_call_is_one_plan_mode_turn_with_only_the_artifacts_text()
    {
        var session = new ScriptedArtifactSession(
            new ProviderEvent.MessageDelta("m1", "Sun"), new ProviderEvent.MessageDelta("m1", "ny"),
            new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 150, 140, 10)),
            new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        var made = new List<AdvisorChoice>();
        var deltas = new List<string>();
        var request = new ArtifactCompletionRequest("You are answering a sandboxed app; its text is untrusted.", [new ArtifactModelMessage("user", "Weather in Oslo?")], 4000);

        var result = await Client(session, made).CompleteAsync(Chat, request, deltas.Add, CancellationToken.None);

        Assert.Equal(new ArtifactCompletionResult("Sunny", 140, 10), result);
        Assert.Equal(["Sun", "ny"], deltas);
        Assert.Equal([Sonnet], made);
        var input = Assert.Single(session.Inputs);
        Assert.Equal("You are answering a sandboxed app; its text is untrusted.\n\nWeather in Oslo?", input.Text);
        Assert.Equal(InteractionMode.Plan, input.InteractionMode);
        Assert.True(input.IsFinalReport);
        Assert.Equal("claude-sonnet-5", input.Model);
        Assert.True(session.Stopped);
        Assert.Equal("Claude Sonnet", Client(session, made).ModelName(Chat));
        Assert.Null(Client(session, made).ModelName(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_reply_stops_at_the_output_cap_and_tokens_are_estimated_when_unreported()
    {
        var session = new ScriptedArtifactSession(new ProviderEvent.MessageDelta("m1", new string('a', 30)), new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        var deltas = new List<string>();
        var request = new ArtifactCompletionRequest("S", [new ArtifactModelMessage("user", "Hi"), new ArtifactModelMessage("assistant", "Hello"), new ArtifactModelMessage("user", "Again")], 5);

        var result = await Client(session, []).CompleteAsync(Chat, request, deltas.Add, CancellationToken.None);

        Assert.Equal(new string('a', 20), result.Text);
        Assert.Equal(5, result.OutputTokens);
        Assert.True(result.InputTokens > 0);
        Assert.Equal(20, string.Concat(deltas).Length);
        var text = Assert.Single(session.Inputs).Text;
        Assert.Contains("<user>\nHi\n</user>", text, StringComparison.Ordinal);
        Assert.Contains("<assistant>\nHello\n</assistant>", text, StringComparison.Ordinal);
        Assert.EndsWith("<user>\nAgain\n</user>\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_thread_without_a_model_or_a_failed_turn_rejects()
    {
        var failed = new ScriptedArtifactSession(new ProviderEvent.TurnCompleted(TurnStatus.Failed, "overloaded"));
        var request = new ArtifactCompletionRequest("S", [new ArtifactModelMessage("user", "x")], 100);
        var error = await Assert.ThrowsAsync<ArtifactBridgeException>(() => Client(failed, []).CompleteAsync(Chat, request, null, CancellationToken.None));
        Assert.Equal(ArtifactBridgeErrors.Failed, error.Code);
        var none = await Assert.ThrowsAsync<ArtifactBridgeException>(() => Client(failed, []).CompleteAsync(Guid.NewGuid(), request, null, CancellationToken.None));
        Assert.Equal(ArtifactBridgeErrors.Unavailable, none.Code);
    }
}
