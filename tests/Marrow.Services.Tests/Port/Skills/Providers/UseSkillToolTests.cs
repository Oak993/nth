// SK-11f / WU-12 (skills-PLAN.md 3.3 native row and 2.1 "use_skill returns SKILL.md and referenced files, and refuses .., absolute paths and anything
// outside the generation"): the use_skill tool, with progressive disclosure (the text loads only when the tool is called).
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class UseSkillToolTests
{
    private static Task<NativeToolResult> Use(NativeSkillTools tools, string json) => tools.Execute("use_skill", JSONValue.Parse(json)!);

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task Calling_use_skill_returns_the_skill_folder_and_the_full_SKILL_md()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var result = await Use(new NativeSkillTools(generation), """{"name":"pdf"}""");
        Assert.False(result.Failed);
        Assert.Equal("Skill \u00B7 pdf", result.Title);
        var folder = Assert.Single(generation.Skills).Folder;
        Assert.Equal($"Skill folder: {folder}\n\n{generation.Invoke("pdf")}", result.Output);
        Assert.Contains("BODY SENTINEL", result.Output, StringComparison.Ordinal);
        Assert.Equal(new SkillUsed("pdf", "A1"), Assert.Single(result.Events));
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_file_the_instructions_reference_is_returned_as_it_is_and_is_not_a_skill_use()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var result = await Use(new NativeSkillTools(generation), """{"name":"pdf","file":"references/example.txt"}""");
        Assert.False(result.Failed);
        Assert.Equal("REFERENCE SENTINEL", result.Output);
        Assert.Empty(result.Events);
    }

    [Theory, Trait("Rule", "R-TEST-10")]
    [InlineData("../doc/SKILL.md")]
    [InlineData("references/../../doc/SKILL.md")]
    [InlineData("..\\doc\\SKILL.md")]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("references/missing.txt")]
    [InlineData("doc/SKILL.md")]
    [InlineData("references")]
    [InlineData("references/")]
    [InlineData("./SKILL.md")]
    public async Task A_path_outside_the_skill_or_not_pinned_in_the_generation_is_refused_and_leaks_nothing(string file)
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        kit.Add("O3", "doc");
        using var generation = kit.Start();
        var tools = new NativeSkillTools(generation);
        var result = await Use(tools, $$"""{"name":"pdf","file":{{System.Text.Json.JsonSerializer.Serialize(file)}}}""");
        Assert.True(result.Failed);
        Assert.StartsWith("Error: ", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Events);
    }

    [Theory, Trait("Rule", "R-TEST-10")]
    [InlineData("../pdf")]
    [InlineData("/pdf")]
    [InlineData("nope")]
    [InlineData("PDF ")]
    [InlineData("A1/pdf")]
    public async Task A_name_that_is_not_in_the_catalogue_is_unknown(string name)
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var result = await Use(new NativeSkillTools(generation), $$"""{"name":{{System.Text.Json.JsonSerializer.Serialize(name)}}}""");
        Assert.True(result.Failed);
        Assert.Equal($"Error: unknown skill {name}.", result.Output);
        Assert.Empty(result.Events);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task A_missing_name_is_an_error_and_a_skill_that_is_off_or_explicit_only_looks_unknown()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        kit.Add("O3", "doc", on: false);
        kit.Add("A2", "screenshot");
        using var generation = kit.Start();
        var tools = new NativeSkillTools(generation);
        Assert.Equal("Error: a skill name is required.", (await Use(tools, "{}")).Output);
        Assert.Equal("Error: a skill name is required.", (await Use(tools, """{"name":""}""")).Output);
        Assert.Equal("Error: unknown skill doc.", (await Use(tools, """{"name":"doc"}""")).Output);
        Assert.Equal("Error: unknown skill screenshot.", (await Use(tools, """{"name":"screenshot"}""")).Output);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task The_tool_is_offered_only_while_there_are_skills_to_use()
    {
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        var tools = new NativeSkillTools(generation);
        Assert.True(tools.Owns("use_skill"));
        var definition = Assert.Single(tools.Definitions);
        Assert.Equal("use_skill", definition["function"]!["name"]!.JSONValueString);
        Assert.Equal("function", definition["type"]!.JSONValueString);
        var parameters = definition["function"]!["parameters"]!;
        Assert.Equal("object", parameters["type"]!.JSONValueString);
        Assert.Equal(["name"], parameters["required"]!.JSONValueArray!.Select(v => v.JSONValueString!));
        Assert.Equal(["file", "name"], parameters["properties"]!.JSONValueObject!.Keys.Order(StringComparer.Ordinal));
        Assert.False(tools.Owns("request_tool"));
        Assert.False(tools.Owns("read_file"));

        var empty = new NativeSkillTools(null);
        Assert.False(empty.Owns("use_skill"));
        Assert.Empty(empty.Definitions);
        var execute = await Record.ExceptionAsync(() => empty.Execute("use_skill", JSONValue.Parse("{}")!));
        Assert.IsType<InvalidOperationException>(execute);
    }
}
