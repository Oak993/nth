using Marrow.Core.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

// SK-18 (pre-merge review M1-M3): the import ends in a plain line when saving or scanning fails, a newer Marrow's library is not changed or crashed on, and opening the sheet again never touches an import that is running.
public sealed class ImportRobustnessTests
{
    private static ImportController Make(ForeignHome home, out ImportedSkillsLibrary library, Func<IReadOnlyList<string>, ForeignScan>? scan = null)
    {
        library = new ImportedSkillsLibrary(home.SkillsRoot, new ManualTimeProvider());
        return new ImportController(scan ?? (folders => new ForeignSkillScanner(home.Home, folders).Scan()), library);
    }

    private static async Task Within(Task task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(task, finished);
        await task;
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_save_that_fails_ends_the_import_with_the_could_not_save_line_and_never_hangs()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
        Directory.CreateDirectory(Path.Combine(home.SkillsRoot, "imported", "index.json")); // the index cannot be written over a folder
        var controller = Make(home, out _);
        await controller.OpenAsync();
        controller.ToggleGroup(ForeignApp.ClaudeCode);
        await Within(controller.ImportSelectedAsync());
        Assert.Equal(ImportPhase.Done, controller.Phase);
        Assert.Equal(ImportWords.CouldNotSave, controller.Summary);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_scan_that_fails_still_opens_the_sheet_and_says_what_could_not_be_read()
    {
        using var home = new ForeignHome();
        var controller = Make(home, out _, _ => throw new IOException("gone"));
        await Within(controller.OpenAsync());
        Assert.Equal(ImportPhase.Choosing, controller.Phase);
        Assert.Equal(1, controller.ProblemCount);
        Assert.Empty(controller.Groups);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Opening_again_while_an_import_waits_on_a_question_changes_nothing()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "alpha", "alpha", "Claude alpha");
        home.Skill(".codex/skills", "alpha", "alpha", "Codex alpha");
        var controller = Make(home, out var library);
        await controller.OpenAsync();
        controller.ToggleGroup(ForeignApp.ClaudeCode);
        controller.ToggleGroup(ForeignApp.Codex);
        var running = controller.ImportSelectedAsync();
        for (var i = 0; i < 500 && controller.Phase != ImportPhase.AskingDuplicate; i++) await Task.Delay(10);
        Assert.Equal(ImportPhase.AskingDuplicate, controller.Phase);
        await Within(controller.OpenAsync());
        Assert.Equal(ImportPhase.AskingDuplicate, controller.Phase);
        Assert.NotNull(controller.Question);
        Assert.Equal(2, controller.TickedCount);
        controller.AnswerDuplicate(DuplicateChoice.Skip, false);
        await Within(running);
        Assert.Equal(ImportPhase.Done, controller.Phase);
        Assert.Equal(ImportWords.Done(1, 0, 1, 0), controller.Summary);
        Assert.Single(library.List());
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_library_from_a_newer_marrow_is_shown_never_changed_and_never_throws_from_the_controller()
    {
        using var home = new ForeignHome();
        var folder = Path.Combine(home.SkillsRoot, "imported");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "index.json"), "{\"version\":2,\"skills\":[]}");
        var controller = Make(home, out var library);
        Assert.True(library.ReadOnly);
        Assert.True(controller.ReadOnly);
        controller.SetEnabled(Guid.NewGuid().ToString("N"), false);
        Assert.False(controller.Remove(Guid.NewGuid().ToString("N")));
        Assert.Equal("{\"version\":2,\"skills\":[]}", File.ReadAllText(Path.Combine(folder, "index.json")));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_sheet_status_line_keeps_its_two_sentences_apart()
    {
        Assert.EndsWith(".", ImportWords.FoundCount(7, 3), StringComparison.Ordinal);
    }
}
