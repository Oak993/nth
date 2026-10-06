using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

// SK-18 (OD-0118): the logic behind Settings > Skills > "Import skills": scan, group by app, tick, Copy or Link, Import all, the duplicate question, the result. No Avalonia.
public sealed class ImportControllerTests
{
    private static ImportController Make(ForeignHome home, out ImportedSkillsLibrary library, Func<Task<string?>>? pick = null)
    {
        library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        return new ImportController(folders => new ForeignSkillScanner(home.Home, folders).Scan(), library, pick);
    }

    private static void Fill(ForeignHome home)
    {
        home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
        home.Skill(".claude/skills", "beta", "beta", "Beta skill.");
        home.Skill(".codex/skills", "gamma", "gamma", "Gamma skill.");
        home.Raw(".cursor/rules/delta.mdc", "---\ndescription: Delta rule\n---\nbody\n");
    }

    private static async Task Wait(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Opening_scans_and_groups_what_was_found_by_app_with_nothing_ticked()
    {
        using var home = new ForeignHome();
        Fill(home);
        var controller = Make(home, out _);
        Assert.Equal(ImportPhase.Closed, controller.Phase);
        var open = controller.OpenAsync();
        Assert.Equal(ImportPhase.Scanning, controller.Phase);
        await open;
        Assert.Equal(ImportPhase.Choosing, controller.Phase);
        Assert.Equal([ImportWords.ClaudeCode, ImportWords.Codex, ImportWords.Cursor], controller.Groups.Select(g => g.Title));
        Assert.Equal(["alpha", "beta"], controller.Groups[0].Rows.Select(r => r.Name));
        Assert.Equal("Alpha skill.", controller.Groups[0].Rows[0].Description);
        Assert.Equal(("Rules", false, false), (controller.Groups[2].Rows[0].Source, controller.Groups[2].Rows[0].Ticked, controller.Groups[2].AllTicked));
        Assert.Equal(0, controller.TickedCount);
        Assert.Equal(ImportMode.Copy, controller.Mode);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Ticks_are_per_skill_and_per_app_and_only_the_ticked_ones_are_imported_by_Import()
    {
        using var home = new ForeignHome();
        Fill(home);
        var controller = Make(home, out var library);
        await controller.OpenAsync();
        controller.Toggle(controller.Groups[0].Rows[0].Key);
        Assert.Equal(1, controller.TickedCount);
        controller.ToggleGroup(ForeignApp.Codex);
        Assert.True(controller.Groups[1].AllTicked);
        Assert.Equal(2, controller.TickedCount);
        controller.ToggleGroup(ForeignApp.Codex);
        Assert.Equal(1, controller.TickedCount);
        await controller.ImportSelectedAsync();
        Assert.Equal(ImportPhase.Done, controller.Phase);
        Assert.Equal(["alpha"], library.List().Select(i => i.Name));
        Assert.Equal(ImportWords.Done(1, 0, 0, 0), controller.Summary);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Import_all_takes_everything_found_and_the_mode_chosen_is_kept()
    {
        using var home = new ForeignHome();
        Fill(home);
        var controller = Make(home, out var library);
        await controller.OpenAsync();
        controller.SetMode(ImportMode.Link);
        await controller.ImportAllAsync();
        Assert.Equal(4, library.List().Count);
        Assert.All(library.List(), i => Assert.Equal(ImportMode.Link, i.Mode));
        Assert.Equal(4, controller.Imported.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Skills_already_imported_are_shown_as_such_and_are_not_ticked_by_a_group_tick()
    {
        using var home = new ForeignHome();
        Fill(home);
        var controller = Make(home, out _);
        await controller.OpenAsync();
        await controller.ImportAllAsync();
        controller.Close();
        await controller.OpenAsync();
        Assert.All(controller.Groups.SelectMany(g => g.Rows), r => Assert.True(r.AlreadyImported));
        controller.ToggleGroup(ForeignApp.ClaudeCode);
        Assert.Equal(0, controller.TickedCount);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_duplicate_stops_the_import_with_a_question_and_the_answer_decides()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "docx", "docx", "Claude's way.");
        home.Skill(".codex/skills", "docx", "docx", "Codex's way.");
        var controller = Make(home, out var library);
        await controller.OpenAsync();
        var import = controller.ImportAllAsync();
        await Wait(() => controller.Phase == ImportPhase.AskingDuplicate);
        Assert.Equal(("docx", ForeignApp.Codex), (controller.Question!.Existing.Name, controller.Question.Incoming.App));
        controller.AnswerDuplicate(DuplicateChoice.KeepBoth, applyToRest: false);
        await import;
        Assert.Equal(ImportPhase.Done, controller.Phase);
        Assert.Equal(["docx", "docx-codex"], library.List().Select(i => i.Name).Order());
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task One_answer_can_be_applied_to_every_other_duplicate()
    {
        using var home = new ForeignHome();
        foreach (var name in new[] { "a", "b", "c" })
        {
            home.Skill(".claude/skills", name, name, "Claude " + name);
            home.Skill(".codex/skills", name, name, "Codex " + name);
        }
        var controller = Make(home, out var library);
        await controller.OpenAsync();
        var import = controller.ImportAllAsync();
        await Wait(() => controller.Phase == ImportPhase.AskingDuplicate);
        controller.AnswerDuplicate(DuplicateChoice.Skip, applyToRest: true);
        await import;
        Assert.Equal(3, library.List().Count);
        Assert.All(library.List(), i => Assert.Equal(ForeignApp.ClaudeCode, i.App));
        Assert.Equal(ImportWords.Done(3, 0, 3, 0), controller.Summary);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_folder_the_user_chooses_is_added_to_the_scan_and_cancelling_the_chooser_adds_nothing()
    {
        using var home = new ForeignHome();
        var picked = Path.Combine(home.Root, "mine");
        Directory.CreateDirectory(Path.Combine(picked, "own-skill"));
        File.WriteAllText(Path.Combine(picked, "own-skill", "SKILL.md"), ForeignHome.SkillText("own-skill", "My own."));
        var answers = new Queue<string?>([null, picked]);
        var controller = Make(home, out _, () => Task.FromResult(answers.Dequeue()));
        await controller.OpenAsync();
        Assert.Empty(controller.Groups);
        await controller.AddFolderAsync();
        Assert.Empty(controller.Groups);
        await controller.AddFolderAsync();
        var group = Assert.Single(controller.Groups);
        Assert.Equal((ImportWords.Folders, "own-skill"), (group.Title, group.Rows.Single().Name));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task What_could_not_be_read_is_counted_and_never_stops_the_list()
    {
        using var home = new ForeignHome();
        Fill(home);
        home.Raw(".claude/skills/broken/SKILL.md", "---\nunclosed");
        var controller = Make(home, out _);
        await controller.OpenAsync();
        Assert.Equal(1, controller.ProblemCount);
        Assert.Equal(4, controller.Groups.Sum(g => g.Rows.Count));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Remove_and_the_switch_go_to_the_library_and_the_view_is_told_about_each_change()
    {
        using var home = new ForeignHome();
        Fill(home);
        var controller = Make(home, out var library);
        var changes = 0;
        controller.Changed += () => changes++;
        await controller.OpenAsync();
        await controller.ImportAllAsync();
        var before = controller.Revision;
        var id = library.List().First().Id;
        controller.SetEnabled(id, false);
        Assert.False(library.List().First(i => i.Id == id).Enabled);
        Assert.True(controller.Remove(id));
        Assert.Equal(3, library.List().Count);
        Assert.True(controller.Revision > before);
        Assert.True(changes >= 4);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Closing_the_sheet_forgets_the_ticks_and_a_scan_that_finds_nothing_says_so()
    {
        using var home = new ForeignHome();
        Fill(home);
        var controller = Make(home, out _);
        await controller.OpenAsync();
        controller.ToggleGroup(ForeignApp.Codex);
        controller.Close();
        Assert.Equal(ImportPhase.Closed, controller.Phase);
        await controller.OpenAsync();
        Assert.Equal(0, controller.TickedCount);

        using var empty = new ForeignHome();
        var none = Make(empty, out _);
        await none.OpenAsync();
        Assert.Empty(none.Groups);
        Assert.Equal(ImportPhase.Choosing, none.Phase);
    }
}
