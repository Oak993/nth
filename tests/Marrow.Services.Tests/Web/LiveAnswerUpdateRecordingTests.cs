using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// DESIGN.md 14 row W24, "each tool across providers ... recorded fixtures" for ui_update and html_widget (web surfaces W24; ED-NEW-web-w24-5).
// tests/fixtures/web-surfaces/teaching/live-<provider>.jsonl: how five providers' models are recorded as changing a live view they showed
// earlier and as making a small HTML widget, replayed through each provider's real session code and the live-answer flow, after the earlier
// view is set up. Claude, Codex and DeepSeek call ui_update and html_widget; Copilot and a Local model write the whole block again with the
// same id, and Copilot an nth-html block (a Local model has no widget). The recordings are Marrow-authored synthetic transcripts.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-web-w24-5")]
public sealed class LiveAnswerUpdateRecordingTests
{
    private static readonly Guid Chat = LiveAnswerRecordingTests.Chat;

    private const string Base =
        "card \"Savings goal\" icon=piggy-bank\n"
        + "  state goal = 12000\n"
        + "  state monthly = 400\n"
        + "  let months = ceil(goal / monthly)\n"
        + "  slider monthly label=\"Per month\" min=50 max=2000 step=50 unit=\"$\"\n"
        + "  stat \"Months to goal\" = months\n"
        + "  text \"At 400 a month it takes 30 months.\" id=note\n";

    /// <summary>The earlier turn, as the tools recorded it.</summary>
    private static void SetUpHost(ArtifactHostScope scope, bool lite = false)
    {
        scope.Host.Live.Forget(Chat);
        scope.Host.Live.Show(Chat, new LiveViewRecord("savings", LiveViewKind.Ui, Base, "Savings goal", 1, lite, null), raise: false);
    }

    /// <summary>The earlier turn, as the chat's timeline holds it: the view's row, with the reader's states on it.</summary>
    private static void SetUp(LiveTimelineSim sim, bool lite) =>
        sim.Apply([new ProviderEvent.ToolStarted("earlier", new ToolCall
        {
            ToolCallKind = ToolCall.Kind.Other,
            Title = "Live answers · Savings goal",
            ToolCallStatus = ToolCall.Status.Completed,
            Payload = new ToolPayload { LiveView = new ToolLiveView { Id = "savings", Text = Base, Version = 1, Complete = true, Title = "Savings goal", Lite = lite, State = "{\"goal\":12000,\"monthly\":400}" } },
        })]);

    private static LiveAnswerRecordingTests.Replayed Replay(string provider, IReadOnlyList<ProviderEvent> events, TeachingOutcome outcome, ArtifactHostScope scope, bool lite = false, bool fenced = false)
    {
        var script = fenced
            ? events.SelectMany(new ArtifactFenceFilter(Chat, scope.Host, () => (new ArtifactAuthor(provider, null, null), null)).Filter).Cast<object>().ToList()
            : LiveAnswerRecordingTests.WithServer(events, outcome, scope, provider switch { "claude" => ProviderKind.Claude, "codex" => ProviderKind.Codex, _ => ProviderKind.Copilot }).GetAwaiter().GetResult();
        return LiveAnswerRecordingTests.Read(script, scope.Host.Live, lite, sim => SetUp(sim, lite));
    }

