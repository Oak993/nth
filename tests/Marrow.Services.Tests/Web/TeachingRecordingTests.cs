using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Local;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// DESIGN.md 14 row W17, "five providers produce valid blocks on 10 prompts each (recorded fixtures)" (web surfaces W17;
// ED-NEW-web-w17-1). Ten prompts, five providers: Claude and Codex call nth-ui's tools through MCP, DeepSeek calls them as native
// functions, Copilot writes fenced blocks, and a Local model writes lite fenced blocks. Each recorded answer is replayed through
// that provider's real session code, then checked: plain questions get plain text with no view; a view question gets one sentence and
// exactly one view that parses clean, without warnings, computes without an error value and ends the answer; an artifact question
// gets exactly one artifact that the real store accepts. The recordings are Marrow-authored synthetic transcripts (see
// tests/fixtures/web-surfaces/teaching/README.md); no model provider is ever called.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-web-w17-1")]
public sealed class TeachingRecordingTests
{
    private static readonly string[] PromptIds =
        ["01-capital", "02-savings", "03-databases", "04-moving", "05-study", "06-oauth", "07-mutex", "08-pomodoro", "09-bakery", "10-laptop"];

    private static (string Info, string Body, string Before, string After)[] Fences(string text)
    {
        var lines = text.Split('\n');
        var found = new List<(string, string, string, string)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var open = Regex.Match(lines[i], @"^ {0,3}(`{3,})(.*)$");
            if (!open.Success) continue;
            var length = open.Groups[1].Length;
            var close = i + 1;
            while (close < lines.Length && !Regex.IsMatch(lines[close], "^ {0,3}`{" + length + @",}\s*$")) close++;
            var body = string.Join("\n", lines[(i + 1)..Math.Min(close, lines.Length)]);
            var after = close + 1 < lines.Length ? string.Join("\n", lines[(close + 1)..]) : "";
            found.Add((open.Groups[2].Value.Trim(), body, string.Join("\n", lines[..i]), after));
            i = close;
        }
        return [.. found];
    }

    private static void AssertValidView(string id, string view, NthUiProfile profile)
    {
        var document = NthUiParser.Parse(view + "\n", profile);
        var notes = string.Join("; ", document.Diagnostics.Select(d => d.ToString()));
        Assert.Empty(NthUiValidator.Problems(document));
        Assert.True(document.IsClean, $"{id}: {notes}");
        Assert.True(document.Diagnostics.Count == 0, $"{id} warns: {notes}");
        Assert.NotEmpty(document.Roots);
        var runtime = new NthUiRuntime(document);
        runtime.Sync();
        foreach (var name in document.Declarations.Keys) Assert.False(runtime.Get(name).IsError, $"{id}: {name} is an error value");
        foreach (var node in document.Nodes)
        {
            foreach (var arg in node.Args.Values) Assert.False(runtime.Evaluate(arg).IsError, $"{id}: {node.Kind} {arg.Name} is an error value");
            foreach (var cell in node.Cells) Assert.False(runtime.Evaluate(cell).IsError, $"{id}: a cell of {node.Kind} is an error value");
        }
    }

    /// <summary>Checks one recorded answer against what its prompt expects; returns the number of views and artifacts it made.</summary>
    private static (int Views, int Artifacts) Check(string provider, TeachingRecording recording, TeachingOutcome outcome, NthUiProfile profile, ArtifactHostScope scope)
    {
        var id = provider + "/" + recording.Id;
        Assert.All(outcome.Calls, call => Assert.Contains(call.Tool, new[] { "ui_show", "artifact_create" }));
        var shown = outcome.Calls.Where(c => c.Tool == "ui_show").ToList();
        var created = outcome.Calls.Where(c => c.Tool == "artifact_create").ToList();
        var fences = Fences(outcome.Text);
        var fencedViews = fences.Where(f => f.Info.StartsWith("nth-ui", StringComparison.Ordinal)).ToList();
        var fencedArtifacts = fences.Where(f => f.Info.StartsWith("nth-artifact", StringComparison.Ordinal)).ToList();
        var views = shown.Count + fencedViews.Count;
        var artifacts = created.Count + fencedArtifacts.Count;
        Assert.False(string.IsNullOrWhiteSpace(outcome.Text), id + ": the answer says nothing before its view or artifact");

        switch (recording.Expect)
        {
            case "plain":
                Assert.Equal((0, 0), (views, artifacts));
                break;
            case "view":
                Assert.Equal((1, 0), (views, artifacts));
                if (shown.Count == 1)
                {
                    AssertValidView(id, shown[0].Arguments["ui"]!.GetValue<string>(), profile);
                    Assert.Equal(["ui"], shown[0].Arguments.Select(p => p.Key));
                }
                else
                {
                    AssertValidView(id, fencedViews[0].Body, profile);
                    Assert.False(string.IsNullOrWhiteSpace(fencedViews[0].Before), id + ": no sentence before the view");
                    Assert.True(string.IsNullOrWhiteSpace(fencedViews[0].After), id + ": text after the view");
                }
                break;
            case "artifact":
                Assert.Equal((0, 1), (views, artifacts));
                var chat = Guid.NewGuid();
                var author = new ArtifactAuthor(provider, "fixture", null);
                if (created.Count == 1)
                {
                    var result = ArtifactTools.Run(scope.Store, chat, "artifact_create", created[0].Arguments, author, recording.Prompt);
                    Assert.False(result.Failed, id + ": " + result.Text);
                }
                else
                {
                    var filter = new ArtifactFenceFilter(chat, scope.Host, () => (author, recording.Prompt));
                    filter.Filter(new ProviderEvent.MessageCompleted("m", outcome.Text));
                    filter.Filter(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                    var saved = Assert.Single(scope.Store.List(chat));
                    Assert.Equal(1, saved.Latest.Number);
                    Assert.True(string.IsNullOrWhiteSpace(fencedArtifacts[0].After) || fencedArtifacts[0].After.Length < 40, id + ": text after the artifact");
                }
                var snapshot = Assert.Single(scope.Store.List(chat));
                var content = scope.Store.Read(chat, snapshot.Id);
                Assert.True(content.IsProject ? content.Files!.Count == 2 : content.Text!.Length > 100, id);
                break;
            default:
                Assert.Fail("an unknown expectation " + recording.Expect);
                break;
        }
        return (views, artifacts);
    }

    private static void CheckAll(string provider, IEnumerable<(TeachingRecording Recording, TeachingOutcome Outcome)> replays, NthUiProfile profile, ArtifactHostScope scope)
    {
        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        var count = 0;
        foreach (var (recording, outcome) in replays)
        {
            count++;
            var (views, artifacts) = Check(provider, recording, outcome, profile, scope);
            tally[recording.Expect] = tally.GetValueOrDefault(recording.Expect) + 1;
            Assert.Equal(recording.Expect == "view" ? 1 : 0, views);
            Assert.Equal(recording.Expect == "artifact" ? 1 : 0, artifacts);
        }
        Assert.Equal(10, count);
        Assert.Equal([("artifact", 2), ("plain", 2), ("view", 6)], tally.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)));
    }

    [Fact]
    public void The_recordings_are_the_same_ten_prompts_for_each_of_five_providers()
    {
        Assert.Equal(["claude", "codex", "copilot", "deepseek", "local"], TeachingReplay.Providers);
        foreach (var provider in TeachingReplay.Providers)
        {
            var recordings = TeachingReplay.Load(provider);
            Assert.Equal(PromptIds, recordings.Select(r => r.Id));
            Assert.Equal(TeachingReplay.Load("claude").Select(r => (r.Prompt, r.Expect)), recordings.Select(r => (r.Prompt, r.Expect)));
            Assert.All(recordings, r => Assert.NotEmpty(r.Events));
            var bytes = File.ReadAllBytes(Path.Combine(TeachingReplay.FixtureFolder(), provider + ".jsonl"));
            Assert.All(bytes, b => Assert.True(b < 128 && b != '\r', provider + " is not plain ASCII with LF line ends"));
        }
        Assert.Equal(["plain", "view", "view", "view", "view", "view", "plain", "artifact", "artifact", "view"], TeachingReplay.Load("claude").Select(r => r.Expect));
    }

    [Fact]
    public void Claude_calls_nth_ui_through_MCP_and_every_block_is_valid()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        CheckAll("claude", TeachingReplay.Load("claude").Select(r => (r, TeachingReplay.Claude(r))).ToList(), NthUiProfile.Full, scope);
    }

    [Fact]
    public void Codex_calls_nth_ui_through_MCP_and_every_block_is_valid()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        CheckAll("codex", TeachingReplay.Load("codex").Select(r => (r, TeachingReplay.Codex(r))).ToList(), NthUiProfile.Full, scope);
    }

    [Fact]
    public void Copilot_writes_fenced_blocks_and_every_block_is_valid()
    {
        using var runtime = new TestRuntime();
        using var scope = new ArtifactHostScope();
        CheckAll("copilot", TeachingReplay.Load("copilot").Select(r => (r, TeachingReplay.Copilot(r, runtime))).ToList(), NthUiProfile.Full, scope);
    }

    [Fact]
    public async Task DeepSeek_calls_the_function_tools_through_the_real_turn_loop_and_every_block_is_valid()
    {
        using var scope = new ArtifactHostScope(new ArtifactTurn(null, "recorded prompt", null));
        var replays = new List<(TeachingRecording, TeachingOutcome)>();
        foreach (var recording in TeachingReplay.Load("deepseek"))
        {
            var chat = Guid.NewGuid();
            var configuration = NativeSessionRig.MakeSessionConfiguration() with { IgnisThreadID = chat };
            await using var rig = new TeachingSessionRig([TeachingReplay.Sse(recording.Events), TeachingReplay.FinalAnswer], configuration,
                session => session.SkillTools = SkillSessionFactory.NativeTools(configuration));
            await rig.Turn(recording.Prompt);
            var requests = rig.Requests;

            // The first request teaches: the block is in the system prompt, and the tools are the artifact tools and the reference.
            var system = requests[0]["messages"]![0]!["content"]!.JSONValueString!;
            Assert.Contains("LIVE ANSWERS AND ARTIFACTS (nth)", system, StringComparison.Ordinal);
            Assert.Contains("call ui_reference with a topic", system, StringComparison.Ordinal);
            var offered = requests[0]["tools"]!.JSONValueArray!.Select(t => t["function"]!["name"]!.JSONValueString!).ToArray();
            Assert.Contains("artifact_create", offered);
            Assert.Contains("ui_reference", offered);

            var text = TeachingReplay.Outcome(rig.Events).Text;
            var calls = requests.Count > 1 ? TeachingReplay.CallsIn(requests[^1]) : [];
            Assert.Equal(recording.Expect == "plain" ? 1 : 2, requests.Count);
            if (recording.Expect == "artifact") Assert.Single(scope.Store.List(chat)); // the real tool loop made it
            else Assert.Empty(scope.Store.List(chat));
            replays.Add((recording, new TeachingOutcome(text, calls)));
        }
        CheckAll("deepseek", replays, NthUiProfile.Full, scope);
    }

    [Fact]
    public async Task A_Local_model_writes_lite_fenced_blocks_through_the_real_Local_session_and_every_block_is_valid()
    {
        using var scope = new ArtifactHostScope(new ArtifactTurn(null, "recorded prompt", null));
        var replays = new List<(TeachingRecording, TeachingOutcome)>();
        foreach (var recording in TeachingReplay.Load("local"))
        {
            await using var server = new LocalProviderLoopback();
            using var runtime = new TestRuntime();
            using var folder = new LocalProviderFolder();
            server.Reply("/v1/models", """{"data":[{"id":"tiny","context_length":8192,"supports_tool_calling":false}]}""");
            using var discovery = new LocalDiscovery([server.BaseURL]);
            var target = discovery.Resolve(Assert.Single(Assert.Single(await discovery.Discover()).Models).SelectionId)!;
            var configuration = new SessionConfiguration
            {
                Provider = ProviderKind.Local,
                WorkingDirectory = SwiftURL.Create("file:" + folder.Root.Replace('\\', '/'))!,
                Environment = ValueMap<string, string>.Empty,
                RuntimeMode = RuntimeMode.FullAccess,
                InteractionMode = InteractionMode.Build,
                IgnisThreadID = Guid.NewGuid(),
            };
            var session = new LocalSession(configuration, target);
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

            // A model that cannot call tools is taught the lite subset and fenced blocks, and is sent no tools.
            var request = Assert.Single(server.Requests, r => r.Target == "/v1/chat/completions");
            var body = JSONValue.Parse(request.Body)!;
            var system = body["messages"]![0]!["content"]!.JSONValueString!;
            Assert.Contains("THE VIEW LANGUAGE (SMALL SET)", system, StringComparison.Ordinal);
            Assert.Contains("nth-artifact", system, StringComparison.Ordinal);
            Assert.DoesNotContain("artifact_create", system, StringComparison.Ordinal);
            Assert.DoesNotContain("tools", body.JSONValueObject!.Keys);
            replays.Add((recording, TeachingReplay.Outcome(events)));
        }
        CheckAll("local", replays, NthUiProfile.Lite, scope);
    }

    [Fact]
    public void A_stray_fence_in_a_plain_answer_is_not_a_view()
    {
        var mutex = TeachingReplay.Load("claude").Single(r => r.Id == "07-mutex");
        using var runtime = new TestRuntime();
        var outcome = TeachingReplay.Claude(mutex);
        var fences = Fences(outcome.Text);
        Assert.Equal("python", Assert.Single(fences).Info);
        Assert.Empty(outcome.Calls);
        Assert.DoesNotContain("nth-ui", outcome.Text, StringComparison.Ordinal);
    }
}
