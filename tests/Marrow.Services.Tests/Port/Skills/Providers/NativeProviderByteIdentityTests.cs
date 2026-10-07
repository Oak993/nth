// SK-11f / DL-11 hard rule (skills-ONDEMAND-PLAN.md section 7, row DL-11: "with nothing installed, argv, environment and RPC traffic equal today's (the Mac's)"):
// against the provider code as it stood before SK-11f. The goldens in Fixtures/skills-native/golden-* were captured from that unmodified code (base commit
// 13a62183), and every byte the API providers send must still equal them: the request bodies (system prompt, history, the six tool definitions, the payload
// keys of each provider) and the system prompts of every mode. With nothing attached, with a context attached that has nothing to offer, and with the
// user's own skills switched off, there is no difference at all.
using Marrow.Core.Models;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class NativeProviderByteIdentityTests
{
    private static readonly string OwnRoot = Path.Combine(Path.GetTempPath(), "sk11f-identity-home", ".agents", "skills");

    private static byte[] Golden(string file) => File.ReadAllBytes(NativeSessionRig.GoldenPath(file));

    private static void AssertSameBytes(byte[] expected, byte[] actual, string what)
    {
        if (expected.AsSpan().SequenceEqual(actual)) return;
        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) at++;
        Assert.Fail($"{what}: differs from the golden at byte {at} (golden {expected.Length} bytes, now {actual.Length}); " +
            $"golden: ...{System.Text.Encoding.UTF8.GetString(expected, Math.Max(0, at - 40), Math.Min(120, expected.Length - Math.Max(0, at - 40)))}... now: ...{System.Text.Encoding.UTF8.GetString(actual, Math.Max(0, at - 40), Math.Min(120, actual.Length - Math.Max(0, at - 40)))}...");
    }

    // What "nothing to offer" can mean: nothing attached; a context with no generation and no offer; a generation holding no enabled skill; an offer that says
    // nothing is installed; the user's own skills folder switched off (and then never opened).
    public static TheoryData<string> Attachments => new() { "none", "empty-context", "generation-without-skills", "offer-unavailable", "own-skills-off" };

    private static (Action<OpenAICompatibleSession>? Configure, IDisposable Keep, RecordingUserSkillFiles Files) Attach(string kind)
    {
        var files = new RecordingUserSkillFiles();
        files.Add(Path.Combine(OwnRoot, "private-skill", "SKILL.md"), "---\nname: private-skill\ndescription: PRIVATE\n---\nPRIVATE BODY SENTINEL\n");
        var kit = new NativeSkillsKit();
        Generation? generation = null;
        NativeSkillTools? tools = null;
        switch (kind)
        {
            case "none": break;
            case "empty-context": tools = new NativeSkillTools(null); break;
            case "generation-without-skills":
                kit.Add("A1", "pdf", on: false);
                generation = kit.Start();
                tools = new NativeSkillTools(generation);
                break;
            case "offer-unavailable": tools = new NativeSkillTools(null, offer: new FakeToolOffer(false, "soffice")); break;
            case "own-skills-off": tools = new NativeSkillTools(null, new NativeUserSkills(OwnRoot, enabled: false, files)); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return (tools is null ? null : session => session.SkillTools = tools, new Disposer(() => { generation?.Dispose(); kit.Dispose(); }), files);
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [MemberData(nameof(Attachments))]
    public async Task A_plain_turn_sends_the_golden_request_bytes(string kind)
    {
        var (configure, keep, files) = Attach(kind);
        using var lifetime = keep;
        AssertSameBytes(Golden("golden-request-plain.json"), await NativeGoldenScenarios.PlainTurn(configure), "the plain request");
        Assert.Empty(files.Accesses);
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [MemberData(nameof(Attachments))]
    public async Task A_tool_round_sends_both_golden_request_bodies_with_the_tool_result_in_the_second(string kind)
    {
        var (configure, keep, files) = Attach(kind);
        using var lifetime = keep;
        var (first, second) = await NativeGoldenScenarios.ToolRound(configure);
        AssertSameBytes(Golden("golden-request-toolround-1.json"), first, "the first request of a tool round");
        AssertSameBytes(Golden("golden-request-toolround-2.json"), second, "the second request of a tool round");
        Assert.Empty(files.Accesses);
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [MemberData(nameof(Attachments))]
    public async Task Every_mode_gets_the_golden_system_prompt(string kind)
    {
        var (configure, keep, _) = Attach(kind);
        using var lifetime = keep;
        foreach (var (runtime, interaction, file) in NativeGoldenScenarios.Modes)
            Assert.Equal(System.Text.Encoding.UTF8.GetString(Golden(file)), await NativeGoldenScenarios.Prompt(runtime, interaction, configure));
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [MemberData(nameof(Attachments))]
    public async Task Each_of_the_four_API_providers_sends_its_golden_payload_and_prompt(string kind)
    {
        var (configure, keep, _) = Attach(kind);
        using var lifetime = keep;
        foreach (var provider in NativeGoldenScenarios.Providers)
        {
            var (payload, prompt) = await NativeGoldenScenarios.ProviderPayload(provider, configure);
            AssertSameBytes(Golden($"golden-provider-{provider}-payload.json"), payload, provider + " payload");
            Assert.Equal(System.Text.Encoding.UTF8.GetString(Golden($"golden-provider-{provider}-prompt.txt")), prompt);
        }
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void The_golden_request_has_exactly_the_six_built_in_tools_and_none_of_Marrows()
    {
        var json = Marrow.Core.Support.JSONValue.Parse(Golden("golden-request-plain.json"))!;
        var names = json["tools"]!.JSONValueArray!.Select(t => t["function"]!["name"]!.JSONValueString!).ToArray();
        Assert.Equal(["read_file", "list_files", "search_text", "write_file", "edit_file", "run_command"], names);
    }
}
