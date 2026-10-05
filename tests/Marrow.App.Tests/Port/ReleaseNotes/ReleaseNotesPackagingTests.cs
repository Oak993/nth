using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.ReleaseNotes;

[Trait("Decision", "ED-NEW-ue-6")]
[Trait("Feature", "UE-6")]
public sealed class ReleaseNotesPackagingTests
{
    private const string Valid = "## [1.7.0] - 2026-10-05\n\n### New features\n- Update notes accompany every version.\n";

    [Fact]
    [Trait("Spec", "UE-T60")]
    public void Missing_default_changelog_refuses_before_any_command_or_output()
    {
        using var rig = new ReleaseNotesScriptRig();
        Refuses(rig, "CHANGELOG", []);
    }

    [Fact]
    [Trait("Spec", "UE-T61")]
    public void Missing_explicit_notes_refuse_even_with_a_valid_default()
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Changelog, Valid);
        Refuses(rig, "not found", ["-ReleaseNotesFile", rig.Notes]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [Trait("Spec", "UE-T62")]
    public void Empty_notes_refuse(string notes)
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Notes, notes);
        Refuses(rig, "empty", ["-ReleaseNotesFile", rig.Notes]);
    }

    [Theory]
    [InlineData("## [1.7.1]\n### New features\n- Wrong version\n")]
    [InlineData("## [1.7.0.1]\n### Bug fixes\n- Prefix is not the exact version\n")]
    [InlineData("### New features\n- No version identity\n")]
    [Trait("Spec", "UE-T63")]
    public void Notes_for_a_different_or_unidentified_version_refuse(string notes)
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Notes, notes);
        Refuses(rig, "1.7.0", ["-ReleaseNotesFile", rig.Notes]);
    }

    [Theory]
    [InlineData("## [1.7.0]\nPlain summary only\n")]
    [InlineData("## [1.7.0]\n### Thanks\n- Someone\n")]
    [InlineData("## [1.7.0]\n### Miscellaneous\n- Unknown section\n")]
    [InlineData("## [1.7.0]\n### New features\n### Thanks\n- Not a feature\n")]
    [InlineData("## [1.7.0]\n### New features\n- **__**\n")]
    [Trait("Spec", "UE-T64")]
    public void Notes_without_a_populated_recognised_section_refuse(string notes)
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Notes, notes);
        Refuses(rig, "recognised", ["-ReleaseNotesFile", rig.Notes]);
    }

    [Theory]
    [InlineData("## [Unreleased]\n### Bug fixes\n- Only unreleased\n")]
    [InlineData("## [1.7.1]\n### Bug fixes\n- A later release\n")]
    [InlineData("## [1.7.0]\n### Thanks\n- No displayable notes\n\n## [1.6.0]\n### Bug fixes\n- An older release cannot license this one\n")]
    [InlineData("## [1.7.0]\nNo changes here\n## 1.8.0\n### Bug fixes\n- Changes in the wrong version\n")]
    [InlineData("## [1.7.0]\nNo changes here\n# [1.8.0]\n### Bug fixes\n- Changes in the wrong version\n")]
    [InlineData("# Changelog\n```markdown\n## [1.7.0]\n### Bug fixes\n- Example only\n```\n")]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("## [1.7.0]\n### Bug fixes\n- One\n## [1.7.0]\n### Bug fixes\n- Duplicate\n")]
    [Trait("Spec", "UE-T65")]
    public void Default_notes_must_exist_for_the_exact_version_and_not_borrow_another_section(string changelog)
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Changelog, changelog);
        Refuses(rig, "Release notes", []);
    }

    [Theory]
    [InlineData("## [1.7.0]\n### Bug fixes\n- One\n\n## [1.6.0]\n### Bug fixes\n- Another version\n")]
    [InlineData("## [1.7.0]\n### Bug fixes\n- One\n\n## [1.7.0]\n### Bug fixes\n- Duplicate\n")]
    [Trait("Spec", "UE-T66")]
    public void An_explicit_file_with_multiple_releases_refuses(string notes)
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Notes, notes);
        Refuses(rig, "version", ["-ReleaseNotesFile", rig.Notes]);
    }

    [Theory]
    [InlineData("New features")]
    [InlineData("Bug fixes")]
    [InlineData("Refinements")]
    [Trait("Spec", "UE-T67")]
    public void Valid_explicit_notes_pass_the_same_preflight_without_packaging(string section)
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Notes, $"## [1.7.0]\n## {section}\n- A change\n## Thanks\n- Contributor\n");
        var result = rig.Run("guard.ps1", "-ValidateReleaseNotesOnly", "-ReleaseNotesFile", rig.Notes);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1.7.0", result.Text, StringComparison.Ordinal);
        rig.AssertNoCommandsOrArtifacts();
    }

    [Fact]
    [Trait("Spec", "UE-T68")]
    public void Default_preflight_and_extraction_select_only_the_exact_release_preserving_its_text()
    {
        using var rig = new ReleaseNotesScriptRig();
        var selected = Valid.Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(rig.Changelog, "# Changelog\r\n\r\n## [Unreleased]\r\n### Bug fixes\r\n- Ignore unreleased\r\n\r\n## [1.7.1]\r\n### Bug fixes\r\n- Later\r\n\r\n" + selected + "## [1.6.0]\r\n### Bug fixes\r\n- Older\r\n");
        Assert.Equal(0, rig.Run("guard.ps1", "-ValidateReleaseNotesOnly").ExitCode);
        var extracted = rig.Run("scripts/extract-release-notes.ps1", "-Version", "1.7.0", "-ChangelogFile", rig.Changelog, "-OutputFile", rig.Output);
        Assert.Equal(0, extracted.ExitCode);
        Assert.Equal(selected, File.ReadAllText(rig.Output));
        rig.AssertNoCommandsOrArtifacts();
    }

    [Fact]
    [Trait("Spec", "UE-T69")]
    public void Failed_extraction_does_not_create_or_overwrite_output()
    {
        using var rig = new ReleaseNotesScriptRig();
        File.WriteAllText(rig.Changelog, "## [1.8.0]\n### Bug fixes\n- Wrong\n");
        File.WriteAllText(rig.Output, "keep this file");
        var result = rig.Run("scripts/extract-release-notes.ps1", "-Version", "1.7.0", "-ChangelogFile", rig.Changelog, "-OutputFile", rig.Output);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("keep this file", File.ReadAllText(rig.Output));
        rig.AssertNoCommandsOrArtifacts();
        File.Delete(rig.Output);
        Assert.NotEqual(0, rig.Run("scripts/extract-release-notes.ps1", "-Version", "1.7.0", "-ChangelogFile", rig.Changelog, "-OutputFile", rig.Output).ExitCode);
        Assert.False(File.Exists(rig.Output));
    }

    private static void Refuses(ReleaseNotesScriptRig rig, string message, string[] args)
    {
        var before = Directory.GetFiles(rig.Folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var result = rig.Run("guard.ps1", args);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(message, result.Text, StringComparison.OrdinalIgnoreCase);
        rig.AssertNoCommandsOrArtifacts();
        var after = Directory.GetFiles(rig.Folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
}
