// SK-11f / DL-14 (skills-ONDEMAND-PLAN.md section 7, row DL-14, and 5.1 S4, 5.5 R4): request_tool for the native API providers. The schema; the call blocks
// until the user answers and returns the installed path or "declined"; names outside the catalogue are refused; nothing is downloaded without the offer.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class RequestToolTests
{
    // A call that is never answered must fail the test, not hang it.
    private static Task<NativeToolResult> Request(NativeSkillTools tools, string json) =>
        tools.Execute("request_tool", JSONValue.Parse(json)!).WaitAsync(TimeSpan.FromSeconds(10));

    [Fact, Trait("Rule", "R-TEST-4")]
    public void The_schema_takes_a_name_and_a_reason_and_requires_both()
    {
        var tools = new NativeSkillTools(null, offer: new FakeToolOffer(true, "soffice"));
        Assert.True(tools.Owns("request_tool"));
        var definition = Assert.Single(tools.Definitions);
        Assert.Equal("function", definition["type"]!.JSONValueString);
        Assert.Equal("request_tool", definition["function"]!["name"]!.JSONValueString);
        var parameters = definition["function"]!["parameters"]!;
        Assert.Equal("object", parameters["type"]!.JSONValueString);
        Assert.Equal(["name", "reason"], parameters["required"]!.JSONValueArray!.Select(v => v.JSONValueString!));
        Assert.Equal(["name", "reason"], parameters["properties"]!.JSONValueObject!.Keys.Order(StringComparer.Ordinal));
        Assert.All(parameters["properties"]!.JSONValueObject!.Values, v => Assert.Equal("string", v["type"]!.JSONValueString));
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void The_tool_is_not_offered_while_nothing_from_the_catalogue_is_installed()
    {
        var tools = new NativeSkillTools(null, offer: new FakeToolOffer(false, "soffice"));
        Assert.False(tools.Owns("request_tool"));
        Assert.Empty(tools.Definitions);
        Assert.False(new NativeSkillTools(null).Owns("request_tool"));
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_call_blocks_until_the_user_answers_and_returns_installed_at_the_path()
    {
        var offer = new FakeToolOffer(true, "soffice");
        var tools = new NativeSkillTools(null, offer: offer);
        var pending = Request(tools, """{"name":"soffice","reason":"convert report.docx to PDF"}""");
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);
        Assert.Equal([("soffice", "convert report.docx to PDF")], offer.Asks);

        var installed = Path.Combine("C:", "Marrow Code", "addons", "bin", "soffice.exe");
        offer.Answer(ToolRequestOutcome.Installed(installed));
        var result = await pending;
        Assert.False(result.Failed);
        Assert.Equal($"installed at {installed}", result.Output);
        Assert.Equal(new ToolRequested("soffice", "convert report.docx to PDF"), Assert.Single(result.Events));
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_decline_returns_declined_and_the_user_is_not_asked_again_by_the_tool()
    {
        var offer = new FakeToolOffer(true, "soffice");
        var tools = new NativeSkillTools(null, offer: offer);
        var pending = Request(tools, """{"name":"soffice","reason":"r"}""");
        offer.Answer(ToolRequestOutcome.Declined);
        var result = await pending;
        Assert.False(result.Failed);
        Assert.Equal("declined", result.Output);
        Assert.Single(offer.Asks);
    }

    [Theory, Trait("Rule", "R-TEST-10")]
    [InlineData("""{"name":"rm","reason":"clean up"}""", "Error: rm is not in the catalogue.")]
    [InlineData("""{"name":"C:\\Windows\\system32\\cmd.exe","reason":"x"}""", "Error: C:\\Windows\\system32\\cmd.exe is not in the catalogue.")]
    [InlineData("""{"name":"","reason":"x"}""", "Error: a tool name is required.")]
    [InlineData("""{"reason":"x"}""", "Error: a tool name is required.")]
    public async Task A_name_outside_the_catalogue_is_refused_and_the_user_is_never_asked(string json, string expected)
    {
        var offer = new FakeToolOffer(true, "soffice");
        var result = await Request(new NativeSkillTools(null, offer: offer), json);
        Assert.True(result.Failed);
        Assert.Equal(expected, result.Output);
        Assert.Empty(offer.Asks);
        Assert.Empty(result.Events);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_missing_reason_is_passed_on_as_empty()
    {
        var offer = new FakeToolOffer(true, "soffice");
        var pending = Request(new NativeSkillTools(null, offer: offer), """{"name":"soffice"}""");
        offer.Answer(ToolRequestOutcome.Declined);
        await pending;
        Assert.Equal([("soffice", "")], offer.Asks);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Both_tools_can_be_offered_together_and_each_goes_to_its_own_handler()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var offer = new FakeToolOffer(true, "soffice");
        var tools = new NativeSkillTools(generation, offer: offer);
        Assert.Equal(["use_skill", "request_tool"], tools.Definitions.Select(d => d["function"]!["name"]!.JSONValueString!));
        Assert.Contains("BODY SENTINEL", (await tools.Execute("use_skill", JSONValue.Parse("""{"name":"pdf"}""")!)).Output, StringComparison.Ordinal);
        Assert.Empty(offer.Asks);
    }
}
