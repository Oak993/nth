// SK-11f / WU-12 end to end: a real OpenAICompatibleSession turn loop against a scripted answerer, with the committed transcripts of
// Fixtures/skills-native. The names and descriptions go out in every request; a skill's text goes out only after the model asks for it (OD-0099).
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class NativeProviderSkillsTurnTests
{
    private static string SystemContent(JSONValue body) => body["messages"]!.JSONValueArray![0]["content"]!.JSONValueString!;

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_first_request_adds_the_block_and_the_use_skill_definition_and_changes_nothing_else()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        kit.Add("O3", "doc");
        using var generation = kit.Start();
        var tools = new NativeSkillTools(generation);
        await using var rig = NativeSessionRig.Create(["plain-turn.sse"], configure: s => s.SkillTools = tools);
        await rig.Begin();
        await rig.Turn("hello there");

        var golden = JSONValue.Parse(File.ReadAllBytes(NativeSessionRig.GoldenPath("golden-request-plain.json")))!;
        var now = rig.Json(0);
        Assert.Equal(SystemContent(golden) + "\n\n" + tools.CatalogueBlock(), SystemContent(now));
        Assert.Equal(golden["tools"]!.JSONValueArray!.Concat(tools.Definitions), now["tools"]!.JSONValueArray!);
        Assert.Equal("auto", now["tool_choice"]!.JSONValueString);
        Assert.Equal(golden["model"], now["model"]);
        Assert.Equal(golden["stream"], now["stream"]);
        Assert.Equal(golden["messages"]!.JSONValueArray!.Skip(1), now["messages"]!.JSONValueArray!.Skip(1));
        Assert.Equal(golden.JSONValueObject!.Keys.Order(StringComparer.Ordinal), now.JSONValueObject!.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("SENTINEL", rig.Body(0), StringComparison.Ordinal);
        Assert.Contains("pdf: Fixture description", rig.Body(0), StringComparison.Ordinal);
        Assert.Contains("doc: Fixture description", rig.Body(0), StringComparison.Ordinal);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_skill_text_reaches_the_provider_only_in_the_request_after_the_model_calls_use_skill()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        await using var rig = NativeSessionRig.Create(["use-skill-call.sse", "use-skill-file-call.sse", "final-answer.sse"],
            configure: s => s.SkillTools = new NativeSkillTools(generation));
        await rig.Begin();
        await rig.Turn("make a pdf");

        Assert.Equal(3, rig.RequestBodies.Count);
        Assert.DoesNotContain("SENTINEL", rig.Body(0), StringComparison.Ordinal);
        var second = rig.Json(1)["messages"]!.JSONValueArray!;
        var skillMessage = second[^1];
        Assert.Equal("tool", skillMessage["role"]!.JSONValueString);
        Assert.Equal("call-1", skillMessage["tool_call_id"]!.JSONValueString);
        Assert.Contains("BODY SENTINEL", skillMessage["content"]!.JSONValueString!, StringComparison.Ordinal);
        Assert.DoesNotContain("REFERENCE SENTINEL", rig.Body(1), StringComparison.Ordinal);
        var third = rig.Json(2)["messages"]!.JSONValueArray!;
        Assert.Equal("REFERENCE SENTINEL", third[^1]["content"]!.JSONValueString);
        Assert.Equal("call-2", third[^1]["tool_call_id"]!.JSONValueString);
        Assert.Equal(SystemContent(rig.Json(0)), SystemContent(rig.Json(2)));
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_use_skill_row_is_a_Skill_row_that_completes_and_never_asks_for_approval()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        await using var rig = NativeSessionRig.Create(["use-skill-call.sse", "final-answer.sse"], RuntimeMode.Supervised,
            configure: s => s.SkillTools = new NativeSkillTools(generation));
        await rig.Begin();
        await rig.Turn("make a pdf", RuntimeMode.Supervised);

        Assert.DoesNotContain(rig.Events, e => e is ProviderEvent.Approval);
        var started = Assert.Single(rig.Events.OfType<ProviderEvent.ToolStarted>());
        Assert.Equal("call-1", started.Id);
        Assert.Equal("Skill · pdf", started.Call.Title);
        Assert.Equal(ToolCall.Kind.Other, started.Call.ToolCallKind);
        var updated = Assert.Single(rig.Events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(ToolCall.Status.Completed, updated.Update.Status);
        Assert.Equal(TurnStatus.Completed, Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]).Status);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_bad_use_skill_call_comes_back_as_an_error_row_and_the_turn_goes_on()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "doc");
        using var generation = kit.Start();
        await using var rig = NativeSessionRig.Create(["use-skill-call.sse", "final-answer.sse"], // the transcript asks for pdf, which is not installed
            configure: s => s.SkillTools = new NativeSkillTools(generation));
        await rig.Begin();
        await rig.Turn("make a pdf");

        Assert.Equal("Error: unknown skill pdf.", rig.Json(1)["messages"]!.JSONValueArray![^1]["content"]!.JSONValueString);
        var updated = Assert.Single(rig.Events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(ToolCall.Status.Failed, updated.Update.Status);
        Assert.Equal(TurnStatus.Completed, Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]).Status);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Without_the_skills_context_a_call_to_use_skill_is_the_unknown_tool_it_always_was()
    {
        await using var rig = NativeSessionRig.Create(["use-skill-call.sse", "final-answer.sse"]);
        await rig.Begin();
        await rig.Turn("make a pdf");
        Assert.Equal("Error: unknown tool use_skill.", rig.Json(1)["messages"]!.JSONValueArray![^1]["content"]!.JSONValueString);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_block_follows_the_policy_and_is_the_same_every_turn()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var tools = new NativeSkillTools(generation);
        await using var rig = NativeSessionRig.Create(["plain-turn.sse", "plain-turn.sse"], configure: s => s.SkillTools = tools);
        await rig.Begin();
        await rig.Turn("one");
        await rig.Turn("two");

        var prompt = SystemContent(rig.Json(0));
        Assert.True(prompt.IndexOf("[Nth anti-slop guidance]", StringComparison.Ordinal) is var policy and >= 0 && policy < prompt.IndexOf(tools.CatalogueBlock()!, StringComparison.Ordinal));
        Assert.EndsWith("\n\n" + tools.CatalogueBlock(), prompt, StringComparison.Ordinal); // no project guidelines here: the block is the last thing
        Assert.Equal(prompt, SystemContent(rig.Json(1)));
    }

    [WindowsGuidanceFact, Trait("Rule", "R-TEST-4")]
    public async Task The_block_comes_before_the_projects_own_guidelines()
    {
        var folder = Path.Combine(TestDataDirectory.Read()!, "sk11f-guidance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "AGENTS.md"), "PROJECT RULES SENTINEL\n");
            using var kit = new NativeSkillsKit();
            kit.Add("A1", "pdf");
            using var generation = kit.Start();
            var tools = new NativeSkillTools(generation);
            await using var rig = NativeSessionRig.Create(["plain-turn.sse"], configure: s => s.SkillTools = tools, workingDirectory: folder);
            await rig.Begin();
            await rig.Turn("one");

            var prompt = SystemContent(rig.Json(0));
            var policy = prompt.IndexOf("[Nth anti-slop guidance]", StringComparison.Ordinal);
            var block = prompt.IndexOf(tools.CatalogueBlock()!, StringComparison.Ordinal);
            var guidance = prompt.IndexOf("PROJECT RULES SENTINEL", StringComparison.Ordinal);
            Assert.True(policy >= 0 && policy < block && block < guidance, $"policy {policy}, block {block}, guidance {guidance}");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
