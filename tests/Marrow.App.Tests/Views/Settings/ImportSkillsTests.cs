using Avalonia.Controls;
using Marrow.App.Port.Skills.Import;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Import;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Tests.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Settings;

// SK-18 (OD-0118): Settings > Skills > "Import skills". The sheet is in the Skills page's own style (SK-15 components, plain words, OD-0076 transitions, Reduce Motion), grouped by app, with the port's tick boxes and Import all.
// Temp folders shaped like each app's layout; no real home folder, no network, no CLI.
[Collection("Headless")]
public sealed class ImportSkillsTests(HeadlessSession headless)
{
    private sealed class Rig : IDisposable
    {
        public Rig(Func<Task<string?>>? pick = null)
        {
            Home = new ForeignHome();
            Library = new ImportedSkillsLibrary(Home.SkillsRoot, new ManualTimeProvider());
            Controller = new ImportController(folders => new ForeignSkillScanner(Home.Home, folders).Scan(), Library, () => Model!.PickFolder?.Invoke() ?? Task.FromResult<string?>(null));
            Model = new ImportModel(Controller) { PickFolder = pick };
        }

        public ForeignHome Home { get; }
        public ImportedSkillsLibrary Library { get; }
        public ImportController Controller { get; }
        public ImportModel? Model { get; }

        public void Fill()
        {
            Home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
            Home.Skill(".claude/skills", "beta", "beta", "Beta skill.");
            Home.Skill(".codex/skills", "gamma", "gamma", "Gamma skill.");
            Home.Raw(".cursor/rules/delta.mdc", "---\ndescription: Delta rule\n---\nbody\n");
        }

        public void Dispose() => Home.Dispose();
    }

