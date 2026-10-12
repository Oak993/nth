using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3, 7.4; ED-NEW-web-w24-5). Between a chat's session and its timeline: a fenced
// nth-ui or nth-html block, a ui_show or html_widget call and a ui_update call become live-answer rows that grow while the model writes,
// whichever way the model made them and whichever provider it runs on. No model provider is called; the events are written here, in
// the shapes the sessions emit.
[Trait("Decision", "ED-NEW-web-w24-5")]
public sealed class LiveAnswerFlowTests
{
    private static readonly Guid Chat = Guid.Parse("2f6d1a90-3b7c-4d58-a1e4-6c0b9e8d7f13");

    private const string Hosting =
        "card \"Hosting estimate\" icon=server\n"
        + "  state instances = 3\n"
        + "  stepper instances label=\"Instances\" min=1 max=24\n"
        + "  text \"About three instances.\" id=summary\n";

    private static readonly string ShowTitle = LiveAnswerFlow.RowTitle("ui_show");

    private static (LiveAnswerFlow Flow, LiveTimelineSim Sim, LiveAnswerHost Host) Make(bool lite = false)
    {
        var host = new LiveAnswerHost();
        var sim = new LiveTimelineSim();
        return (new LiveAnswerFlow(Chat, host, lite, sim.Lookup), sim, host);
    }

    private static void Stream(LiveAnswerFlow flow, LiveTimelineSim sim, string id, string text, int piece)
    {
        for (var at = 0; at < text.Length; at += piece)
            sim.Feed(flow, new ProviderEvent.MessageDelta(id, text[at..Math.Min(text.Length, at + piece)]));
        sim.Feed(flow, new ProviderEvent.MessageCompleted(id, text));
    }

    private static LiveViewChange Shown(LiveAnswerHost host, string id, string text, LiveViewKind kind = LiveViewKind.Ui, int? height = null) =>
        host.Show(Chat, new LiveViewRecord(id, kind, text, NthUiPatch.TitleOf(NthUiParser.Parse(text)), 1, false, height), raise: false);

    private static string Json(string ui, string id = "") => JsonSerializer.Serialize(id.Length == 0 ? new Dictionary<string, string> { ["ui"] = ui } : new Dictionary<string, string> { ["id"] = id, ["ui"] = ui });

    // ------------------------------------------------------------------------------------------------------------------
    // Fenced blocks.
    // ------------------------------------------------------------------------------------------------------------------

    private const string Answer =
        "At 400 a month it takes 30 months.\n"
        + "```nth-ui id=savings\n"
        + "card \"Savings goal\"\n"
        + "  state goal = 12000\n"
        + "  stat \"Goal\" = goal\n"
        + "```\n"
        + "Try the slider.";

