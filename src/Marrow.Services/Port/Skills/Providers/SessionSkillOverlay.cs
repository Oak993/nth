// PORT-ONLY: SK-17 / OD-0099 Q-SKILLS-8 A. Only live-proven provider layouts are eligible for automatic discovery.
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Providers;

public static class SessionSkillOverlay
{
    public static string RequireFolder(Generation generation, string provider)
    {
        if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(generation.Folder)), provider, StringComparison.Ordinal))
            throw new ArgumentException("The generation belongs to a different provider.", nameof(generation));
        if (!generation.Verify()) throw new IOException("The skill generation failed verification before launch.");
        return generation.Folder;
    }
}
