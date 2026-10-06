// PORT-ONLY, first-release SA-1 shell, built by SK-15 (OD-0099 Sections C: its own page, directly below Plugins): the Skills Settings page. The body is shared with the Plugins page (Port/Skills/Pages/LibraryPage.cs):
// the library list and its detail (layout D), Download all with its confirmation, the per-skill switches, "Already available through your providers", the not-affiliated footer.
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Port.Settings;

/// <summary>The Skills page (OD-0099 Sections C: its own page, directly below Plugins).</summary>
internal sealed class SkillsSettingsPage : LibraryPage
{
    /// <summary>Built by SK-15: the page is listed in the sidebar and found by its search words.</summary>
    internal const bool IsReady = true;

    public SkillsSettingsPage() : base(LibrarySection.Skills)
    {
    }
}
