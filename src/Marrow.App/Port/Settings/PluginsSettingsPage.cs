// PORT-ONLY, first-release SA-1 shell, built by SK-15 (OD-0099 Sections C: its own page, directly above Skills): the Plugins Settings page. The body is shared with the Skills page (Port/Skills/Pages/LibraryPage.cs):
// the library list and its detail (layout D), Download all with its confirmation, the per-folder switches, the not-affiliated footer.
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Port.Settings;

/// <summary>The Plugins page (OD-0099 Sections C: its own page, directly above Skills).</summary>
internal sealed class PluginsSettingsPage : LibraryPage
{
    /// <summary>Built by SK-15: the page is listed in the sidebar and found by its search words.</summary>
    internal const bool IsReady = true;

    public PluginsSettingsPage() : base(LibrarySection.Plugins)
    {
    }
}
