using System.Text;
using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

// SK-18 (OD-0118): Copy (Marrow's own) and Link (kept in sync), duplicates (keep both, replace, skip), the quiet "from <app>" tag, remove. Temp folders only.
public sealed class ImportedSkillsLibraryTests
{
    private static ImportedSkillsLibrary Open(ForeignHome home, ManualTimeProvider? clock = null) => new(home.SkillsRoot, clock ?? new ManualTimeProvider());

    private static ForeignSkill Find(ForeignHome home, string name, params string[] picked) =>
        new ForeignSkillScanner(home.Home, picked).Scan().Skills.Single(s => s.Name == name);

    private static DuplicateChoice NeverAsked(DuplicateQuestion q) => throw new InvalidOperationException("no duplicate expected: " + q.Incoming.Name);


    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Copy_brings_the_whole_skill_folder_into_marrows_library_and_leaves_the_original_alone()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "pdf-tools", "pdf-tools", "Work with PDF files.", ("references/guide.md", "GUIDE"), ("scripts/run.sh", "echo hi"));
        var before = ForeignHome.Fingerprint(home.Home);
        var library = Open(home);
        var result = library.Import([Find(home, "pdf-tools")], ImportMode.Copy, NeverAsked);
        var imported = Assert.Single(result.Imported);
        Assert.Equal(("pdf-tools", ForeignApp.ClaudeCode, ImportMode.Copy, true), (imported.Name, imported.App, imported.Mode, imported.Enabled));
        Assert.Equal(before, ForeignHome.Fingerprint(home.Home));
        var files = library.Snapshot().Single().Files;
        Assert.Equal(["SKILL.md", "references/guide.md", "scripts/run.sh"], files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("GUIDE", Encoding.UTF8.GetString(files["references/guide.md"]));
        Assert.All(Directory.EnumerateFiles(home.SkillsRoot, "*", SearchOption.AllDirectories), f => Assert.Contains("imported", f, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_copy_outlives_the_original_and_the_library_survives_a_restart()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".codex/skills", "plan", "plan", "Plan the work.");
        var clock = new ManualTimeProvider();
        var first = Open(home, clock).Import([Find(home, "plan")], ImportMode.Copy, NeverAsked).Imported.Single();
        Directory.Delete(folder, recursive: true);
        var reopened = Open(home, clock);
        var listed = Assert.Single(reopened.List());
        Assert.Equal((first.Id, first.ImportedAt, false), (listed.Id, listed.ImportedAt, listed.OriginalMissing));
        Assert.Equal("plan", Assert.Single(reopened.Snapshot()).Name);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Link_copies_nothing_and_follows_the_original_when_it_changes()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".claude/skills", "notes", "notes", "Take notes.");
        var library = Open(home);
        var linked = library.Import([Find(home, "notes")], ImportMode.Link, NeverAsked).Imported.Single();
        Assert.Equal(ImportMode.Link, linked.Mode);
        Assert.DoesNotContain(Directory.EnumerateFiles(home.SkillsRoot, "SKILL.md", SearchOption.AllDirectories), _ => true);
        Assert.Equal("Take notes.", library.Snapshot().Single().Description);

        File.WriteAllText(Path.Combine(folder, "SKILL.md"), ForeignHome.SkillText("notes", "Take better notes.", "NEW BODY"));
        File.WriteAllText(Path.Combine(folder, "extra.txt"), "added later");
        var now = library.Snapshot().Single();
        Assert.Equal("Take better notes.", now.Description);
        Assert.Contains("NEW BODY", Encoding.UTF8.GetString(now.Files["SKILL.md"]), StringComparison.Ordinal);
        Assert.Contains("extra.txt", now.Files.Keys);
        Assert.Equal("Take better notes.", library.List().Single().Description);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_link_whose_original_goes_away_pauses_and_comes_back_with_it()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".claude/skills", "notes", "notes", "Take notes.");
        var library = Open(home);
        library.Import([Find(home, "notes")], ImportMode.Link, NeverAsked);
        var moved = folder + "-moved";
        Directory.Move(folder, moved);
        Assert.Empty(library.Snapshot());
        Assert.True(Assert.Single(library.List()).OriginalMissing);
        Directory.Move(moved, folder);
        Assert.Single(library.Snapshot());
        Assert.False(Assert.Single(library.List()).OriginalMissing);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_cursor_rule_becomes_a_skill_for_copy_and_for_link()
    {
        using var home = new ForeignHome();
        home.Raw(".cursor/rules/style.mdc", "---\ndescription: House style for code\nglobs: \"**/*.cs\"\n---\nUse tabs.\nKeep lines short.\n");
        var rule = Find(home, "style");
        var library = Open(home);
        library.Import([rule], ImportMode.Copy, NeverAsked);
        var skill = library.Snapshot().Single();
        var text = Encoding.UTF8.GetString(skill.Files["SKILL.md"]);
        Assert.Equal(("style", "House style for code"), (skill.Name, skill.Description));
        Assert.StartsWith("---\nname: style\ndescription: House style for code\n---\n", text, StringComparison.Ordinal);
        Assert.Contains("Use tabs.\nKeep lines short.", text, StringComparison.Ordinal);

        using var other = new ForeignHome();
        other.Raw(".cursor/rules/style.mdc", "Plain rule text\nmore\n");
        var linkLibrary = Open(other);
        linkLibrary.Import([new ForeignSkillScanner(other.Home).Scan().Skills.Single()], ImportMode.Link, NeverAsked);
        File.WriteAllText(Path.Combine(other.Home, ".cursor/rules/style.mdc"), "Changed first line\nmore\n");
        Assert.Equal("Changed first line", linkLibrary.Snapshot().Single().Description);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Keep_both_gives_the_second_skill_a_name_of_its_own_and_changes_only_marrows_copy()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "docx", "docx", "Claude's way.");
        home.Skill(".codex/skills", "docx", "docx", "Codex's way.");
        var scan = new ForeignSkillScanner(home.Home).Scan().Skills;
        var library = Open(home);
        library.Import([scan.Single(s => s.App == ForeignApp.ClaudeCode)], ImportMode.Copy, NeverAsked);
        var asked = new List<DuplicateQuestion>();
        var result = library.Import([scan.Single(s => s.App == ForeignApp.Codex)], ImportMode.Copy, q => { asked.Add(q); return DuplicateChoice.KeepBoth; });
        Assert.Equal(("docx", ForeignApp.ClaudeCode), (asked.Single().Existing.Name, asked.Single().Existing.App));
        Assert.Equal("docx-codex", Assert.Single(result.Imported).Name);
        Assert.Equal(["docx", "docx-codex"], library.Snapshot().Select(s => s.Name).Order());
        foreach (var skill in library.Snapshot())
            Assert.Equal(skill.Name, ForeignFrontmatter.Parse(Encoding.UTF8.GetString(skill.Files["SKILL.md"])).Name);
        Assert.Contains("name: docx", File.ReadAllText(Path.Combine(home.Home, ".codex/skills/docx/SKILL.md")), StringComparison.Ordinal);
        Assert.DoesNotContain("docx-codex", File.ReadAllText(Path.Combine(home.Home, ".codex/skills/docx/SKILL.md")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Keep_both_for_a_link_renames_it_in_what_is_handed_out_not_in_the_original()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "docx", "docx", "Claude's way.");
        home.Skill(".gemini/skills", "docx", "docx", "Antigravity's way.");
        var scan = new ForeignSkillScanner(home.Home).Scan().Skills;
        var library = Open(home);
        library.Import(scan, ImportMode.Link, q => DuplicateChoice.KeepBoth);
        var names = library.Snapshot().Select(s => s.Name).Order().ToList();
        Assert.Equal(["docx", "docx-antigravity"], names);
        var renamed = library.Snapshot().Single(s => s.Name == "docx-antigravity");
        Assert.Equal("docx-antigravity", ForeignFrontmatter.Parse(Encoding.UTF8.GetString(renamed.Files["SKILL.md"])).Name);
        Assert.Contains("name: docx\n", File.ReadAllText(Path.Combine(home.Home, ".gemini/skills/docx/SKILL.md")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Replace_swaps_the_old_import_for_the_new_one_and_keeps_its_switch()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "docx", "docx", "Claude's way.", ("old.txt", "OLD"));
        home.Skill(".codex/skills", "docx", "docx", "Codex's way.", ("new.txt", "NEW"));
        var scan = new ForeignSkillScanner(home.Home).Scan().Skills;
        var library = Open(home);
        var first = library.Import([scan.Single(s => s.App == ForeignApp.ClaudeCode)], ImportMode.Copy, NeverAsked).Imported.Single();
        library.SetEnabled(first.Id, false);
        var result = library.Import([scan.Single(s => s.App == ForeignApp.Codex)], ImportMode.Copy, _ => DuplicateChoice.Replace);
        Assert.Equal(first.Id, Assert.Single(result.Replaced).Id);
        var now = Assert.Single(library.List());
        Assert.Equal((ForeignApp.Codex, "Codex's way.", false), (now.App, now.Description, now.Enabled));
        Assert.Empty(library.Snapshot());
        library.SetEnabled(now.Id, true);
        Assert.Equal(["SKILL.md", "new.txt"], library.Snapshot().Single().Files.Keys.Order(StringComparer.Ordinal));
        Assert.Single(Directory.GetDirectories(Path.Combine(home.SkillsRoot, "imported", "copies")));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Skip_changes_nothing_and_says_so()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "docx", "docx", "Claude's way.");
        home.Skill(".codex/skills", "docx", "docx", "Codex's way.");
        var scan = new ForeignSkillScanner(home.Home).Scan().Skills;
        var library = Open(home);
        library.Import([scan.Single(s => s.App == ForeignApp.ClaudeCode)], ImportMode.Copy, NeverAsked);
        var before = ForeignHome.Fingerprint(home.SkillsRoot);
        var result = library.Import([scan.Single(s => s.App == ForeignApp.Codex)], ImportMode.Copy, _ => DuplicateChoice.Skip);
        Assert.Equal("docx", Assert.Single(result.Skipped).Name);
        Assert.Empty(result.Imported);
        Assert.Equal(before, ForeignHome.Fingerprint(home.SkillsRoot));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Importing_the_same_original_again_is_a_duplicate_and_two_equal_names_in_one_batch_ask_too()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Notes.");
        var skill = Find(home, "notes");
        var library = Open(home);
        library.Import([skill], ImportMode.Copy, NeverAsked);
        var asked = 0;
        library.Import([skill], ImportMode.Copy, _ => { asked++; return DuplicateChoice.Skip; });
        Assert.Equal(1, asked);
        Assert.NotNull(library.FindDuplicate(skill));

        using var batch = new ForeignHome();
        batch.Skill(".claude/skills", "x", "x", "One.");
        batch.Skill(".codex/skills", "x", "x", "Two.");
        var fresh = Open(batch);
        var questions = new List<string>();
        fresh.Import(new ForeignSkillScanner(batch.Home).Scan().Skills, ImportMode.Copy, q => { questions.Add(q.Incoming.App.ToString()); return DuplicateChoice.KeepBoth; });
        Assert.Equal(["Codex"], questions);
        Assert.Equal(2, fresh.List().Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Remove_deletes_the_import_and_marrows_copy_and_never_touches_the_original()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Notes.", ("a.txt", "A"));
        home.Skill(".codex/skills", "plan", "plan", "Plan.");
        var library = Open(home);
        var imported = library.Import(new ForeignSkillScanner(home.Home).Scan().Skills, ImportMode.Copy, NeverAsked).Imported;
        var sources = ForeignHome.Fingerprint(home.Home);
        Assert.True(library.Remove(imported.Single(i => i.Name == "notes").Id));
        Assert.False(library.Remove("not-an-id"));
        Assert.Equal(["plan"], library.List().Select(i => i.Name));
        Assert.Single(Directory.GetDirectories(Path.Combine(home.SkillsRoot, "imported", "copies")));
        Assert.Equal(sources, ForeignHome.Fingerprint(home.Home));
        Assert.Equal(["plan"], Open(home).List().Select(i => i.Name));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Turning_an_imported_skill_off_keeps_it_but_stops_handing_it_out()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "notes", "notes", "Notes.");
        var library = Open(home);
        var id = library.Import([Find(home, "notes")], ImportMode.Copy, NeverAsked).Imported.Single().Id;
        library.SetEnabled(id, false);
        Assert.Empty(library.Snapshot());
        Assert.False(Open(home).List().Single().Enabled);
        Assert.Throws<KeyNotFoundException>(() => library.SetEnabled("nope", true));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void One_skill_that_cannot_be_imported_is_reported_and_leaves_no_half_copy_and_the_rest_go_in()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "fine", "fine", "Fine.");
        home.Skill(".claude/skills", "huge", "huge", "Huge folder.", ("blob.bin", new string('x', 6 * 1024 * 1024)));
        var library = Open(home);
        var result = library.Import(new ForeignSkillScanner(home.Home).Scan().Skills, ImportMode.Copy, NeverAsked);
        Assert.Equal(["fine"], result.Imported.Select(i => i.Name));
        var failure = Assert.Single(result.Failed);
        Assert.Equal("huge", failure.Skill.Name);
        Assert.False(string.IsNullOrWhiteSpace(failure.Reason));
        Assert.Single(Directory.GetDirectories(Path.Combine(home.SkillsRoot, "imported", "copies")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(home.SkillsRoot, "imported"), "*.tmp*", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_link_inside_a_skill_folder_is_not_followed_when_copying()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".claude/skills", "notes", "notes", "Notes.");
        var secret = Path.Combine(home.Root, "secret.txt");
        File.WriteAllText(secret, "SECRET");
        File.CreateSymbolicLink(Path.Combine(folder, "leak.txt"), secret);
        var library = Open(home);
        library.Import([Find(home, "notes")], ImportMode.Copy, NeverAsked);
        Assert.Equal(["SKILL.md"], library.Snapshot().Single().Files.Keys);
        Assert.DoesNotContain("SECRET", string.Join("", Directory.EnumerateFiles(home.SkillsRoot, "*", SearchOption.AllDirectories).Select(File.ReadAllText)), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unreadable_index_loads_empty_with_a_reason_and_is_kept_aside_before_it_is_replaced()
    {
        using var home = new ForeignHome();
        Directory.CreateDirectory(Path.Combine(home.SkillsRoot, "imported"));
        var index = Path.Combine(home.SkillsRoot, "imported", "index.json");
        File.WriteAllText(index, "{ not json");
        home.Skill(".claude/skills", "notes", "notes", "Notes.");
        var library = Open(home);
        Assert.Empty(library.List());
        Assert.NotNull(library.LoadProblem);
        library.Import([Find(home, "notes")], ImportMode.Copy, NeverAsked);
        Assert.Equal("{ not json", File.ReadAllText(index + ".unreadable"));
        Assert.Single(Open(home).List());
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void An_index_from_a_newer_marrow_is_read_but_never_written_over()
    {
        using var home = new ForeignHome();
        Directory.CreateDirectory(Path.Combine(home.SkillsRoot, "imported"));
        var index = Path.Combine(home.SkillsRoot, "imported", "index.json");
        const string text = "{\"version\":9,\"skills\":[],\"future\":true}";
        File.WriteAllText(index, text);
        home.Skill(".claude/skills", "notes", "notes", "Notes.");
        var library = Open(home);
        Assert.True(library.ReadOnly);
        Assert.Throws<InvalidOperationException>(() => library.Import([Find(home, "notes")], ImportMode.Copy, NeverAsked));
        Assert.Equal(text, File.ReadAllText(index));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_index_names_the_app_and_where_the_skill_came_from_for_the_quiet_tag()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/plugins/cache/market/superpower/1.0.0/skills", "brainstorm", "brainstorm", "Ideas.");
        var library = Open(home);
        library.Import(new ForeignSkillScanner(home.Home).Scan().Skills, ImportMode.Link, NeverAsked);
        var item = library.List().Single();
        Assert.Equal((ForeignApp.ClaudeCode, "superpower"), (item.App, item.SourceLabel));
        var json = File.ReadAllText(Path.Combine(home.SkillsRoot, "imported", "index.json"));
        Assert.Equal(1, System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_library_can_be_read_while_a_duplicate_question_waits_for_its_answer()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "pdf", "pdf", "Work with PDF files.");
        var library = Open(home);
        library.Import([Find(home, "pdf")], ImportMode.Copy, NeverAsked);
        var asking = new ManualResetEventSlim();
        var answer = new ManualResetEventSlim();
        var import = Task.Run(() => library.Import([Find(home, "pdf")], ImportMode.Copy, _ =>
        {
            asking.Set();
            Assert.True(answer.Wait(TimeSpan.FromSeconds(10)));
            return DuplicateChoice.Skip;
        }));
        Assert.True(asking.Wait(TimeSpan.FromSeconds(10)));
        var read = Task.Run(() => (library.List().Count, library.Snapshot().Count));
        Assert.True(read.Wait(TimeSpan.FromSeconds(5)), "the library was locked while the question waited");
        Assert.Equal((1, 1), read.Result);
        answer.Set();
        Assert.Single(import.Result.Skipped);
    }
}
