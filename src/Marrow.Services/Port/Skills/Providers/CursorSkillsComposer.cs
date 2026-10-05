// PORT-ONLY: SK-17 Cursor follow-up. Same trusted composition, with Cursor's own filters and user/project roots.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;

namespace Marrow.Services.Port.Skills.Providers;

public static class CursorSkillsComposer
{
    public static Generation Prepare(SkillsHost host, string chat, string directory, string home, GenerationBudget budget) =>
        AntigravitySkillsComposer.PrepareForProvider(host, "cursor", chat, directory, home, budget);
}
