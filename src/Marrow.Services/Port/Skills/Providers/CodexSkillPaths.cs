// PORT-ONLY: SK-11b / WU-7, skills-PLAN.md 2.1 "Listing generations": a path the listing probe reported is sent back as the same skill under the session's own generation; a user's own path is unchanged.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Providers;

public static class CodexSkillPaths
{
    // The same rule as GenerationListing.ForChat, over the listing's skills as they were when the probe ran, so it also works once the probe's lease is gone.
    public static string Rewrite(string listedPath, IReadOnlyList<PublishedSkill>? listed, Generation chat)
    {
        var skill = listed?.SingleOrDefault(s => StorePaths.Comparer.Equals(s.File, listedPath));
        if (skill is null) return listedPath;
        return chat.Skills.Single(s => s.Id == skill.Id).File;
    }
}
