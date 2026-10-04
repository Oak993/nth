// PORT-ONLY: SK-11f / WU-12 (skills-PLAN.md 2.2, Q17). The only door to the user's own skills folder, so a test can prove it is never opened while the switch is off.
namespace Marrow.Services.Port.Skills.Providers;

public interface IUserSkillFiles
{
    /// <summary>The folders directly under <paramref name="root"/> that hold a SKILL.md; empty when the root does not exist.</summary>
    IReadOnlyList<string> SkillFolders(string root);

    /// <summary>The text of a file inside a skill folder, or null when it is not a plain readable file there.</summary>
    string? ReadText(string file);
}
