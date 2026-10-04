// PORT-ONLY: first-release feature unit SK-1. The skill name rule of the Agent Skills specification.
namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>
/// "1-64 lowercase letters, digits and single hyphens, and the name must equal the folder name" (skills-research-loading.md section 1, quoting the
/// Agent Skills specification, agentskills.io/specification, as OpenCode applies it); skills-PLAN.md 2.1 makes it a gate: every skill folder name equals
/// its frontmatter name. UNVERIFIED: no leading or trailing hyphen is the lead's reading of the specification (the site was not reachable from the build
/// machine); recorded as ED-294.
/// </summary>
public static class SkillName
{
    public const int MaxLength = 64;

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength) return false;
        if (name[0] == '-' || name[^1] == '-') return false;
        var previousHyphen = false;
        foreach (var c in name)
        {
            var hyphen = c == '-';
            if (hyphen && previousHyphen) return false;
            if (!hyphen && c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9')) return false;
            previousHyphen = hyphen;
        }
        return true;
    }
}
