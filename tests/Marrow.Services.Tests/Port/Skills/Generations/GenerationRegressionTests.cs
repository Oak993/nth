// PORT-ONLY: SK-9 independent review regressions, reviewed fixtures only.
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Generations;

[Trait("Spec", "DL-9")]
public sealed class GenerationRegressionTests
{
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-5-YAML-inline-comment
    public void YAML_inline_comments_do_not_hide_user_skill_name_clashes()
    {
        var d = SkillFrontmatter.Disclosure("---\nname: pdf # user skill\ndescription: 'A # description' # comment\n---\nbody");
        Assert.Equal("pdf", d.Name);
        Assert.Equal("A # description", d.Description);
        Assert.True(GenerationFilters.Clashes("codex", "pdf", [d]));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-5-Grok-TOML-comments
    public void Commented_Grok_section_and_path_arrays_are_read_without_silent_omissions()
    {
        using var f = new GenerationFixture();
        var home = Path.Combine(f.Paths.Root, "home");
        var extra = Path.Combine(home, "extra");
        Directory.CreateDirectory(Path.Combine(home, ".grok"));
        Directory.CreateDirectory(Path.Combine(extra, "pdf"));
        File.WriteAllText(Path.Combine(extra, "pdf", "SKILL.md"), "---\nname: pdf\ndescription: Fixture\n---\nbody");
        File.WriteAllText(Path.Combine(home, ".grok", "config.toml"), "[skills] # comment\npaths = [\"" + extra.Replace('\\', '/') + "\"] # comment\n");
        Assert.Contains(new SkillClashScanner(home, f.Paths.Root, f.Paths.Root).Scan("grok"), s => s.Name == "pdf");
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-plugin-folder-switches
    public void Plugin_folders_in_one_set_have_independent_content_switches()
    {
        using var f = new GenerationFixture();
        var item = f.Add("O4", "pdf", plugin: true, secondName: "doc", nestedPlugins: true);
        var s = f.Switches((item, true)).WithSkill(item.Skills[1], false).WithPlugin("O4/first", true);
        s.Save(f.Paths.EnabledFile);
        var loaded = SkillSwitches.Load(f.Paths.EnabledFile).Reconcile("fixture", [item]);
        using var g = f.Open().Start("claude", "a", [item], loaded, f.Budget);
        Assert.EndsWith(Path.Combine("O4", "first"), Assert.Single(g.PluginFolders), StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.GetFiles(g.Folder, "*", SearchOption.AllDirectories), p => p.Contains(Path.Combine("O4", "second"), StringComparison.Ordinal));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-record-moved-path
    public void A_held_generation_move_is_recorded_and_reused_after_restart()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        var old = service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget);
        var original = old.Folder;
        f.Deletion.Held.Add(original);
        old.Dispose();
        var next = service.Start("codex", "one", [item], f.Switches((item, false)), f.Budget);
        var moved = next.Folder;
        Assert.NotEqual(original, moved);
        Assert.Equal(original, next.PreviousFolder);
        next.Dispose();
        service.Dispose();
        f.Deletion.Held.Clear();
        using var resumed = f.Open().Start("codex", "one", [item], f.Switches((item, false)), f.Budget);
        Assert.Equal(moved, resumed.Folder);
        Assert.Null(resumed.PreviousFolder);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-chat-path-confinement
    public void Planted_chat_path_metadata_is_not_authority_to_read_or_delete_elsewhere()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        Directory.CreateDirectory(f.Paths.Skills);
        File.WriteAllText(Path.Combine(f.Paths.Skills, "chat-paths.json"), "{\"codex/one\":\"../../outside\"}");
        Assert.Throws<InvalidDataException>(() => f.Open().Start("codex", "one", [item], f.Switches((item, true)), f.Budget));
    }
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-installed-during-chat
    public void A_set_installed_during_chat_is_absent_from_live_snapshot_and_present_on_resume()
    {
        using var f = new GenerationFixture();
        var firstItem = f.Add("A1", "pdf");
        var firstService = f.Open();
        var running = firstService.Start("codex", "one", [firstItem], f.Switches((firstItem, true)), f.Budget);
        var path = running.Folder;
        // Complete a second offline installation while the chat remains live: verified files and an Installed record, no provider process.
        var installedDuringChat = f.Add("O3", "doc");
        Assert.Equal("pdf", Assert.Single(running.Skills).Name);
        Assert.Throws<InvalidOperationException>(() => running.Invoke("doc"));
        running.Dispose();
        firstService.Dispose();
        // SK-13 refreshes the frozen installed-descriptor inventory at the owner's lifecycle boundary before resume.
        using var resumed = f.Open().Start("codex", "one", [firstItem, installedDuringChat],
            f.Switches((firstItem, true), (installedDuringChat, true)), f.Budget);
        Assert.Equal(path, resumed.Folder);
        Assert.Equal(new[] { "doc", "pdf" }, resumed.Skills.Select(x => x.Name).Order(StringComparer.Ordinal));
    }
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-plugin-switch-after-reconcile
    public void Item_activation_after_reconcile_clears_old_folder_overrides()
    {
        using var f = new GenerationFixture();
        var item = f.Add("O4", "pdf", plugin: true);
        var switches = SkillSwitches.Empty.Reconcile("fixture", [item]).WithPlugin("O4", true);
        switches.Save(f.Paths.EnabledFile);
        using var g = f.Open().Start("claude", "one", [item], SkillSwitches.Load(f.Paths.EnabledFile), f.Budget);
        Assert.Single(g.PluginFolders);
        Assert.False(switches.WithPlugin("O4", false).PluginEnabled("O4", ""));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-failed-publication-no-lease-leak
    public void Path_record_failure_disposes_the_unreturned_generation_lease()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        var locks = 0;
        f.Fake.BeforeLock = path =>
        {
            if (Path.GetFileName(path) == "chat-one" && ++locks == 2)
                File.WriteAllText(Path.Combine(f.Paths.Skills, "chat-paths.json"), "null");
        };
        Assert.Throws<InvalidDataException>(() => service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget));
        f.Fake.BeforeLock = null;
        f.Store!.Collect();
        Assert.False(Directory.Exists(Path.Combine(f.Paths.Views, "codex", "chat-one")));
    }
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-spare-miss-reason
    public void Missing_spare_is_logged_with_reason_without_skill_body_text()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        using var g = service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget);
        Assert.Equal(GenerationLaunchReason.MissingSpare, service.LastLaunch!.Reason);
        var log = File.ReadAllText(Path.Combine(f.Paths.Skills, "generation-launches.jsonl"));
        Assert.Contains("MissingSpare", log, StringComparison.Ordinal);
        Assert.DoesNotContain("BODY SENTINEL", log, StringComparison.Ordinal);
    }
}