    [Fact]
    public void An_answer_with_no_block_comes_out_exactly_as_it_went_in()
    {
        var (flow, _, _) = Make();
        foreach (var @event in new ProviderEvent[]
        {
            new ProviderEvent.MessageDelta("m1", "Canberra. "),
            new ProviderEvent.MessageDelta("m1", "Sydney is bigger."),
            new ProviderEvent.MessageCompleted("m1", "Canberra. Sydney is bigger."),
            new ProviderEvent.ReasoningDelta("r1", "thinking"),
            new ProviderEvent.ToolStarted("t1", new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "ls" }),
            new ProviderEvent.ToolUpdated("t1", new ToolUpdate { Output = "a", Status = ToolCall.Status.Completed }),
            new ProviderEvent.ToolOutput("t1", "more"),
            new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "hi" }),
        })
            Assert.Equal([@event], flow.Filter(@event));

        // A line that could have been an opener is held back, then shown.
        var held = flow.Filter(new ProviderEvent.MessageDelta("m2", "Look:\n``"));
        Assert.Equal([new ProviderEvent.MessageDelta("m2", "Look:\n")], held);
        Assert.Equal([new ProviderEvent.MessageDelta("m2", "``` and so on")], flow.Filter(new ProviderEvent.MessageDelta("m2", "` and so on")));
        var done = new ProviderEvent.MessageCompleted("m2", "Look:\n``` and so on");
        Assert.Equal([done], flow.Filter(done));
    }

    [Fact]
    public void A_fenced_view_becomes_a_row_between_the_sentence_and_the_rest_whatever_the_size_of_the_pieces()
    {
        foreach (var piece in new[] { 1, 3, 7, 40, Answer.Length })
        {
            var (flow, sim, host) = Make();
            Stream(flow, sim, "m1", Answer, piece);
            sim.Feed(flow, new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));

            Assert.Equal(["m1", "live-m1-0", "m1~1"], sim.Order);
            Assert.Equal(["At 400 a month it takes 30 months.\n", "Try the slider."], sim.Texts);
            Assert.True(sim.IsComplete("m1") && sim.IsComplete("m1~1"), $"piece {piece}");
            var row = sim.Rows["live-m1-0"];
            var view = row.View!;
            Assert.Equal(("savings", LiveViewKind.Ui, "Savings goal", true, 1), (view.Id, view.Kind, view.Title, view.Complete, view.Version));
            Assert.Equal("card \"Savings goal\"\n  state goal = 12000\n  stat \"Goal\" = goal\n", view.Text);
            Assert.Equal((ToolCall.Status.Completed, "Live answers · Savings goal"), (row.Status, row.Title));
            Assert.Equal("savings", Recorded(host, "savings")!.Id); // the tools' store holds it for ui_update
        }
    }

    private static LiveViewRecord? Recorded(LiveAnswerHost host, string id) => host.Find(Chat, id).GetAwaiter().GetResult();

    [Fact]
    public void The_row_grows_as_the_lines_arrive_and_never_loses_what_it_showed()
    {
        var (flow, sim, _) = Make();
        var previous = "";
        var texts = new List<string>();
        var sawRunning = false;
        for (var at = 0; at < Answer.Length; at += 5)
        {
            sim.Feed(flow, new ProviderEvent.MessageDelta("m1", Answer[at..Math.Min(Answer.Length, at + 5)]));
            if (!sim.Rows.TryGetValue("live-m1-0", out var row)) continue;
            Assert.StartsWith(previous, row.View!.Text, StringComparison.Ordinal);
            previous = row.View.Text;
            texts.Add(previous);
            sawRunning |= !row.View.Complete && row.Status == ToolCall.Status.Running;
        }
        Assert.True(texts.Count > 5, "the row was seen growing");
        Assert.True(sawRunning, "the row was running while the lines arrived");
        Assert.Contains(texts, text => !text.EndsWith('\n')); // a line still arriving is handed over
        Assert.True(sim.Rows["live-m1-0"].View!.Complete);
        Assert.Equal(ToolCall.Status.Completed, sim.Rows["live-m1-0"].Status);
    }

    [Fact]
    public void An_answer_that_arrives_whole_is_read_at_once_in_the_same_order()
    {
        var (flow, sim, _) = Make();
        sim.Feed(flow, new ProviderEvent.MessageCompleted("m1", Answer));
        Assert.Equal(["m1", "live-m1-0", "m1~1"], sim.Order);
        Assert.Equal(["At 400 a month it takes 30 months.\n", "Try the slider."], sim.Texts);
        Assert.True(sim.Rows["live-m1-0"].View!.Complete);
        Assert.Equal(ToolCall.Status.Completed, sim.Rows["live-m1-0"].Status);
    }

    [Fact]
    public void An_answer_that_starts_with_the_block_has_no_empty_message_before_it_and_one_that_ends_with_it_has_none_after()
    {
        var (flow, sim, _) = Make();
        Stream(flow, sim, "m1", "```nth-ui\ntext \"Only this\"\n```\n", 4);
        Assert.Equal(["live-m1-0"], sim.Order);
        Assert.Equal("text \"Only this\"\n", sim.Rows["live-m1-0"].View!.Text);
        Assert.Matches("^view-[0-9a-f]{4}$", sim.Rows["live-m1-0"].View!.Id); // nth named it
    }

    [Fact]
    public void Two_blocks_in_one_answer_make_two_rows_and_three_messages()
    {
        var (flow, sim, _) = Make();
        Stream(flow, sim, "m1", "One.\n```nth-ui id=a\ntext \"A\"\n```\nTwo.\n```nth-ui id=b\ntext \"B\"\n```\nThree.", 6);
        Assert.Equal(["m1", "live-m1-0", "m1~1", "live-m1-1", "m1~2"], sim.Order);
        Assert.Equal(["One.\n", "Two.\n", "Three."], sim.Texts);
        Assert.Equal(["a", "b"], sim.LiveRows.Select(row => row.View!.Id));
    }

    [Fact]
    public void A_fenced_view_with_an_id_already_shown_supersedes_the_earlier_row()
    {
        var (flow, sim, host) = Make();
        Stream(flow, sim, "m1", "First.\n```nth-ui id=plan\ntext \"v1\"\n```\n", 5);
        Stream(flow, sim, "m2", "Again.\n```nth-ui id=plan\ntext \"v2\"\n```\n", 5);
        Assert.True(sim.Rows["live-m1-0"].View!.Superseded);
        Assert.False(sim.Rows["live-m2-0"].View!.Superseded);
        Assert.Equal("text \"v2\"\n", sim.Rows["live-m2-0"].View!.Text);
        Assert.Equal(2, Recorded(host, "plan")!.Version);
    }

    [Fact]
    public void A_fenced_html_widget_keeps_its_page_height_and_title()
    {
        var (flow, sim, _) = Make();
        Stream(flow, sim, "m1", "Play:\n```nth-html height=400\n<html><head><title>Pong</title></head><body>x</body></html>\n```\n", 9);
        var view = sim.Rows["live-m1-0"].View!;
        Assert.Equal((LiveViewKind.Html, 400, "Pong", true), (view.Kind, view.Height, view.Title, view.Complete));
        Assert.Equal("<html><head><title>Pong</title></head><body>x</body></html>", view.Text);
    }

    [Fact]
    public void A_block_the_turn_ends_inside_closes_as_cut_off_and_an_empty_block_fails()
    {
        var (flow, sim, _) = Make();
        sim.Feed(flow, new ProviderEvent.MessageDelta("m1", "Here:\n```nth-ui\ncard \"Half\"\n  text \"still arri"));
        Assert.False(sim.Rows["live-m1-0"].View!.Complete);
        var output = sim.Feed(flow, new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
        Assert.IsType<ProviderEvent.TurnCompleted>(output[^1]);
        var row = sim.Rows["live-m1-0"];
        Assert.True(row.View!.Complete);
        Assert.EndsWith("still arri", row.View.Text, StringComparison.Ordinal);
        Assert.Equal(ToolCall.Status.Completed, row.Status);

        var (flow2, sim2, _) = Make();
        Stream(flow2, sim2, "m1", "Oops:\n```nth-ui\n```\n", 3);
        Assert.Equal((ToolCall.Status.Failed, "The live answer was empty."), (sim2.Rows["live-m1-0"].Status, sim2.Rows["live-m1-0"].View!.Problem));
    }

    [Fact]
    public void A_local_flow_marks_its_views_lite_and_a_header_can_ask_for_it()
    {
        var (flow, sim, _) = Make(lite: true);
        Stream(flow, sim, "m1", "A.\n```nth-ui\ntext \"x\"\n```\n", 4);
        Assert.True(sim.Rows["live-m1-0"].View!.Lite);
        var (flow2, sim2, _) = Make();
        Stream(flow2, sim2, "m1", "A.\n```nth-ui lite\ntext \"x\"\n```\n", 4);
        Assert.True(sim2.Rows["live-m1-0"].View!.Lite);
        var (flow3, sim3, _) = Make(lite: true);
        Stream(flow3, sim3, "m1", "A.\n```nth-html\n<p>x</p>\n```\n", 4);
        Assert.False(sim3.Rows["live-m1-0"].View!.Lite);
    }

    [Fact]
    public void A_view_inside_the_answers_own_code_fence_is_text_and_the_message_is_not_split()
    {
        var (flow, sim, _) = Make();
        const string text = "Write it like this:\n```markdown\n```nth-ui\ncard \"Not me\"\n```\nThat is all.";
        Stream(flow, sim, "m1", text, 5);
        Assert.Equal(["m1"], sim.Order);
        Assert.Equal(text, sim.Message("m1"));
        Assert.Empty(sim.LiveRows);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Tool calls.
    // ------------------------------------------------------------------------------------------------------------------

    private static ProviderEvent Started(string id, string tool, string? detail = null) =>
        new ProviderEvent.ToolStarted(id, new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = LiveAnswerFlow.RowTitle(tool), Detail = detail });

    [Fact]
    public void A_ui_show_call_streams_into_its_own_row_and_settles_on_what_the_tool_recorded()
    {
        var (flow, sim, host) = Make();
        var json = Json(Hosting, "hosting");
        sim.Feed(flow, Started("tool-1", "ui_show"));
        var row = sim.Rows["tool-1"];
        Assert.Equal(("Live answers", ToolCall.Status.Running, LiveViewKind.Ui), (row.Title, row.Status, row.View!.Kind));
        Assert.Equal("", row.View.Text);

        var seen = new List<string>();
        for (var at = 7; at < json.Length; at += 9)
        {
            sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Detail = json[..at] }));
            seen.Add(row.View!.Text);
        }
        Assert.True(seen.Count > 5);
        for (var i = 1; i < seen.Count; i++) Assert.StartsWith(seen[i - 1], seen[i], StringComparison.Ordinal);
        Assert.Equal("hosting", row.View!.Id);
        Assert.Equal("Hosting estimate", row.View.Title);
        Assert.False(row.View.Complete);
        Assert.Null(row.Detail); // the raw arguments never show

        // The call ends: complete arguments and the result text the model reads (not shown).
        sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Detail = json, Title = "nth-ui · ui_show" }));
        Assert.True(row.View.Complete);
        Assert.Equal(Hosting, row.View.Text);
        sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Output = "Showed the live view", Status = ToolCall.Status.Completed }));
        Assert.Equal((ToolCall.Status.Completed, ""), (row.Status, row.Output));

        // The tools recorded the same view: it binds to this row and adds nothing new.
        var before = sim.Order.Count;
        sim.Feed(flow, Shown(host, "hosting", Hosting.TrimEnd('\n')));
        Assert.Equal(before, sim.Order.Count);
        Assert.Equal(("hosting", 1, true), (row.View.Id, row.View.Version, row.View.Complete));
        Assert.Equal("Live answers · Hosting estimate", row.Title);
    }

    [Fact]
    public void A_call_whose_arguments_arrive_whole_shows_the_view_at_once()
    {
        var (flow, sim, _) = Make();
        sim.Feed(flow, Started("tool-1", "ui_show", Json(Hosting)));
        var view = sim.Rows["tool-1"].View!;
        Assert.Equal(Hosting, view.Text);
        Assert.True(view.Complete); // the arguments are whole
        Assert.Equal(ToolCall.Status.Running, sim.Rows["tool-1"].Status);
        sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Status = ToolCall.Status.Completed }));
        Assert.Equal(ToolCall.Status.Completed, sim.Rows["tool-1"].Status);
    }

    [Fact]
    public void A_change_that_beats_the_providers_report_makes_a_row_and_the_providers_row_is_the_same_view()
    {
        var (flow, sim, host) = Make();
        sim.Feed(flow, Shown(host, "hosting", Hosting.TrimEnd('\n')));
        Assert.Equal(["live-hosting-1"], sim.Order);
        Assert.Equal((true, ToolCall.Status.Completed), (sim.Rows["live-hosting-1"].View!.Complete, sim.Rows["live-hosting-1"].Status));

        var output = sim.Feed(flow, Started("tool-1", "ui_show", Json(Hosting)));
        Assert.Empty(output); // the same view: no second row
        Assert.Empty(sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Output = "Showed", Status = ToolCall.Status.Completed })));
        Assert.Single(sim.LiveRows);
        // The next call is a new view again.
        sim.Feed(flow, Started("tool-2", "ui_show", Json("text \"b\"")));
        Assert.Equal(2, sim.LiveRows.Count);
    }

    [Fact]
    public void A_second_ui_show_with_the_same_id_supersedes_the_first_row_and_takes_its_place_below()
    {
        var (flow, sim, host) = Make();
        sim.Feed(flow, Started("tool-1", "ui_show", Json(Hosting, "hosting")));
        sim.Feed(flow, Shown(host, "hosting", Hosting.TrimEnd('\n')));
        sim.Feed(flow, Started("tool-2", "ui_show", Json("card \"Other\"\n  text \"b\"\n", "hosting")));
        Assert.False(sim.Rows["tool-1"].View!.Superseded);
        sim.Feed(flow, Shown(host, "hosting", "card \"Other\"\n  text \"b\""));
        Assert.True(sim.Rows["tool-1"].View!.Superseded);
        var second = sim.Rows["tool-2"].View!;
        Assert.Equal(("hosting", 2, true, false), (second.Id, second.Version, second.Complete, second.Superseded));
        Assert.Equal(["tool-1", "tool-2"], sim.Order);
    }

    [Fact]
    public void A_ui_update_call_is_hidden_and_changes_the_view_in_place_keeping_the_readers_states()
    {
        var (flow, sim, host) = Make();
        sim.Feed(flow, Started("tool-1", "ui_show", Json(Hosting, "hosting")));
        sim.Feed(flow, Shown(host, "hosting", Hosting.TrimEnd('\n')));
        // The reader moved the stepper: the runtime saved it on the row.
        sim.Rows["tool-1"].View = sim.Rows["tool-1"].View! with { State = "{\"instances\":8,\"other\":1}" };

        Assert.Empty(sim.Feed(flow, Started("tool-2", "ui_update", "{\"id\":\"hosting\"}")));
        Assert.Empty(sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-2", new ToolUpdate { Detail = "{\"id\":\"hosting\",\"patch\":{}}" })));
        Assert.Empty(sim.Feed(flow, new ProviderEvent.ToolOutput("tool-2", "Updated")));

        var update = host.Update(Chat, "hosting", Hosting.TrimEnd('\n').Replace("About three", "About five", StringComparison.Ordinal), null, "{\"instances\":5}", raise: false);
        sim.Feed(flow, update);
        Assert.Empty(sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-2", new ToolUpdate { Output = "Updated", Status = ToolCall.Status.Completed })));
        Assert.Equal(["tool-1"], sim.Order);
        var view = sim.Rows["tool-1"].View!;
        Assert.Contains("About five", view.Text, StringComparison.Ordinal);
        Assert.Equal(2, view.Version);
        Assert.Equal("{\"instances\":5,\"other\":1}", view.State); // the patch's states laid over the reader's
        Assert.True(view.Complete);
    }

    [Fact]
    public void An_update_for_a_view_whose_row_is_in_the_saved_timeline_finds_it_again_after_a_restart()
    {
        var (flow, sim, host) = Make();
        // A row saved by an earlier session: this flow knows nothing of it, and the chat's timeline holds it.
        sim.Apply([new ProviderEvent.ToolStarted("old-tool", new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Other, Title = "Live answers",
            Payload = new ToolPayload { LiveView = new ToolLiveView { Id = "hosting", Text = Hosting.TrimEnd('\n'), Version = 3, Complete = true, State = "{\"instances\":4,\"region\":\"eu\"}" } },
        })]);
        // The tools found the view through the lookup seam and recorded the update.
        host.Show(Chat, new LiveViewRecord("hosting", LiveViewKind.Ui, Hosting.TrimEnd('\n'), "Hosting estimate", 3, false, null), raise: false);
        var change = host.Update(Chat, "hosting", "text \"new\" id=summary", "New", "{\"region\":\"us\"}", raise: false);
        sim.Feed(flow, change);
        Assert.Equal(["old-tool"], sim.Order);
        var view = sim.Rows["old-tool"].View!;
        Assert.Equal(("text \"new\" id=summary", "{\"instances\":4,\"region\":\"us\"}", true), (view.Text, view.State, view.Complete));
        Assert.Equal(change.View.Version, view.Version);
    }

    [Fact]
    public void Calls_in_flight_at_once_bind_by_the_id_each_names()
    {
        var (flow, sim, host) = Make();
        sim.Feed(flow, Started("tool-a", "ui_show", Json("card \"A\"\n  text \"a\"\n", "a")));
        sim.Feed(flow, Started("tool-b", "ui_show", Json("card \"B\"\n  text \"b\"\n", "b")));
        sim.Feed(flow, Shown(host, "b", "card \"B\"\n  text \"b\"\n")); // the second call's view is recorded first
        sim.Feed(flow, Shown(host, "a", "card \"A\"\n  text \"a\"\n"));
        Assert.Equal(("a", "b"), (sim.Rows["tool-a"].View!.Id, sim.Rows["tool-b"].View!.Id));
        Assert.Equal(["tool-a", "tool-b"], sim.Order);
        Assert.All(sim.LiveRows, row => Assert.True(row.View!.Complete));
    }

    [Fact]
    public void An_update_for_a_row_the_chat_no_longer_holds_makes_a_row_of_its_own()
    {
        var (flow, sim, host) = Make();
        sim.Feed(flow, Started("tool-1", "ui_show", Json(Hosting, "hosting")));
        sim.Feed(flow, Shown(host, "hosting", Hosting));
        sim.Remove("tool-1"); // the chat was rewound past it
        sim.Feed(flow, host.Update(Chat, "hosting", Hosting.Replace("three", "five", StringComparison.Ordinal), null, null, raise: false));
        var row = Assert.Single(sim.LiveRows);
        Assert.Equal(("live-hosting-2", "hosting", 2, true), (row.Id, row.View!.Id, row.View.Version, row.View.Complete));
        Assert.Contains("five", row.View.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_call_leaves_a_calm_row_with_the_reason()
    {
        var (flow, sim, _) = Make();
        sim.Feed(flow, Started("tool-1", "ui_show", "{\"ui\":\"{not a view}\"}"));
        sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Output = "Error: No line of the view could be read as a component, so nothing was shown. line 1: ...", Status = ToolCall.Status.Failed }));
        var row = sim.Rows["tool-1"];
        Assert.Equal(ToolCall.Status.Failed, row.Status);
        Assert.StartsWith("No line of the view could be read", row.View!.Problem, StringComparison.Ordinal);
        Assert.True(row.View.Complete);
    }

    [Fact]
    public void A_row_the_flow_cannot_recognise_stays_as_it_is_and_the_tools_change_makes_a_row_of_its_own()
    {
        var (flow, sim, host) = Make();
        var tool = new ProviderEvent.ToolStarted("acp-1", new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = "Tool" });
        Assert.Equal([tool], flow.Filter(tool));
        var other = new ProviderEvent.ToolStarted("srv-1", new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = "shipyard · ui_show" });
        Assert.Equal([other], flow.Filter(other)); // another server's tool of the same name is not ours
        sim.Feed(flow, Shown(host, "hosting", Hosting.TrimEnd('\n')));
        Assert.Equal("live-hosting-1", Assert.Single(sim.LiveRows).Id);
    }

    [Fact]
    public void An_html_call_streams_the_page_and_a_row_left_waiting_at_the_end_of_the_turn_settles()
    {
        var (flow, sim, _) = Make();
        var json = JsonSerializer.Serialize(new { html = "<html><head><title>Pong</title></head><body>go</body></html>", height = 420 });
        sim.Feed(flow, Started("tool-1", "html_widget", json[..40]));
        Assert.Equal(LiveViewKind.Html, sim.Rows["tool-1"].View!.Kind);
        Assert.False(sim.Rows["tool-1"].View!.Complete);
        sim.Feed(flow, new ProviderEvent.ToolUpdated("tool-1", new ToolUpdate { Detail = json }));
        var view = sim.Rows["tool-1"].View!;
        Assert.Equal(("<html><head><title>Pong</title></head><body>go</body></html>", 420), (view.Text, view.Height));

        // No change ever came from the tools: the turn ends and the row settles on what it has.
        sim.Feed(flow, new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        Assert.True(sim.Rows["tool-1"].View!.Complete);
        Assert.Equal(ToolCall.Status.Completed, sim.Rows["tool-1"].Status);

        var (flow2, sim2, _) = Make();
        sim2.Feed(flow2, Started("tool-9", "ui_show"));
        sim2.Feed(flow2, new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        Assert.Equal((ToolCall.Status.Failed, true), (sim2.Rows["tool-9"].Status, sim2.Rows["tool-9"].View!.Problem is not null));
    }

    [Fact]
    public void Heads_approvals_and_other_events_pass_through_untouched()
    {
        var (flow, _, _) = Make();
        ProviderEvent[] others =
        [
            new ProviderEvent.AgentEvent("a1", new ProviderEvent.MessageDelta("m", "```nth-ui\nhi")),
            new ProviderEvent.Todos(ValueList<TodoStep>.Empty),
            new ProviderEvent.SessionReady("s"),
            new ProviderEvent.TurnStarted(null),
        ];
        foreach (var @event in others) Assert.Equal([@event], flow.Filter(@event));
    }
}
