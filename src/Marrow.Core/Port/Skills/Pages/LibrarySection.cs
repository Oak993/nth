// PORT-ONLY: first-release feature unit SK-15 (DL-17, OD-0099 Sections C); there is no Swift twin. The two Settings pages of the skills library.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.Pages;

/// <summary>Plugins and Skills are two pages of their own, Plugins directly above Skills (OD-0099 clarification, "Sections C"); neither shows the other's items.</summary>
public enum LibrarySection
{
    Plugins,
    Skills,
}

public static class LibrarySections
{
    /// <summary>The catalogue kind each page lists: plugins on Plugins, skill sets on Skills. Tools stay on the Tools page and package sets are never listed.</summary>
    public static ItemKind Kind(this LibrarySection section) => section == LibrarySection.Plugins ? ItemKind.Plugin : ItemKind.Skillset;

    public static string Title(this LibrarySection section) => section == LibrarySection.Plugins ? "Plugins" : "Skills";

    /// <summary>The page's plural noun in running text ("Download all plugins", "All skills are here").</summary>
    public static string Noun(this LibrarySection section) => section == LibrarySection.Plugins ? "plugins" : "skills";
}