    private static void CheckUpdatedInPlace(string id, LiveAnswerRecordingTests.Replayed replayed, LiveAnswerHost host)
    {
        var sim = replayed.Sim;
        Assert.Equal(1, replayed.Rows);
        var row = Assert.Single(sim.LiveRows);
        Assert.Equal("earlier", row.Id);
        var view = row.View!;
        Assert.Equal((2, true, false), (view.Version, view.Complete, view.Superseded));
        Assert.Contains("six months sooner", view.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("At 400 a month", view.Text, StringComparison.Ordinal);
        Assert.Equal("{\"goal\":12000,\"monthly\":500}", view.State); // the patch's state laid over what the reader had
        Assert.True(NthUiParser.Parse(view.Text).IsClean, id);
        Assert.Equal(2, host.Find(Chat, "savings").GetAwaiter().GetResult()!.Version);
        Assert.DoesNotContain(sim.Rows.Keys, key => key != "earlier"); // the update call left no row of its own
        Assert.Contains("24 months", sim.Texts[0], StringComparison.Ordinal); // the sentence that goes with the change
    }

    private static void CheckReplaced(string id, LiveAnswerRecordingTests.Replayed replayed, LiveAnswerHost host, bool lite)
    {
        var sim = replayed.Sim;
        Assert.Equal(2, replayed.Rows);
        Assert.True(sim.Rows["earlier"].View!.Superseded, id);
        var again = sim.LiveRows.Single(row => row.Id != "earlier");
        Assert.Equal(("savings", true, false, lite), (again.View!.Id, again.View.Complete, again.View.Superseded, again.View.Lite));
        Assert.Contains("state monthly = 500", again.View.Text, StringComparison.Ordinal);
        Assert.Equal(2, host.Find(Chat, "savings").GetAwaiter().GetResult()!.Version);
        Assert.Equal(ToolCall.Status.Completed, again.Status);
    }

    private static void CheckWidget(string id, LiveAnswerRecordingTests.Replayed replayed, LiveAnswerHost host)
    {
        Assert.Equal(2, replayed.Rows); // the earlier view and the widget
        var row = Assert.Single(replayed.Sim.LiveRows, candidate => candidate.View!.Kind == LiveViewKind.Html);
        var view = row.View!;
        Assert.Equal((LiveViewKind.Html, 300, "Tap counter", true), (view.Kind, view.Height, view.Title, view.Complete));
        Assert.StartsWith("<!doctype html>", view.Text, StringComparison.Ordinal);
        Assert.Contains("document.getElementById(\"tap\")", view.Text, StringComparison.Ordinal);
        Assert.EndsWith("</html>", view.Text.TrimEnd(), StringComparison.Ordinal);
        Assert.Equal(ToolCall.Status.Completed, row.Status);
        Assert.StartsWith("Live answers", row.Title, StringComparison.Ordinal);
        Assert.Equal(LiveViewKind.Html, host.Find(Chat, view.Id).GetAwaiter().GetResult()!.Kind);
    }

    private static TeachingRecording Recording(string provider, string id) => TeachingReplay.Load(provider, "live-").Single(recording => recording.Id == id);

    [Fact]
    public void The_recordings_exist_for_each_provider_that_can_give_them()
    {
        foreach (var provider in TeachingReplay.Providers)
        {
            var recordings = TeachingReplay.Load(provider, "live-");
            Assert.Equal(provider == "local" ? ["11-update"] : ["11-update", "12-widget"], recordings.Select(recording => recording.Id));
            Assert.All(recordings, recording => Assert.NotEmpty(recording.Events));
            var bytes = File.ReadAllBytes(Path.Combine(TeachingReplay.FixtureFolder(), "live-" + provider + ".jsonl"));
            Assert.All(bytes, b => Assert.True(b < 128 && b != '\r', provider + " is not plain ASCII with LF line ends"));
        }
    }

    [Fact]
    public void Claude_calls_ui_update_and_html_widget_and_the_view_changes_in_place()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        var update = Recording("claude", "11-update");
        var events = TeachingReplay.ClaudeEvents(update);
        var outcome = TeachingReplay.Outcome(events);
        Assert.Equal(["ui_update"], outcome.Calls.Select(call => call.Tool));
        SetUpHost(scope);
        CheckUpdatedInPlace("claude/11", Replay("claude", events, outcome, scope), scope.Host.Live);

        SetUpHost(scope);
        var widgetEvents = TeachingReplay.ClaudeEvents(Recording("claude", "12-widget"));
        var widget = TeachingReplay.Outcome(widgetEvents);
        Assert.Equal(["html_widget"], widget.Calls.Select(call => call.Tool));
        CheckWidget("claude/12", Replay("claude", widgetEvents, widget, scope), scope.Host.Live);
    }


    [Fact]
    public void Codex_calls_ui_update_and_html_widget_through_MCP()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        SetUpHost(scope);
        var events = TeachingReplay.CodexEvents(Recording("codex", "11-update"));
        var outcome = TeachingReplay.Outcome(events);
        Assert.Equal(["ui_update"], outcome.Calls.Select(call => call.Tool));
        CheckUpdatedInPlace("codex/11", Replay("codex", events, outcome, scope), scope.Host.Live);

