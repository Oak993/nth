using System.Text.Json.Nodes;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Web;
using Xunit;

namespace Marrow.App.Tests.Runtime;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3, 7.4; ED-NEW-web-w24-5). The thread runtime between a session and its timeline:
// the events of a session pass through the artifact fallback and then the live-answer flow, the changes the tools make are applied to the rows
// on the main actor, a row is found again by its view id (after a restart too), and the reader's states are kept on it.
[Trait("Decision", "ED-NEW-web-w24-5")]
public sealed class ThreadRuntimeLiveAnswerTests
{
    private const string Answer =
        "At 400 a month it takes 30 months.\n"
        + "```nth-ui id=savings\n"
        + "card \"Savings goal\"\n"
        + "  state goal = 12000\n"
        + "  stat \"Goal\" = goal\n"
        + "```\n"
        + "Try the slider.";

    private const string Hosting = "card \"Hosting estimate\"\n  state instances = 3\n  stepper instances label=\"Instances\" min=1 max=24\n  text \"About three.\" id=summary\n";

    /// <summary>Waits for a task that needs the main actor while the harness's main loop runs.</summary>
    private static T Await<T>(ThreadRuntimePart12Harness h, Task<T> task)
    {
        h.Main.Loop.RunUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private static IReadOnlyList<TimelineEntry> Entries(ThreadRuntimePart12Harness h) => h.Main.OnMain(() => h.Runtime.Entries.ToList());

    private static void Deliver(ThreadRuntimePart12Harness h, ProviderEvent @event) => h.Main.OnMain(() => h.Runtime.DeliverSessionEvent(@event));

    private static ToolLiveView View(ThreadRuntimePart12Harness h, string entry) =>
        h.Main.OnMain(() => ThreadRuntime.LiveViewIn(h.Runtime.Entries.Single(candidate => candidate.Id == entry)))!;

    private static string TextOf(ThreadRuntimePart12Harness h, TimelineEntry entry) =>
        h.Main.OnMain(() => ((TimelineItem.Content.Assistant)entry.Item.TimelineItemContent).Value.Text);

    [Fact]
    public void A_fenced_view_in_a_streamed_answer_becomes_a_row_between_the_sentence_and_the_rest()
    {
        using var artifacts = new LiveArtifactScope();
        using var h = new ThreadRuntimePart12Harness();
        for (var at = 0; at < Answer.Length; at += 11) Deliver(h, new ProviderEvent.MessageDelta("m1", Answer[at..Math.Min(Answer.Length, at + 11)]));
        Deliver(h, new ProviderEvent.MessageCompleted("m1", Answer));

        var entries = Entries(h);
        Assert.Equal(["m1", "live-m1-0", "m1~1"], entries.Select(entry => entry.Id));
        Assert.Equal("At 400 a month it takes 30 months.\n", TextOf(h, entries[0]));
        Assert.Equal("Try the slider.", TextOf(h, entries[2]));
        var view = View(h, "live-m1-0");
        Assert.Equal(("savings", true, "Savings goal"), (view.Id, view.Complete, view.Title));
        Assert.Equal("card \"Savings goal\"\n  state goal = 12000\n  stat \"Goal\" = goal\n", view.Text);
        Assert.True(h.Main.OnMain(() => entries.All(entry => entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant { Value.IsStreaming: true })));
    }

    [Fact]
    public async Task A_call_of_ui_show_binds_to_the_providers_row_and_the_tools_record_reaches_the_main_actor()
    {
        using var artifacts = new LiveArtifactScope();
        using var h = new ThreadRuntimePart12Harness();
        var chat = h.Runtime.ThreadID;
        var json = System.Text.Json.JsonSerializer.Serialize(new { ui = Hosting, id = "hosting" });

        Deliver(h, new ProviderEvent.ToolStarted("tool-1", new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = LiveAnswerFlow.RowTitle("ui_show") }));
        Deliver(h, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Detail = json[..30] }));
        Assert.False(View(h, "tool-1").Complete); // the model is still writing it
        for (var at = 47; at < json.Length; at += 17) Deliver(h, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Detail = json[..at] }));
        Assert.StartsWith("card \"Hosting", View(h, "tool-1").Text, StringComparison.Ordinal);

        // The nth-ui server records the view on its own thread; the row hears of it on the main actor.
        var result = await Task.Run(() => LiveAnswerTools.Run(artifacts.Host.Live, chat, false, "ui_show", JsonNode.Parse(json)));
        Assert.False(result.Failed, result.Text);
        h.Main.OnMain(() => { });
        Deliver(h, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Detail = json, Output = result.Text, Status = ToolCall.Status.Completed }));

        var row = View(h, "tool-1");
        Assert.Equal(("hosting", true, 1, Hosting), (row.Id, row.Complete, row.Version, row.Text));
        Assert.Equal(["tool-1"], Entries(h).Select(entry => entry.Id));
        Assert.Equal(ToolCall.Status.Completed, h.Main.OnMain(() => ((TimelineItem.Content.Tool)h.Runtime.Entries[0].Item.TimelineItemContent).Value.ToolCallStatus));

        // ui_update changes it in place, keeps what the reader set, and lays the patch's states over it.
        h.Main.OnMain(() => h.Runtime.SetLiveViewState("tool-1", "{\"instances\":8}"));
        var update = await Task.Run(() => LiveAnswerTools.Run(artifacts.Host.Live, chat, false, "ui_update",
            JsonNode.Parse("{\"id\":\"hosting\",\"patch\":{\"nodes\":{\"summary\":\"text \\\"About eight.\\\" id=summary\"}}}")));
        Assert.False(update.Failed, update.Text);
        h.Main.OnMain(() => { });
        var changed = View(h, "tool-1");
        Assert.Contains("About eight.", changed.Text, StringComparison.Ordinal);
        Assert.Equal((2, "{\"instances\":8}"), (changed.Version, changed.State));
        Assert.Single(Entries(h));
    }

    [Fact]
    public void A_view_from_an_earlier_session_is_found_through_the_saved_timeline_and_updated_in_place()
    {
        using var h = new ThreadRuntimePart12Harness();
        var chat = h.Runtime.ThreadID;
        var live = new LiveAnswerHost(lookup: (thread, id) => MainActor.Run(() => thread == chat ? h.Runtime.FindLiveView(id) : null));
        using var artifacts = new LiveArtifactScope(live);
        // The row as an earlier session saved it.
        h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolStarted("old-tool", new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Other,
            Title = "Live answers",
            ToolCallStatus = ToolCall.Status.Completed,
            Payload = new ToolPayload { LiveView = new ToolLiveView { Id = "hosting", Text = Hosting, Version = 4, Complete = true, State = "{\"instances\":6}" } },
        })));
        // A session starts: one event is enough to start the chat's flow, as the first event of any turn does.
        Deliver(h, new ProviderEvent.TurnStarted(null));

        var found = Await(h, Task.Run(() => artifacts.Host.Live.Find(chat, "hosting")));
        Assert.Equal((4, Hosting), (found!.Version, found.Text));
        var result = Await(h, Task.Run(() => LiveAnswerTools.Run(artifacts.Host.Live, chat, false, "ui_update",
            JsonNode.Parse("{\"id\":\"hosting\",\"patch\":{\"state\":{\"instances\":9},\"nodes\":{\"summary\":\"text \\\"Nine.\\\" id=summary\"}}}"))));
        Assert.False(result.Failed, result.Text);
        h.Main.OnMain(() => { });
        var view = View(h, "old-tool");
        Assert.Contains("Nine.", view.Text, StringComparison.Ordinal);
        Assert.Equal((5, "{\"instances\":9}"), (view.Version, view.State));
        Assert.Single(Entries(h));
        Assert.Null(h.Main.OnMain(() => h.Runtime.FindLiveView("nothing")));
    }

    [Fact]
    public void The_readers_states_are_kept_on_the_row_and_a_second_view_with_the_same_id_supersedes_the_first()
    {
        using var artifacts = new LiveArtifactScope();
        using var h = new ThreadRuntimePart12Harness();
        Deliver(h, new ProviderEvent.MessageCompleted("m1", "One.\n```nth-ui id=plan\ncard \"v1\"\n  state n = 1\n```\n"));
        h.Main.OnMain(() => h.Runtime.SetLiveViewState("live-m1-0", "{\"n\":5}"));
        Assert.Equal("{\"n\":5}", View(h, "live-m1-0").State);
        h.Main.OnMain(() => h.Runtime.SetLiveViewState("live-m1-0", "{\"n\":5}")); // the same state: nothing to save
        h.Main.OnMain(() => h.Runtime.SetLiveViewState("nope", "{}")); // no such row
        h.Main.OnMain(() => h.Runtime.SetLiveViewState("live-m1-0", new string('x', ToolLiveView.MaxStateLength + 1))); // too big to keep
        Assert.Equal("{\"n\":5}", View(h, "live-m1-0").State);

        Deliver(h, new ProviderEvent.MessageCompleted("m2", "Again.\n```nth-ui id=plan\ncard \"v2\"\n```\n"));
        Assert.True(View(h, "live-m1-0").Superseded);
        Assert.False(View(h, "live-m2-0").Superseded);
        Assert.Equal(2, h.Main.OnMain(() => h.Runtime.FindLiveView("plan"))!.Version);
    }

    [Fact]
    public void With_artifacts_off_or_live_answers_off_an_answer_is_shown_as_the_session_wrote_it()
    {
        using var h = new ThreadRuntimePart12Harness();
        Deliver(h, new ProviderEvent.MessageCompleted("m1", Answer)); // artifacts off in this process
        Assert.Equal(["m1"], Entries(h).Select(entry => entry.Id));
        Assert.Equal(Answer, TextOf(h, Entries(h)[0]));

        using var artifacts = new LiveArtifactScope(new LiveAnswerHost(enabled: () => false));
        using var h2 = new ThreadRuntimePart12Harness();
        Deliver(h2, new ProviderEvent.MessageCompleted("m1", Answer));
        Assert.Equal(["m1"], Entries(h2).Select(entry => entry.Id));
    }
}
