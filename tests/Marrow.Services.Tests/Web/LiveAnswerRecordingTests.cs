using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// DESIGN.md 14 row W24, "each tool across providers; partial streams render; recorded fixtures" (web surfaces W24; ED-NEW-web-w24-5). The
// recorded answers of the teaching fixtures (and the live-answer ones beside them) are replayed through each provider's real session
// code, and what the session says is read again by the live-answer flow into the rows the chat shows: a plain question leaves no row,
// a view question leaves exactly one row that grew while the model wrote and ends complete with the very view it wrote, in the order
// sentence, row. Claude, Codex and DeepSeek call the tools; the tools run for real (the nth-ui server for the CLI providers, the native
// tool loop for DeepSeek) and record the view, which binds to the provider's row. Copilot and a Local model write fenced blocks.
// The recordings are Marrow-authored synthetic transcripts (tests/fixtures/web-surfaces/teaching and live); no model provider is called.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-web-w24-5")]
public sealed class LiveAnswerRecordingTests
{
    private static readonly Guid Chat = Guid.Parse("7e5b2c44-1a90-4f3d-8c71-0d6a9b3e2f55");

    /// <summary>What a replay made: the rows the chat shows, in order, and the texts of its messages.</summary>
    private sealed record Replayed(LiveTimelineSim Sim, int Rows, IReadOnlyList<string> Growth);

