// PORT-ONLY: Tactus (ED-NEW-pcctl-42; DESIGN.md 2.4, 3.4). A real OpenAICompatibleSession turn loop with the nth-pc tools,
// against the scripted answerer and the committed transcripts of Fixtures/skills-native (no network, no provider): the
// tools and the instructions go out while on and never while off; a call runs on the engine without a provider card;
// its text is the tool message and its screenshot follows in one user message only for a model that takes images; the
// next turn drops the screenshots unless the setting keeps them; and a screenshot message never counts as a turn's start.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-42")]
public sealed class PcApiTurnTests
{
    private static Func<OpenAICompatibleSession.Config, SessionConfiguration, OpenAICompatibleSession> Seeing(bool vision) =>
        (config, configuration) => new OpenAICompatibleSession(config with { IsVisionModel = _ => vision }, configuration);

    private static IEnumerable<string> ToolNames(JSONValue body) => body["tools"]!.JSONValueArray!.Select(t => t["function"]!["name"]!.JSONValueString!);

    [Fact]
    public async Task A_session_built_while_tactus_is_off_sends_no_nth_pc_tool_and_no_instructions()
    {
        await using var rig = NativeSessionRig.Create(["final-answer.sse"]);
        await rig.Begin();
        await rig.Turn("hello");

        Assert.Null(rig.Session.PcTools);
        Assert.DoesNotContain(ToolNames(rig.Json(0)), name => name.StartsWith("pc_", StringComparison.Ordinal));
        Assert.DoesNotContain(PcInstructions.Text, rig.Body(0), StringComparison.Ordinal);
    }

    [Theory, InlineData(true), InlineData(false)]
    public async Task A_call_runs_on_the_engine_and_its_screenshot_follows_only_for_a_model_that_takes_images(bool vision)
    {
        using var pc = new PcAssemblyRig();
        pc.Notepad();
        var assembly = pc.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        using var engine = new PcControlEngine(assembly.PartsFor("chat-api"), new PcCaller("chat-api", () => true, vision));
        await using var rig = NativeSessionRig.Create(["pc-observe-call.sse", "final-answer.sse"], RuntimeMode.Supervised, factory: Seeing(vision),
            configure: s => s.PcTools = new PcNativeTools(new PcRegistration("chat-api", ProviderKind.Deepseek, "deepseek-chat", vision), _ => engine, () => pc.Settings));
        await rig.Begin();
        await rig.Turn("save my note", RuntimeMode.Supervised);

        Assert.Contains(PcToolSchemas.Observe, ToolNames(rig.Json(0)));
        Assert.Contains(PcInstructions.Text, rig.Json(0)["messages"]!.JSONValueArray![0]["content"]!.JSONValueString!, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Events, e => e is ProviderEvent.Approval);
        var started = Assert.Single(rig.Events.OfType<ProviderEvent.ToolStarted>());
        Assert.Equal(PcNativeTools.Title(PcToolSchemas.Observe), started.Call.Title);
        Assert.Contains(pc.Audits["chat-api"].Entries, e => e.Tool == PcToolSchemas.Observe);

        var second = rig.Json(1)["messages"]!.JSONValueArray!;
        var toolAt = second.ToList().FindIndex(m => m["role"]!.JSONValueString == "tool");
        Assert.Equal("call-pc1", second[toolAt]["tool_call_id"]!.JSONValueString);
        Assert.Contains("Notepad", second[toolAt]["content"]!.JSONValueString!, StringComparison.Ordinal);
        if (vision)
        {
            Assert.Equal(toolAt + 2, second.Count);
            Assert.True(PcNativeTools.IsScreenshotMessage(second[^1]));
            Assert.StartsWith("data:image/jpeg;base64,", second[^1]["content"]![1]!["image_url"]!["url"]!.JSONValueString!, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(toolAt + 1, second.Count);
            Assert.DoesNotContain("image_url", rig.Body(1), StringComparison.Ordinal);
        }
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task The_next_turn_drops_the_screenshots_unless_the_setting_keeps_them(bool keep)
    {
        using var pc = new PcAssemblyRig(PcSettings.Default with { Enabled = true, KeepScreenshots = keep });
        pc.Notepad();
        var assembly = pc.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        using var engine = new PcControlEngine(assembly.PartsFor("chat-keep"), new PcCaller("chat-keep", () => true, true));
        await using var rig = NativeSessionRig.Create(["pc-observe-call.sse", "final-answer.sse", "final-answer.sse"], factory: Seeing(true),
            configure: s => s.PcTools = new PcNativeTools(new PcRegistration("chat-keep", ProviderKind.Deepseek, "deepseek-chat", true), _ => engine, () => pc.Settings));
        await rig.Begin();
        await rig.Turn("look at my note");
        await rig.Turn("thanks");

        var third = rig.Json(2)["messages"]!.JSONValueArray!;
        Assert.Equal(keep, third.Any(PcNativeTools.IsScreenshotMessage));
        Assert.Equal(keep, rig.Body(2).Contains("image_url", StringComparison.Ordinal));
        if (!keep) Assert.Contains(third, m => m["content"]?.JSONValueString is { } text && text.StartsWith("[screenshot o:", StringComparison.Ordinal) && text.EndsWith(" removed]", StringComparison.Ordinal));
        Assert.Equal("thanks", third[^1]["content"]!.JSONValueString);
    }

    [Fact]
    public async Task Two_looks_in_one_turn_keep_both_and_a_third_drops_the_oldest()
    {
        using var pc = new PcAssemblyRig();
        pc.Notepad();
        var assembly = pc.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        using var engine = new PcControlEngine(assembly.PartsFor("chat-three"), new PcCaller("chat-three", () => true, true));
        await using var rig = NativeSessionRig.Create(["pc-observe-call.sse", "pc-observe-again-call.sse", "pc-observe-third-call.sse", "final-answer.sse"], factory: Seeing(true),
            configure: s => s.PcTools = new PcNativeTools(new PcRegistration("chat-three", ProviderKind.Deepseek, "deepseek-chat", true), _ => engine, () => pc.Settings));
        await rig.Begin();
        await rig.Turn("look three times");

        Assert.Equal(2, rig.Json(2)["messages"]!.JSONValueArray!.Count(PcNativeTools.IsScreenshotMessage));
        var last = rig.Json(3)["messages"]!.JSONValueArray!;
        Assert.Equal(2, last.Count(PcNativeTools.IsScreenshotMessage));
        Assert.Single(last, m => m["content"]?.JSONValueString is { } text && text.StartsWith("[screenshot o:", StringComparison.Ordinal));
        Assert.Equal("look three times", last[1]["content"]!.JSONValueString);
    }

    [Fact]
    public async Task A_screenshot_message_is_never_the_start_of_the_turn_the_history_keeps()
    {
        await using var rig = NativeSessionRig.Create([]);
        var task = JSONValue.FromDictionaryLiteral(("role", "user"), ("content", "the real task"));
        var history = new List<JSONValue> { JSONValue.FromDictionaryLiteral(("role", "system"), ("content", "rules")), task };
        for (var i = 0; i < 70; i++)
        {
            history.Add(JSONValue.FromDictionaryLiteral(("role", "assistant"), ("content", "step " + i)));
            if (i % 10 == 9) history.Add(PcNativeTools.ScreenshotMessage([("pc_observe", new PcImage([1], 1, 1, "o:" + i))]));
        }
        rig.Session.SetMessagesForTests(history);

        rig.Session.TrimHistoryForTests();

        Assert.Contains(rig.Session.Messages, m => ReferenceEquals(m, task));
        await Task.CompletedTask;
    }
}