    private static void OpenSheet(LibraryPageHarness h)
    {
        Assert.True(h.Press("import-skills"));
        h.Until(() => h.Find<Button>("import-cancel").IsEffectivelyVisible);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_the_Skills_page_gets_the_action_and_only_when_imports_are_available() => LibraryPageHarness.Run(headless, LibrarySection.Skills, without =>
    {
        Assert.Null(without.TryFind("import-skills"));
        using var rig = new Rig();
        LibraryPageHarness.Run(headless, LibrarySection.Plugins, plugins => Assert.Null(plugins.TryFind("import-skills")), imports: rig.Model);
        LibraryPageHarness.Run(headless, LibrarySection.Skills, skills =>
        {
            var button = skills.Find<Button>("import-skills");
            Assert.Equal(ImportWords.Action, LibraryButtonsWords(button));
            Assert.Contains(ImportWords.ActionNote, skills.Words());
        }, imports: rig.Model);
    });

    private static string LibraryButtonsWords(Button button) => ((TextBlock)button.Content!).Text ?? "";

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_action_opens_a_sheet_that_groups_what_it_finds_by_app_with_nothing_ticked() => RunWithFiles((h, rig) =>
    {
        OpenSheet(h);
        var sheet = h.Find<Control>("import-sheet");
        Assert.Equal([ImportWords.ClaudeCode, ImportWords.Codex, ImportWords.Cursor], h.Words(sheet).Where(w => w is ImportWords.ClaudeCode or ImportWords.Codex or ImportWords.Cursor));
        foreach (var name in new[] { "alpha", "beta", "gamma", "delta" }) Assert.Contains(name, h.Words(sheet));
        Assert.Contains("Alpha skill.", h.Words(sheet));
        Assert.Contains("Delta rule", h.Words(sheet));
        Assert.Equal(0, rig.Controller.TickedCount);
        Assert.False(h.Find<Button>("import-selected").IsEffectivelyEnabled);
        Assert.True(h.Find<Button>("import-all").IsEffectivelyEnabled);
        Assert.Equal(ImportWords.Title, h.Words(sheet)[0]);
    });

    private void RunWithFiles(Action<LibraryPageHarness, Rig> check, bool motion = false, bool reduceMotion = false, Func<Task<string?>>? pick = null)
    {
        using var rig = new Rig(pick);
        rig.Fill();
        LibraryPageHarness.Run(headless, LibrarySection.Skills, h => check(h, rig), motion, reduceMotion, imports: rig.Model);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_port_tick_boxes_tick_one_skill_or_a_whole_app_and_the_button_counts_them() => RunWithFiles((h, rig) =>
    {
        OpenSheet(h);
        Assert.True(h.Press("import-tick-ClaudeCode-alpha"));
        Assert.Equal(1, rig.Controller.TickedCount);
        Assert.Equal(ImportWords.ImportSelected(1), LibraryButtonsWords(h.Find<Button>("import-selected")));
        Assert.True(h.Find<Button>("import-selected").IsEffectivelyEnabled);
        Assert.True(h.Press("import-tick-group-Codex"));
        Assert.Equal(2, rig.Controller.TickedCount);
        Assert.Equal(ImportWords.ImportSelected(2), LibraryButtonsWords(h.Find<Button>("import-selected")));
        Assert.True(h.Press("import-tick-ClaudeCode-alpha"));
        Assert.Equal(1, rig.Controller.TickedCount);
    });

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Import_brings_in_only_the_ticked_ones_as_a_copy_by_default_and_the_page_then_lists_them_with_a_quiet_tag() => RunWithFiles((h, rig) =>
    {
        OpenSheet(h);
        Assert.Equal(ImportMode.Copy, rig.Controller.Mode);
        Assert.True(h.Press("import-tick-ClaudeCode-alpha"));
        Assert.True(h.Press("import-selected"));
        h.Until(() => rig.Controller.Phase == ImportPhase.Done);
        Assert.Contains(ImportWords.Done(1, 0, 0, 0), h.Words());
        Assert.True(h.Press("import-close"));
        h.Until(() => rig.Controller.Phase == ImportPhase.Closed && h.TryFind("import-sheet") is null);
        Assert.Equal(["alpha"], rig.Library.List().Select(i => i.Name));
        Assert.All(rig.Library.List(), i => Assert.Equal(ImportMode.Copy, i.Mode));
        var words = h.Words();
        Assert.Contains(ImportWords.SectionHeading, words);
        Assert.Contains("alpha", words);
        Assert.Contains(ImportWords.From(ImportWords.ClaudeCode) + " · " + ImportWords.Copied, words);
    });

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Link_is_a_choice_of_its_own_with_its_own_plain_note_and_Import_all_takes_everything() => RunWithFiles((h, rig) =>
    {
        OpenSheet(h);
        Assert.Contains(ImportWords.CopyNote, h.Words());
        Assert.True(h.Press("import-link"));
        Assert.Equal(ImportMode.Link, rig.Controller.Mode);
        Assert.Contains(ImportWords.LinkNote, h.Words());
        Assert.DoesNotContain(ImportWords.CopyNote, h.Words());
        Assert.True(h.Press("import-all"));
        h.Until(() => rig.Controller.Phase == ImportPhase.Done);
        Assert.Equal(4, rig.Library.List().Count);
        Assert.All(rig.Library.List(), i => Assert.Equal(ImportMode.Link, i.Mode));
        Assert.True(h.Press("import-close"));
        h.Until(() => h.TryFind("import-sheet") is null);
        Assert.Contains(ImportWords.From(ImportWords.Cursor) + " · " + ImportWords.Linked, h.Words());
    });

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_duplicate_is_asked_in_the_sheet_with_keep_both_replace_and_skip_and_one_answer_for_the_rest()
    {
        using var rig = new Rig();
        foreach (var n in new[] { "a", "b" })
        {
            rig.Home.Skill(".claude/skills", n, n, "Claude " + n);
            rig.Home.Skill(".codex/skills", n, n, "Codex " + n);
        }
        LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
        {
            OpenSheet(h);
            Assert.True(h.Press("import-all"));
            h.Until(() => rig.Controller.Phase == ImportPhase.AskingDuplicate);
            Assert.Contains(ImportWords.Duplicate("a"), h.Words());
            foreach (var id in new[] { "import-keep-both", "import-replace", "import-skip" }) Assert.NotNull(h.TryFind(id));
            Assert.True(h.Press("import-rest"));
            Assert.True(h.Press("import-keep-both"));
            h.Until(() => rig.Controller.Phase == ImportPhase.Done);
            Assert.Equal(["a", "a-codex", "b", "b-codex"], rig.Library.List().Select(i => i.Name).Order(StringComparer.Ordinal));
        }, imports: rig.Model);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void An_imported_skill_has_a_switch_and_is_removed_in_two_presses_without_touching_the_original() => RunWithFiles((h, rig) =>
    {
        rig.Library.Import(new ForeignSkillScanner(rig.Home.Home).Scan().Skills.Where(s => s.Name == "alpha"), ImportMode.Copy, _ => DuplicateChoice.Skip);
        rig.Controller.Raise();
        h.Settle();
        var original = ForeignHome.Fingerprint(rig.Home.Home);
        var id = rig.Library.List().Single().Id;
        Assert.True(rig.Library.List().Single().Enabled);
        Assert.True(h.Press("imported-switch-" + id) || h.TryFind("imported-switch-" + id) is not null);
        rig.Controller.SetEnabled(id, false);
        h.Settle();
        Assert.False(rig.Library.List().Single().Enabled);
        Assert.True(h.Press("imported-remove-" + id));
        Assert.Contains(ImportWords.RemoveAsk, h.Words());
        Assert.Single(rig.Library.List());
        Assert.True(h.Press("imported-remove-confirm-" + id));
        h.Until(() => rig.Library.List().Count == 0);
        Assert.DoesNotContain("alpha", h.Words().Where(w => w == "alpha"));
        Assert.Equal(original, ForeignHome.Fingerprint(rig.Home.Home));
    });

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_remove_question_can_be_taken_back() => RunWithFiles((h, rig) =>
    {
        rig.Library.Import(new ForeignSkillScanner(rig.Home.Home).Scan().Skills.Take(1), ImportMode.Copy, _ => DuplicateChoice.Skip);
        rig.Controller.Raise();
        h.Settle();
        var id = rig.Library.List().Single().Id;
        Assert.True(h.Press("imported-remove-" + id));
        Assert.True(h.Press("imported-remove-cancel-" + id));
        Assert.Single(rig.Library.List());
        Assert.NotNull(h.TryFind("imported-remove-" + id));
    });

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Cancel_closes_the_sheet_and_forgets_what_was_ticked() => RunWithFiles((h, rig) =>
    {
        OpenSheet(h);
        Assert.True(h.Press("import-tick-ClaudeCode-alpha"));
        Assert.True(h.Press("import-cancel"));
        h.Until(() => h.TryFind("import-sheet") is null);
        Assert.Equal((ImportPhase.Closed, 0), (rig.Controller.Phase, rig.Controller.TickedCount));
        Assert.Empty(rig.Library.List());
    });

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Choose_a_folder_adds_that_folder_to_the_list_through_the_system_chooser()
    {
        using var rig = new Rig();
        var picked = Path.Combine(rig.Home.Root, "mine");
        Directory.CreateDirectory(Path.Combine(picked, "own-skill"));
        File.WriteAllText(Path.Combine(picked, "own-skill", "SKILL.md"), ForeignHome.SkillText("own-skill", "My own."));
        rig.Model!.PickFolder = () => Task.FromResult<string?>(picked);
        LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
        {
            OpenSheet(h);
            Assert.Contains(ImportWords.NothingFound, h.Words());
            Assert.True(h.Press("import-folder"));
            h.Until(() => rig.Controller.Groups.Count == 1);
            Assert.Contains(ImportWords.Folders, h.Words());
            Assert.Contains("own-skill", h.Words());
            Assert.DoesNotContain(ImportWords.NothingFound, h.Words());
        }, imports: rig.Model);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void What_could_not_be_read_is_said_once_in_plain_words() => RunWithFiles((h, rig) =>
    {
        rig.Home.Raw(".claude/skills/broken/SKILL.md", "---\nunclosed");
        OpenSheet(h);
        Assert.Contains(ImportWords.NotReadCount(1), h.Words());
    });

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: a tick eases in on the frame clock, never a cut
    public void A_tick_eases_in_over_several_frames() => RunWithFiles((h, rig) =>
    {
        OpenSheet(h);
        var tick = h.All().OfType<ImportTick>().First();
        Assert.Equal(0, tick.Drawn, 6);
        tick.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var samples = new List<double> { tick.Drawn };
        for (var spent = 0; spent < 260; spent += 16)
        {
            h.Step(16);
            samples.Add(tick.Drawn);
        }
        Assert.Equal(1, samples[^1], 6);
        Assert.True(samples.Distinct().Count() >= 5, "the tick was a cut");
        Assert.True(samples.Zip(samples.Skip(1), (a, b) => b - a).All(d => d >= -1e-9));
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // OD-0076: Reduce Motion lands every change at once
    public void Under_Reduce_Motion_a_tick_lands_at_once() => RunWithFiles((h, rig) =>
    {
        OpenSheet(h);
        var tick = h.All().OfType<ImportTick>().First();
        tick.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, tick.Drawn, 6);
    }, motion: true, reduceMotion: true);
}