    /// <summary>Runs the events (and the tools' changes, where the provider's tools run) through the flow, noting how each live row grew.</summary>
    private static Replayed Read(IEnumerable<object> script, LiveAnswerHost host, bool lite = false)
    {
        var sim = new LiveTimelineSim();
        var flow = new LiveAnswerFlow(Chat, host, lite, sim.Lookup);
        var growth = new List<string>();
        foreach (var item in script)
        {
            switch (item)
            {
                case ProviderEvent @event: sim.Feed(flow, @event); break;
                case LiveViewChange change: sim.Feed(flow, change); break;
            }
            foreach (var row in sim.LiveRows) if (growth.Count == 0 || growth[^1] != row.View!.Text) growth.Add(row.View!.Text);
        }
        sim.Feed(flow, new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        return new Replayed(sim, sim.LiveRows.Count, growth);
    }

    private static string CleanView(string text) => NthUiPatch.Clean(text);

    /// <summary>Checks one replay against what its prompt expects.</summary>
    private static void Check(string provider, TeachingRecording recording, Replayed replayed, string? expectedView, LiveAnswerHost host)
    {
        var id = provider + "/" + recording.Id;
        var sim = replayed.Sim;
        if (recording.Expect != "view")
        {
            Assert.Equal(0, replayed.Rows);
            Assert.DoesNotContain(sim.Rows.Values, row => row.Title.StartsWith("Live answers", StringComparison.Ordinal));
            return;
        }
        Assert.Equal(1, replayed.Rows);
        var row = Assert.Single(sim.LiveRows);
        var view = row.View!;
        Assert.True(view.Complete, id + ": the row ended complete");
        Assert.True(view.Problem is null, id + ": " + view.Problem);
        Assert.Equal(ToolCall.Status.Completed, row.Status);
        Assert.Equal(CleanView(expectedView!).TrimEnd('\n'), view.Text.TrimEnd('\n'));
        Assert.False(view.Id.Length == 0, id + ": the view has an id");
        Assert.True(NthUiParser.Parse(view.Text, view.Lite ? NthUiProfile.Lite : NthUiProfile.Full).IsClean, id);
        Assert.StartsWith("Live answers", row.Title, StringComparison.Ordinal);

        // The row grew: it was seen with less than it ended with before it ended, and never lost text.
        Assert.True(replayed.Growth.Count >= 1, id);
        for (var i = 1; i < replayed.Growth.Count; i++) Assert.StartsWith(replayed.Growth[i - 1], replayed.Growth[i], StringComparison.Ordinal);

        // The sentence comes first, then the row, and no message repeats the view.
        var order = sim.Order;
        var rowAt = order.ToList().IndexOf(row.Id);
        Assert.True(rowAt > 0, id + ": the sentence is before the row");
        Assert.True(sim.Message(order[0]).Trim().Length > 0);
        Assert.DoesNotContain(sim.Texts, text => text.Contains("```", StringComparison.Ordinal) || text.Contains("card \"", StringComparison.Ordinal));

        // The tools' record holds it, so ui_update can find it.
        Assert.Equal(view.Text.TrimEnd('\n'), host.Find(Chat, view.Id).GetAwaiter().GetResult()?.Text.TrimEnd('\n'));
    }

    private static string ViewOf(TeachingRecording recording, IEnumerable<TeachingCall> calls, string text)
    {
        if (recording.Expect != "view") return "";
        if (calls.FirstOrDefault(call => call.Tool == "ui_show") is { } call) return call.Arguments["ui"]!.GetValue<string>();
        var parts = LiveFenceParser.Parse(text);
        return string.Concat(parts.OfType<LiveFencePart.Content>().Select(part => part.Value));
    }

    /// <summary>The tools of the CLI providers run in the nth-ui server: the recorded call is made there, and what it records binds to the row.</summary>
    private static async Task<List<object>> WithServer(IReadOnlyList<ProviderEvent> events, TeachingOutcome outcome, ArtifactHostScope scope, ProviderKind provider)
    {
        var script = new List<object>();
        var changes = new List<LiveViewChange>();
        using var subscription = scope.Host.Live.Subscribe(Chat, changes.Add);
        var server = new UiMcpServer(UiMcpBridge.For(new SessionConfiguration
        {
            Provider = provider,
            WorkingDirectory = SwiftURL.Create("file:///C:/live-replay")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
            IgnisThreadID = Chat,
        }));
        var run = false;
        foreach (var @event in events)
        {
            script.Add(@event);
            // The server runs the call when the provider reports its arguments whole (the first time the row carries complete JSON).
            if (!run && WholeArguments(@event) && outcome.Calls.Count > 0)
            {
                run = true;
                foreach (var call in outcome.Calls.Where(call => call.Tool is "ui_show" or "ui_update" or "html_widget"))
                {
                    var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = call.Tool, ["arguments"] = call.Arguments.DeepClone() } };
                    var reply = JsonNode.Parse((await server.Handle(request.ToJsonString(), CancellationToken.None))!)!;
                    Assert.False(reply["result"]!["isError"]!.GetValue<bool>(), reply.ToJsonString());
                    foreach (var change in changes) script.Add(change);
                    changes.Clear();
                }
            }
        }
        return script;
    }

    private static bool WholeArguments(ProviderEvent @event) => @event switch
    {
        ProviderEvent.ToolUpdated { Update.Detail: { } detail } => IsWhole(detail),
        ProviderEvent.ToolStarted { Call.Detail: { } detail } => IsWhole(detail),
        _ => false,
    };

    private static bool IsWhole(string detail)
    {
        try { return JsonNode.Parse(detail) is JsonObject { Count: > 0 }; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    [Fact]
    public async Task Claude_calls_ui_show_and_the_row_grows_with_the_arguments_and_settles_on_what_the_tool_recorded()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        foreach (var recording in TeachingReplay.Load("claude"))
        {
            scope.Store.Clear(Chat);
            var events = TeachingReplay.ClaudeEvents(recording);
            var outcome = TeachingReplay.Outcome(events);
            var script = await WithServer(events, outcome, scope, ProviderKind.Claude);
            var replayed = Read(script, scope.Host.Live);
            Check("claude", recording, replayed, ViewOf(recording, outcome.Calls, outcome.Text), scope.Host.Live);
            if (recording.Expect == "view")
            {
                // Claude streams the arguments, so the row was seen partly written: more than one state before the end.
                Assert.True(replayed.Growth.Count > 3, recording.Id + ": " + string.Join(",", replayed.Growth.Select(g => g.Length)));
                Assert.Contains(replayed.Growth, text => text.Length > 0 && text.Length < replayed.Growth[^1].Length);
            }
            scope.Host.Live.Forget(Chat);
        }
    }

    [Fact]
    public async Task Codex_calls_ui_show_through_MCP_and_the_row_shows_the_view_with_the_call()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        foreach (var recording in TeachingReplay.Load("codex"))
        {
            var events = TeachingReplay.CodexEvents(recording);
            var outcome = TeachingReplay.Outcome(events);
            var script = await WithServer(events, outcome, scope, ProviderKind.Codex);
            Check("codex", recording, Read(script, scope.Host.Live), ViewOf(recording, outcome.Calls, outcome.Text), scope.Host.Live);
            scope.Host.Live.Forget(Chat);
        }
    }

    [Fact]
    public void Copilot_writes_fenced_blocks_and_each_view_becomes_one_row_that_grew_line_by_line()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        foreach (var recording in TeachingReplay.Load("copilot"))
        {
            var events = TeachingReplay.CopilotEvents(recording, runtime);
            var outcome = TeachingReplay.Outcome(events);
            // The artifact fallback reads the artifact blocks first, as the chat does; what is left is the flow's.
            var filter = new ArtifactFenceFilter(Chat, scope.Host, () => (new ArtifactAuthor("copilot", null, null), recording.Prompt));
            var script = events.SelectMany(filter.Filter).Cast<object>().ToList();
            var replayed = Read(script, scope.Host.Live);
            Check("copilot", recording, replayed, ViewOf(recording, outcome.Calls, outcome.Text), scope.Host.Live);
            if (recording.Expect == "view") Assert.True(replayed.Growth.Count > 3, recording.Id);
            scope.Host.Live.Forget(Chat);
            scope.Store.Clear(Chat);
        }
    }

    [Fact]
    public async Task DeepSeek_calls_the_native_tools_through_the_real_turn_loop_and_the_row_binds_to_what_they_recorded()
    {
        using var scope = new ArtifactHostScope(new ArtifactTurn(null, "recorded prompt", null));
        foreach (var recording in TeachingReplay.Load("deepseek"))
        {
            scope.Host.Live.Forget(Chat);
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
            var outcome = TeachingReplay.Outcome(rig.Events);
            List<object> ordered;
            lock (script) ordered = [.. script];
            var replayed = Read(ordered, scope.Host.Live);
            Check("deepseek", recording, replayed, ViewOf(recording, rig.Requests.Count > 1 ? TeachingReplay.CallsIn(rig.Requests[^1]) : [], outcome.Text), scope.Host.Live);
            if (recording.Expect == "view") Assert.True(replayed.Growth.Count > 3, recording.Id + ": the native loop reports the call's arguments as they stream");
        }
    }

    [Fact]
    public async Task A_Local_model_writes_lite_fenced_blocks_and_each_view_becomes_one_lite_row()
    {
        using var scope = new ArtifactHostScope(new ArtifactTurn(null, "recorded prompt", null));
        foreach (var recording in TeachingReplay.Load("local"))
        {
            scope.Host.Live.Forget(Chat);
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

            var outcome = TeachingReplay.Outcome(events);
            var filter = new ArtifactFenceFilter(Chat, scope.Host, () => (new ArtifactAuthor("local", null, null), recording.Prompt));
            var replayed = Read(events.SelectMany(filter.Filter).Cast<object>().ToList(), scope.Host.Live, lite: true);
            Check("local", recording, replayed, ViewOf(recording, outcome.Calls, outcome.Text), scope.Host.Live);
            if (recording.Expect == "view") Assert.True(Assert.Single(replayed.Sim.LiveRows).View!.Lite);
            scope.Store.Clear(Chat);
        }
    }
}