        SetUpHost(scope);
        var widgetEvents = TeachingReplay.CodexEvents(Recording("codex", "12-widget"));
        CheckWidget("codex/12", Replay("codex", widgetEvents, TeachingReplay.Outcome(widgetEvents), scope), scope.Host.Live);
    }

    [Fact]
    public void Copilot_writes_the_block_again_with_the_same_id_and_an_nth_html_block_for_the_widget()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        SetUpHost(scope);
        var events = TeachingReplay.CopilotEvents(Recording("copilot", "11-update"), runtime);
        CheckReplaced("copilot/11", Replay("copilot", events, TeachingReplay.Outcome(events), scope, fenced: true), scope.Host.Live, lite: false);

        SetUpHost(scope);
        var widgetEvents = TeachingReplay.CopilotEvents(Recording("copilot", "12-widget"), runtime);
        CheckWidget("copilot/12", Replay("copilot", widgetEvents, TeachingReplay.Outcome(widgetEvents), scope, fenced: true), scope.Host.Live);
    }

    [Fact]
    public async Task DeepSeek_calls_the_native_tools_through_the_real_loop_for_both()
    {
        using var scope = new ArtifactHostScope(new ArtifactTurn(null, "recorded prompt", null));
        foreach (var id in new[] { "11-update", "12-widget" })
        {
            var recording = Recording("deepseek", id);
            SetUpHost(scope);
            var script = new List<object>();
            using var subscription = scope.Host.Live.Subscribe(Chat, change => { lock (script) script.Add(change); });
            var configuration = NativeSessionRig.MakeSessionConfiguration() with { IgnisThreadID = Chat };
            await using var rig = new TeachingSessionRig([TeachingReplay.Sse(recording.Events), TeachingReplay.FinalAnswer], configuration, session =>
            {
                session.SkillTools = SkillSessionFactory.NativeTools(configuration);
                var inner = session.OnEvent!;
                session.OnEvent = @event => { inner(@event); lock (script) script.Add(@event); };
            });
            await rig.Turn(recording.Prompt);
            List<object> ordered;
            lock (script) ordered = [.. script];
            var replayed = LiveAnswerRecordingTests.Read(ordered, scope.Host.Live, false, sim => SetUp(sim, false));
            if (id == "11-update") CheckUpdatedInPlace("deepseek/" + id, replayed, scope.Host.Live);
            else CheckWidget("deepseek/" + id, replayed, scope.Host.Live);
        }
    }

    [Fact]
    public async Task A_Local_model_writes_the_lite_block_again_with_the_same_id()
    {
        using var scope = new ArtifactHostScope(new ArtifactTurn(null, "recorded prompt", null));
        var recording = Recording("local", "11-update");
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var folder = new LocalProviderFolder();
        server.Reply("/v1/models", """{"data":[{"id":"tiny","context_length":8192,"supports_tool_calling":false}]}""");
        using var discovery = new Marrow.Services.Port.Local.LocalDiscovery([server.BaseURL]);
        var target = discovery.Resolve(Assert.Single(Assert.Single(await discovery.Discover()).Models).SelectionId)!;
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Local,
            WorkingDirectory = SwiftURL.Create("file:" + folder.Root.Replace('\\', '/'))!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
            IgnisThreadID = Chat,
        };
        var session = new Marrow.Services.Port.Local.LocalSession(configuration, target);
        session.SkillTools = SkillSessionFactory.NativeTools(configuration);
        session.SeedMcpToolsForTests([]);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        server.Reply("/v1/chat/completions", TeachingReplay.Sse(recording.Events));
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await session.Start();
            await session.Send(new TurnInput { Images = ValueList<Attachment>.Empty, Text = recording.Prompt, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build });
        }, LoopbackHttpServer.Guard));
        session.Stop();

        SetUpHost(scope, lite: true);
        CheckReplaced("local/11", Replay("local", events, TeachingReplay.Outcome(events), scope, lite: true, fenced: true), scope.Host.Live, lite: true);
    }
}
