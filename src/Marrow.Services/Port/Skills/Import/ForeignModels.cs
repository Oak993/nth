// PORT-ONLY: first-release feature unit SK-18 (OD-0118). What the scanner finds in other AI tools' folders and what the library keeps of what was imported.
namespace Marrow.Services.Port.Skills.Import;

public enum ForeignApp
{
    ClaudeCode,
    Codex,
    Antigravity,
    Cursor,
    Folder,
}

/// <summary>A skill folder (SKILL.md and what sits beside it), or one Cursor rule file Marrow wraps as a skill.</summary>
public enum ForeignLayout
{
    SkillFolder,
    CursorRule,
}

public enum ImportMode
{
    /// <summary>Marrow keeps its own copy.</summary>
    Copy,

    /// <summary>Marrow reads the original each time it hands skills out.</summary>
    Link,
}

public enum DuplicateChoice
{
    KeepBoth,
    Replace,
    Skip,
}

/// <summary>One skill another app keeps. <see cref="Source"/> is where in that app ("Skills", a plugin's name, "Rules", or the folder the user chose); <see cref="Path"/> is the skill folder or the rule file.</summary>
public sealed record ForeignSkill(ForeignApp App, string Source, string Name, string Description, string Path, ForeignLayout Layout)
{
    public string Key => App + "|" + Path;
}

public sealed record ScanProblem(string Path, string Reason);

public sealed record ForeignScan(IReadOnlyList<ForeignSkill> Skills, IReadOnlyList<ScanProblem> Problems);

public sealed record ScanLimits(int MaxSkills = 1000, int MaxDepth = 8, long MaxSkillFileBytes = 256 * 1024);

public sealed record ImportedSkill(
    string Id,
    string Name,
    string OriginalName,
    string Description,
    ForeignApp App,
    string SourceLabel,
    ImportMode Mode,
    string SourcePath,
    ForeignLayout Layout,
    bool Enabled,
    DateTimeOffset ImportedAt,
    bool OriginalMissing);

public sealed record DuplicateQuestion(ForeignSkill Incoming, ImportedSkill Existing);

public sealed record ImportFailure(ForeignSkill Skill, string Reason);

public sealed record ImportResult(
    IReadOnlyList<ImportedSkill> Imported,
    IReadOnlyList<ImportedSkill> Replaced,
    IReadOnlyList<ForeignSkill> Skipped,
    IReadOnlyList<ImportFailure> Failed);
