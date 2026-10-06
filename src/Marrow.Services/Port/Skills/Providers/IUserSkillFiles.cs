// PORT-ONLY: SK-11f / WU-12 (skills-PLAN.md 2.2, Q17). The only door to the user's own skills folder, so a test can prove it is never opened while the switch is off.
namespace Marrow.Services.Port.Skills.Providers;

public interface IUserSkillFiles
{
    /// <summary>The folders directly under <paramref name="root"/> that hold a SKILL.md, in name order; empty when the root does not exist.</summary>
    IReadOnlyList<string> SkillFolders(string root);

    /// <summary>
    /// The text of <paramref name="file"/>, which must lie under <paramref name="root"/> and be reached through plain folders and a plain file; null when it is not
    /// (missing, a link, outside the root, or not text).
    /// </summary>
    string? ReadText(string root, string file);
}
