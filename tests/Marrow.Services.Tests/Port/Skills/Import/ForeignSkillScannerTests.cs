using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.Import;

// SK-18 (OD-0118): the places other AI tools keep skills on this PC. Temp folders shaped like each tool's layout; no real home folder, no network, no CLI.
public sealed class ForeignSkillScannerTests
{
    private static ForeignSkill Only(ForeignScan scan, string name) => Assert.Single(scan.Skills, s => s.Name == name);

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void An_empty_home_finds_nothing_and_reports_no_problem()
    {
        using var home = new ForeignHome();
        var scan = new ForeignSkillScanner(home.Home).Scan();
        Assert.Empty(scan.Skills);
        Assert.Empty(scan.Problems);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Claude_Code_skills_are_read_with_their_name_and_one_line_description()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".claude/skills", "pdf-tools", "pdf-tools", "Work with PDF files.", ("references/guide.md", "G"));
        var skill = Only(new ForeignSkillScanner(home.Home).Scan(), "pdf-tools");
        Assert.Equal((ForeignApp.ClaudeCode, "Skills", "Work with PDF files.", ForeignLayout.SkillFolder), (skill.App, skill.Source, skill.Description, skill.Layout));
        Assert.Equal(folder, skill.Path);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Claude_Code_plugin_skills_are_found_under_the_plugin_that_owns_them_and_only_the_newest_version_is_listed()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/plugins/cache/market/superpower/1.0.0/skills", "brainstorm", "brainstorm", "Old words.");
        var newest = home.Skill(".claude/plugins/cache/market/superpower/1.2.0/skills", "brainstorm", "brainstorm", "New words.");
        File.SetLastWriteTimeUtc(Path.Combine(home.Home, ".claude/plugins/cache/market/superpower/1.0.0/skills/brainstorm/SKILL.md"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(newest, "SKILL.md"), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        home.Skill(".claude/plugins/marketplaces/market/plugins/review/skills", "review-code", "review-code", "Review code.");
        var scan = new ForeignSkillScanner(home.Home).Scan();
        var brainstorm = Only(scan, "brainstorm");
        Assert.Equal(("superpower", "New words.", newest), (brainstorm.Source, brainstorm.Description, brainstorm.Path));
        Assert.Equal("review", Only(scan, "review-code").Source);
        Assert.All(scan.Skills, s => Assert.Equal(ForeignApp.ClaudeCode, s.App));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Codex_skills_come_from_its_own_folder_and_the_shared_agents_folder_and_not_from_hidden_system_ones()
    {
        using var home = new ForeignHome();
        home.Skill(".codex/skills", "plan", "plan", "Plan the work.");
        home.Skill(".codex/skills/.system", "imagegen", "imagegen", "Bundled.");
        home.Skill(".agents/skills", "shared-one", "shared-one", "Shared by tools.");
        var scan = new ForeignSkillScanner(home.Home).Scan();
        Assert.Equal(["plan", "shared-one"], scan.Skills.Select(s => s.Name).Order());
        Assert.All(scan.Skills, s => Assert.Equal(ForeignApp.Codex, s.App));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Antigravity_skills_are_found_in_each_of_its_three_folders()
    {
        using var home = new ForeignHome();
        home.Skill(".gemini/antigravity-cli/skills", "one", "one", "First.");
        home.Skill(".gemini/config/skills", "two", "two", "Second.");
        home.Skill(".gemini/skills", "three", "three", "Third.");
        var scan = new ForeignSkillScanner(home.Home).Scan();
        Assert.Equal(["one", "three", "two"], scan.Skills.Select(s => s.Name).Order());
        Assert.All(scan.Skills, s => Assert.Equal(ForeignApp.Antigravity, s.App));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Cursor_skills_and_rules_are_listed_a_rule_by_its_description_or_first_line()
    {
        using var home = new ForeignHome();
        home.Skill(".cursor/skills", "design", "design", "Design things.");
        home.Raw(".cursor/rules/style.mdc", "---\ndescription: House style for code\nglobs: \"**/*.cs\"\nalwaysApply: false\n---\nUse tabs.\n");
        home.Raw(".cursor/rules/plain.md", "Prefer small functions.\nSecond line.\n");
        home.Raw(".cursor/rules/empty.mdc", "");
        var scan = new ForeignSkillScanner(home.Home).Scan();
        Assert.Equal(("Design things.", ForeignLayout.SkillFolder), (Only(scan, "design").Description, Only(scan, "design").Layout));
        var style = Only(scan, "style");
        Assert.Equal(("House style for code", ForeignLayout.CursorRule, "Rules"), (style.Description, style.Layout, style.Source));
        Assert.Equal("Prefer small functions.", Only(scan, "plain").Description);
        Assert.DoesNotContain(scan.Skills, s => s.Name == "empty");
        Assert.Contains(scan.Problems, p => p.Path.EndsWith("empty.mdc", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_folder_the_user_picks_is_read_as_one_skill_or_as_a_folder_of_skills()
    {
        using var home = new ForeignHome();
        var single = Path.Combine(home.Root, "picked-single");
        Directory.CreateDirectory(single);
        File.WriteAllText(Path.Combine(single, "SKILL.md"), ForeignHome.SkillText("lone", "A lone skill."));
        var many = Path.Combine(home.Root, "picked-many");
        foreach (var n in new[] { "a-one", "b-two" })
        {
            Directory.CreateDirectory(Path.Combine(many, n));
            File.WriteAllText(Path.Combine(many, n, "SKILL.md"), ForeignHome.SkillText(n, "Skill " + n));
        }
        var scan = new ForeignSkillScanner(home.Home, [single, many]).Scan();
        Assert.Equal(["a-one", "b-two", "lone"], scan.Skills.Select(s => s.Name).Order());
        Assert.All(scan.Skills, s => Assert.Equal(ForeignApp.Folder, s.App));
        Assert.Equal("picked-single", Only(scan, "lone").Source);
        Assert.Equal("picked-many", Only(scan, "a-one").Source);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_picked_folder_that_is_missing_or_relative_is_a_problem_not_an_error()
    {
        using var home = new ForeignHome();
        var scan = new ForeignSkillScanner(home.Home, [Path.Combine(home.Root, "nowhere"), "relative/path"]).Scan();
        Assert.Empty(scan.Skills);
        Assert.Equal(2, scan.Problems.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Broken_files_are_reported_and_left_out_and_never_stop_the_good_ones()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "good", "good", "Good skill.");
        home.Raw(".claude/skills/unclosed/SKILL.md", "---\nname: unclosed\ndescription: no end\nbody");
        home.Raw(".claude/skills/binary/SKILL.md", "\u0000\u0001\u0002 not text");
        home.Raw(".claude/skills/empty-file/SKILL.md", "");
        home.Raw(".claude/skills/no-description/SKILL.md", "---\nname: no-description\n---\nbody");
        Directory.CreateDirectory(Path.Combine(home.Home, ".claude/skills/no-skill-file"));
        home.Raw(".claude/skills/Folder Name/SKILL.md", "---\nname: Not A Valid Name\ndescription: Falls back to nothing valid\n---\n");
        home.Raw(".claude/skills/fallback-folder/SKILL.md", "---\nname: Not Valid!\ndescription: Uses the folder name.\n---\n");
        var scan = new ForeignSkillScanner(home.Home).Scan();
        Assert.Equal(["fallback-folder", "good"], scan.Skills.Select(s => s.Name).Order());
        Assert.Equal(5, scan.Problems.Count);
        Assert.All(scan.Problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Reason)));
        Assert.All(scan.Problems, p => Assert.StartsWith(home.Home, p.Path, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void Scanning_writes_nothing_into_the_other_apps_folders()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills", "a", "a", "A.", ("scripts/run.sh", "echo"));
        home.Skill(".codex/skills", "b", "b", "B.");
        home.Skill(".gemini/skills", "c", "c", "C.");
        home.Raw(".cursor/rules/r.mdc", "---\ndescription: R\n---\nbody");
        home.Raw(".claude/skills/broken/SKILL.md", "---\nunclosed");
        var before = ForeignHome.Fingerprint(home.Home);
        var scanner = new ForeignSkillScanner(home.Home);
        scanner.Scan();
        scanner.Scan();
        Assert.Equal(before, ForeignHome.Fingerprint(home.Home));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void A_link_in_a_skills_folder_is_not_followed_and_is_reported()
    {
        using var home = new ForeignHome();
        var outside = Path.Combine(home.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "SKILL.md"), ForeignHome.SkillText("outside", "Out there."));
        Directory.CreateDirectory(Path.Combine(home.Home, ".claude/skills"));
        var link = Path.Combine(home.Home, ".claude/skills/linked");
        Directory.CreateSymbolicLink(link, outside);
        Assert.True(Directory.Exists(link));
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        Assert.Equal(ForeignHome.SkillText("outside", "Out there."), File.ReadAllText(Path.Combine(link, "SKILL.md")));
        var scan = new ForeignSkillScanner(home.Home).Scan();
        Assert.Empty(scan.Skills);
        Assert.Contains(scan.Problems, p => p.Path.EndsWith("linked", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_scan_is_bounded_by_skill_count_depth_and_file_size()
    {
        using var home = new ForeignHome();
        for (var i = 0; i < 6; i++) home.Skill(".claude/skills", "s" + i, "s" + i, "Skill " + i);
        var limited = new ForeignSkillScanner(home.Home, null, new ScanLimits(MaxSkills: 4)).Scan();
        Assert.Equal(4, limited.Skills.Count);
        Assert.Contains(limited.Problems, p => p.Reason.Contains("limit", StringComparison.OrdinalIgnoreCase));

        home.Skill(".claude/plugins/cache/a/b/c/d/e/f/g/h/i/skills", "too-deep", "too-deep", "Deep.");
        Assert.DoesNotContain(new ForeignSkillScanner(home.Home).Scan().Skills, s => s.Name == "too-deep");

        home.Raw(".codex/skills/huge/SKILL.md", ForeignHome.SkillText("huge", "Huge.", new string('x', 400 * 1024)));
        var big = new ForeignSkillScanner(home.Home).Scan();
        Assert.DoesNotContain(big.Skills, s => s.Name == "huge");
        Assert.Contains(big.Problems, p => p.Path.Contains("huge", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_same_skill_found_twice_is_listed_once_under_its_app_and_the_order_is_by_app_then_name()
    {
        using var home = new ForeignHome();
        home.Skill(".codex/skills", "zeta", "zeta", "Z.");
        home.Skill(".claude/skills", "beta", "beta", "B.");
        home.Skill(".claude/skills", "alpha", "alpha", "A.");
        var picked = Path.Combine(home.Home, ".claude", "skills");
        var scan = new ForeignSkillScanner(home.Home, [picked]).Scan();
        Assert.Equal(["alpha", "beta", "zeta"], scan.Skills.Where(s => s.App != ForeignApp.Folder).Select(s => s.Name));
        // A picked folder an app's own place already covers adds nothing: the app that owns the place keeps the skill.
        Assert.DoesNotContain(scan.Skills, s => s.App == ForeignApp.Folder);
        Assert.Equal(3, scan.Skills.Count);
    }

    [Fact]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public void The_scanner_needs_an_absolute_home_and_never_looks_anywhere_else()
    {
        Assert.Throws<ArgumentException>(() => new ForeignSkillScanner("relative/home"));
        using var home = new ForeignHome();
        var elsewhere = Path.Combine(home.Root, "other-home");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "SKILL.md"), ForeignHome.SkillText("never", "Never found."));
        Assert.Empty(new ForeignSkillScanner(home.Home).Scan().Skills);
    }
}
