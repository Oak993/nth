// PORT-ONLY: WU-5 read-only folder fixtures, always inside MARROW_PORT_DATA_DIR.
using System.Security.Cryptography;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Generations;

[Trait("Feature", "SK-9")]
[Trait("Spec", "DL-9")]
public sealed class ClashScannerTests
{
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-5-user-project-scans
    public void Provider_specific_user_and_project_folders_are_scanned_without_writes()
    {
        using var f = new GenerationFixture();
        var home = Path.Combine(f.Paths.Root, "home");
        var repo = Path.Combine(f.Paths.Root, "repo");
        var project = Path.Combine(repo, "nested");
        Write(home, ".agents/skills/pdf", "PDF");
        Write(home, ".codex/skills/.system/skill-creator", "skill-creator");
        Write(home, ".claude/skills/synced/x/docx", "docx");
        Write(repo, ".agents/skills/spreadsheet", "spreadsheet");
        Directory.CreateDirectory(project);
        var before = Hash(home);
        var scanner = new SkillClashScanner(home, project, repo);
        var codex = scanner.Scan("codex");
        Assert.Contains(codex, x => x.Name == "PDF");
        Assert.Contains(codex, x => x.Name == "skill-creator");
        Assert.Contains(codex, x => x.Name == "spreadsheet");
        Assert.Contains(scanner.Scan("claude"), x => x.Name == "docx");
        Assert.Equal(before, Hash(home));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-5-Grok-own-roots
    public void Grok_scans_its_own_configured_and_Claude_roots_without_leaking_them_to_Claude()
    {
        using var f = new GenerationFixture();
        var home = Path.Combine(f.Paths.Root, "home");
        var project = Path.Combine(f.Paths.Root, "project");
        Directory.CreateDirectory(project);
        Write(home, ".grok/skills/pdf", "pdf");
        Write(home, ".claude/skills/docx", "docx");
        var extra = Path.Combine(home, "extra");
        Write(extra, "spreadsheet", "spreadsheet");
        File.WriteAllText(Path.Combine(home, ".grok", "config.toml"), "[skills]\npaths = [\"" + extra.Replace('\\', '/') + "\"]\n");
        var scanner = new SkillClashScanner(home, project, project);
        var grok = scanner.Scan("grok");
        Assert.Contains(grok, x => x.Name == "pdf");
        Assert.Contains(grok, x => x.Name == "docx");
        Assert.Contains(grok, x => x.Name == "spreadsheet");
        Assert.DoesNotContain(scanner.Scan("claude"), x => x.Name is "pdf" or "spreadsheet");
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-5-OpenCode-relative-global
    public void OpenCode_effective_paths_resolve_against_session_and_only_forward_tilde_expands()
    {
        using var f = new GenerationFixture();
        var home = Path.Combine(f.Paths.Root, "home");
        var project = Path.Combine(f.Paths.Root, "project");
        Write(project, "agent-skills/pdf", "pdf");
        Write(home, "team-skills/doc", "doc");
        var scanner = new SkillClashScanner(home, project, project);
        var found = scanner.Scan("opencode", extraPaths: ["agent-skills", "~/team-skills"]);
        Assert.Contains(found, s => s.Name == "pdf");
        Assert.Contains(found, s => s.Name == "doc");
        Assert.Equal(Path.Combine(project, "~\\x"), scanner.ResolveExtra("~\\x"));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-12-own-skills-off
    public void Native_own_skills_switch_off_reads_nothing_from_home()
    {
        using var f = new GenerationFixture();
        var home = Path.Combine(f.Paths.Root, "never exists");
        var scanner = new SkillClashScanner(home, f.Paths.Root, f.Paths.Root);
        Assert.Empty(scanner.Scan("native"));
        Assert.False(Directory.Exists(home));
        Write(home, ".agents/skills/private", "private");
        Assert.Empty(scanner.Scan("native"));
        Assert.Equal("private", Assert.Single(scanner.Scan("native", ownSkillsForApi: true)).Name);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-5-fail-closed-metadata
    public void Malformed_disclosure_and_unresolved_config_paths_refuse_the_scan()
    {
        using var f = new GenerationFixture();
        var home = Path.Combine(f.Paths.Root, "home");
        Write(home, ".agents/skills/pdf", "pdf");
        File.WriteAllText(Path.Combine(home, ".agents", "skills", "pdf", "SKILL.md"), "unreadable metadata");
        Assert.Throws<InvalidDataException>(() => new SkillClashScanner(home, f.Paths.Root, f.Paths.Root).Scan("codex"));
        Assert.Throws<InvalidDataException>(() => new SkillClashScanner(home, f.Paths.Root, f.Paths.Root).Scan("opencode", extraPaths: ["{env:SKILLS}"]));
    }

    private static void Write(string root, string relative, string name)
    {
        var folder = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), $"---\nname: {name}\ndescription: User fixture\n---\nPrivate body\n");
    }
    private static string Hash(string root) => string.Join(',', Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
        .Select(p => p + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)))));
}
