// SK-11f / WU-12 r2 and r3 (skills-PLAN.md 3.3 native row, Q17 and review finding 6): the user's own skills (~/.agents/skills) reach an API provider only
// behind a switch that is off by default. While it is off, nothing from that folder is read, listed, sent or readable; while it is on the user's entry wins a
// name clash and is counted before Marrow's entries in the budget.
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class OwnSkillsSwitchTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "sk11f-fake-home", ".agents", "skills");

    private static string Skill(string name, string description, string body) => $"---\nname: {name}\ndescription: {description}\n---\n{body}\n";

    private static RecordingUserSkillFiles Folder()
    {
        var files = new RecordingUserSkillFiles();
        files.Add(Path.Combine(Root, "private-skill", "SKILL.md"), Skill("private-skill", "PRIVATE DESCRIPTION", "PRIVATE BODY SENTINEL"));
        files.Add(Path.Combine(Root, "private-skill", "references", "notes.txt"), "PRIVATE NOTES SENTINEL");
        files.Add(Path.Combine(Root, "pdf", "SKILL.md"), Skill("pdf", "Own pdf skill", "OWN PDF BODY"));
        files.Add(Path.Combine(Root, "secret.txt"), "OUTSIDE SENTINEL");
        return files;
    }

    private static Task<NativeToolResult> Use(NativeSkillTools tools, string json) => tools.Execute("use_skill", JSONValue.Parse(json)!);

    [Fact, Trait("Rule", "R-TEST-10")]
    public async Task With_the_switch_off_the_folder_is_never_opened_and_nothing_of_it_is_listed_sent_or_readable()
    {
        var files = Folder();
        var own = new NativeUserSkills(Root, enabled: false, files);
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start(existing: own.Existing());
        var tools = new NativeSkillTools(generation, own);

        var block = tools.CatalogueBlock()!;
        Assert.Equal("Skills (call use_skill with a skill's name to read its instructions):\npdf: Fixture description", block);
        var refused = await Use(tools, """{"name":"private-skill"}""");
        var refusedFile = await Use(tools, """{"name":"private-skill","file":"references/notes.txt"}""");
        Assert.Equal("Error: unknown skill private-skill.", refused.Output);
        Assert.Equal("Error: unknown skill private-skill.", refusedFile.Output);
        Assert.Empty(own.List());
        Assert.Empty(own.Existing());
        Assert.Null(own.Read("private-skill", "SKILL.md"));
        foreach (var text in new[] { block, string.Concat(tools.Definitions.Select(d => d.CompactString)) })
        {
            Assert.DoesNotContain("private-skill", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("PRIVATE", refused.Output + refusedFile.Output, StringComparison.Ordinal); // the refusal repeats only the name the model typed
        Assert.Empty(files.Accesses);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void With_the_switch_on_the_own_skills_are_listed_before_Marrows_and_win_a_name_clash()
    {
        var files = Folder();
        var own = new NativeUserSkills(Root, enabled: true, files);
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        kit.Add("O3", "doc");
        using var generation = kit.Start(existing: own.Existing());
        Assert.Equal(["doc"], generation.Skills.Select(s => s.Name)); // the generation already left Marrow's pdf out: the user's pdf wins
        var lines = new NativeSkillTools(generation, own).CatalogueBlock()!.Split('\n');
        Assert.Equal(
            ["Skills (call use_skill with a skill's name to read its instructions):",
                "pdf: Own pdf skill", "private-skill: PRIVATE DESCRIPTION", "doc: Fixture description"],
            lines);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void A_clash_the_generation_did_not_remove_is_still_listed_once_with_the_users_description()
    {
        var own = new NativeUserSkills(Root, enabled: true, Folder());
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start(); // the caller forgot to pass the user's skills as the existing set
        Assert.Equal(["pdf"], generation.Skills.Select(s => s.Name));
        var lines = new NativeSkillTools(generation, own).CatalogueBlock()!.Split('\n').Skip(1).ToArray();
        Assert.Equal(["pdf: Own pdf skill", "private-skill: PRIVATE DESCRIPTION"], lines);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public async Task With_the_switch_on_use_skill_reads_the_users_skill_and_only_inside_its_folder()
    {
        var files = Folder();
        var own = new NativeUserSkills(Root, enabled: true, files);
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start(existing: own.Existing());
        var tools = new NativeSkillTools(generation, own);

        var skill = await Use(tools, """{"name":"private-skill"}""");
        Assert.False(skill.Failed);
        Assert.StartsWith($"Skill folder: {Path.Combine(Root, "private-skill")}\n\n---\nname: private-skill", skill.Output, StringComparison.Ordinal);
        Assert.Contains("PRIVATE BODY SENTINEL", skill.Output, StringComparison.Ordinal);
        Assert.Equal("private-skill", Assert.IsType<Marrow.Core.Port.Skills.Detect.SkillUsed>(Assert.Single(skill.Events)).Skill);
        Assert.Null(((Marrow.Core.Port.Skills.Detect.SkillUsed)skill.Events[0]).ItemId);

        Assert.Equal("PRIVATE NOTES SENTINEL", (await Use(tools, """{"name":"private-skill","file":"references/notes.txt"}""")).Output);
        var clash = await Use(tools, """{"name":"pdf"}""");
        Assert.Contains("OWN PDF BODY", clash.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("BODY SENTINEL", clash.Output, StringComparison.Ordinal);

        foreach (var escape in new[] { "../secret.txt", "../pdf/SKILL.md", "/secret.txt", "..\\secret.txt", "C:\\secret.txt", "references/missing.txt" })
        {
            var refused = await Use(tools, $$"""{"name":"private-skill","file":{{System.Text.Json.JsonSerializer.Serialize(escape)}}}""");
            Assert.True(refused.Failed, escape);
            Assert.StartsWith("Error: ", refused.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("OUTSIDE SENTINEL", refused.Output, StringComparison.Ordinal);
        }
        Assert.All(files.Accesses.Where(a => a.StartsWith("read:", StringComparison.Ordinal)),
            access => Assert.StartsWith("read:" + Root + Path.DirectorySeparatorChar, access, StringComparison.Ordinal));
        Assert.DoesNotContain(files.Accesses, a => a.EndsWith("secret.txt", StringComparison.Ordinal));
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void The_users_entries_keep_their_whole_descriptions_and_with_Marrows_stay_inside_the_budget()
    {
        var files = new RecordingUserSkillFiles();
        files.Add(Path.Combine(Root, "own-one", "SKILL.md"), Skill("own-one", new string('a', 300), "x"));
        files.Add(Path.Combine(Root, "own-two", "SKILL.md"), Skill("own-two", new string('b', 300), "x"));
        var own = new NativeUserSkills(Root, enabled: true, files);
        using var kit = new NativeSkillsKit { Budget = new GenerationBudget(2000) };
        for (var i = 0; i < 50; i++) kit.Add($"S{i:D2}", $"skill-{i:D2}");
        using var generation = kit.Start(existing: own.Existing());
        var block = new NativeSkillTools(generation, own).CatalogueBlock()!;
        var lines = block.Split('\n');
        Assert.Equal("own-one: " + new string('a', 300), lines[1]);
        Assert.Equal("own-two: " + new string('b', 300), lines[2]);
        Assert.True(block.Length <= 2000, $"the block is {block.Length} characters and the budget 2000");
        Assert.NotEmpty(generation.LeftOut);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Entries_with_unusable_metadata_are_skipped_and_the_rest_still_load()
    {
        var files = new RecordingUserSkillFiles();
        files.Add(Path.Combine(Root, "broken", "SKILL.md"), "no frontmatter at all");
        files.Add(Path.Combine(Root, "Bad Name", "SKILL.md"), Skill("Bad Name", "d", "b"));
        files.Add(Path.Combine(Root, "good", "SKILL.md"), Skill("good", "A good skill", "b"));
        var own = new NativeUserSkills(Root, enabled: true, files);
        Assert.Equal([new OwnSkill("good", "A good skill", Path.Combine(Root, "good"))], own.List());
        Assert.Equal([new ExistingSkill("good", "A good skill")], own.Existing());
        Assert.Single(files.Accesses, a => a.StartsWith("list:", StringComparison.Ordinal)); // listed once, kept for the session
        Assert.Single(own.Existing());
        Assert.Single(files.Accesses, a => a.StartsWith("list:", StringComparison.Ordinal));
    }

    [Fact, Trait("Rule", "R-TEST-10")]
    public void A_folder_that_cannot_be_read_lists_nothing_instead_of_failing_the_session()
    {
        var own = new NativeUserSkills(Root, enabled: true, new UnreadableUserSkillFiles());
        Assert.Empty(own.List());
        Assert.Empty(own.Existing());
        using var kit = new NativeSkillsKit();
        kit.Add("A1", "pdf");
        using var generation = kit.Start();
        Assert.Equal("Skills (call use_skill with a skill's name to read its instructions):\npdf: Fixture description", new NativeSkillTools(generation, own).CatalogueBlock());
    }
}
