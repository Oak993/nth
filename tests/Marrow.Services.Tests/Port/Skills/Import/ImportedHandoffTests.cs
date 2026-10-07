using System.Text;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

// SK-18 (OD-0118, point 4): imported skills flow into what Marrow hands Claude, Codex and Antigravity, through the same generations as every other skill (SK-9): same switches, same clash rule, same budget.
public sealed class ImportedHandoffTests
{
    private static ImportedSkillsLibrary Library(ForeignHome home) => new(home.SkillsRoot, new ManualTimeProvider());

    private static SkillGenerations Service(GenerationFixture f, IImportedSkillSource? source, IEnumerable<ExistingSkill>? existing = null)
    {
        f.Open();
        return new SkillGenerations(f.Paths, f.Store!, f.Protection, _ => [.. existing ?? []], source);
    }

    private static void Import(ForeignHome home, ImportedSkillsLibrary library, ImportMode mode = ImportMode.Copy) =>
        library.Import(new ForeignSkillScanner(home.Home).Scan().Skills, mode, _ => DuplicateChoice.KeepBoth);

    [Theory]
    [InlineData("claude", "skills/notes/SKILL.md")]
    [InlineData("codex", "notes/SKILL.md")]
    [InlineData("agy", "notes/SKILL.md")]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void An_imported_skill_is_in_the_generation_of_each_provider_at_that_providers_place(string provider, string relative)
    {
        using var home = new ForeignHome();
        home.Skill(".cursor/skills", "notes", "notes", "Take notes.", ("references/guide.md", "GUIDE"));
        var library = Library(home);
        Import(home, library);
        using var f = new GenerationFixture();
        using var service = Service(f, library);
        using var g = service.Start(provider, "chat", [], f.Switches(), f.Budget);
        var skill = Assert.Single(g.Skills);
        Assert.Equal(("notes", "Take notes."), (skill.Name, skill.Description));
        Assert.True(File.Exists(Path.Combine(g.Folder, relative.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Contains("notes: Take notes.", g.Catalogue, StringComparison.Ordinal);
        Assert.Contains("BODY SENTINEL", g.Invoke("notes"), StringComparison.Ordinal);
        Assert.Equal("GUIDE", g.ReadReference("notes", "references/guide.md"));
        Assert.Equal(provider == "claude", File.Exists(Path.Combine(g.Folder, ".claude-plugin", "plugin.json")));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_generation_with_no_imports_is_exactly_what_it_was_before()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var plain = f.Open();
        using var home = new ForeignHome();
        var empty = Library(home);
        using var withEmpty = new SkillGenerations(f.Paths, f.Store!, f.Protection, _ => [], empty);
        var switches = f.Switches((item, true));
        Assert.Equal(plain.Prepare("codex", [item], switches, f.Budget, []), withEmpty.Prepare("codex", [item], switches, f.Budget, []));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_skill_turned_off_in_the_library_is_not_handed_out_and_the_key_changes_with_every_change()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Take notes.");
        var library = Library(home);
        Import(home, library);
        using var f = new GenerationFixture();
        using var service = Service(f, library);
        var on = service.Prepare("claude", [], f.Switches(), f.Budget, []);
        library.SetEnabled(library.List().Single().Id, false);
        var off = service.Prepare("claude", [], f.Switches(), f.Budget, []);
        library.SetEnabled(library.List().Single().Id, true);
        Assert.NotEqual(on, off);
        Assert.Equal(on, service.Prepare("claude", [], f.Switches(), f.Budget, []));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_linked_skill_that_changes_gives_the_next_chat_a_new_generation_and_a_running_chat_keeps_its_own()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".claude/skills", "notes", "notes", "Take notes.");
        var library = Library(home);
        Import(home, library, ImportMode.Link);
        using var f = new GenerationFixture();
        using var service = Service(f, library);
        using var running = service.Start("codex", "first", [], f.Switches(), f.Budget);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), ForeignHome.SkillText("notes", "Take better notes.", "CHANGED BODY"));
        using var next = service.Start("codex", "second", [], f.Switches(), f.Budget);
        Assert.NotEqual(running.Key, next.Key);
        Assert.Contains("BODY SENTINEL", running.Invoke("notes"), StringComparison.Ordinal);
        Assert.Contains("CHANGED BODY", next.Invoke("notes"), StringComparison.Ordinal);
        Assert.Equal("Take better notes.", Assert.Single(next.Skills).Description);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void An_imported_skill_that_clashes_with_a_skill_the_provider_already_has_is_left_out_like_any_other()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Take notes.");
        home.Skill(".claude/skills", "plan", "plan", "Plan.");
        var library = Library(home);
        Import(home, library);
        using var f = new GenerationFixture();
        using var service = Service(f, library, [new ExistingSkill("notes", "Already there")]);
        using var g = service.Start("claude", "chat", [], f.Switches(), f.Budget);
        Assert.Equal("plan", Assert.Single(g.Skills).Name);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_catalogue_skill_wins_a_name_and_the_budget_applies_to_imports_too()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "pdf", "pdf", "Imported pdf.");
        home.Skill(".claude/skills", "long", "long", new string('x', 1500));
        var library = Library(home);
        Import(home, library);
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        using var service = Service(f, library);
        using var g = service.Start("codex", "chat", [item], f.Switches((item, true)), new GenerationBudget(1720));
        Assert.Equal("Fixture description", Assert.Single(g.Skills).Description);
        Assert.Contains(g.LeftOut, id => id.StartsWith("import/", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Whatever_the_other_app_wrote_the_handed_out_skill_carries_a_front_matter_the_strict_reader_accepts()
    {
        using var home = new ForeignHome();
        home.Raw(".claude/skills/odd/SKILL.md", "﻿---\r\nname: odd\r\ndescription: >\r\n  Folded across\r\n  two lines\r\nallowed-tools: [Read]\r\n---\r\nBody\r\n");
        home.Raw(".cursor/rules/rule-one.mdc", "---\ndescription: A rule\n---\nDo it.\n");
        var library = Library(home);
        Import(home, library);
        using var f = new GenerationFixture();
        using var service = Service(f, library);
        using var g = service.Start("claude", "chat", [], f.Switches(), f.Budget);
        Assert.Equal(["odd", "rule-one"], g.Skills.Select(s => s.Name).Order());
        foreach (var skill in g.Skills)
        {
            var strict = SkillFrontmatter.Disclosure(File.ReadAllText(skill.File, new UTF8Encoding(false, true)));
            Assert.Equal((skill.Name, skill.Description), (strict.Name, strict.Description));
        }
    }
}
