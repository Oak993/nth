using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.UI.Port;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.App.Port;

/// <summary>
/// PORT-ONLY (ED-NEW-perf-startup-structure-3, -5). Three read-only tables are built on first use, and the first use is on the UI thread before the first window:
/// the skills catalogue (1.2 MB through reflection, about 70 ms), the SF-symbol mapping (1.8 MB, about 25 ms and 8 MB of allocation, reached by the first icon of the cloaked
/// window build) and, when the user has picked a library wallpaper, the wallpaper catalogue (two JSON documents and a definition per wallpaper). Each one is a
/// <see cref="Lazy{T}"/> that publishes once, so reading it early on the pool costs the UI thread nothing: it finds the table made, or waits for the read that is
/// already running instead of starting its own. A failed read is kept by the Lazy and raised at the first use, where it always was. Each prime takes the runner as a
/// parameter (production: <see cref="Pool"/>) so a test sees what is requested and where it runs.
/// </summary>
internal static class StartupPrimes
{
    /// <summary>Runs the work on the thread pool.</summary>
    public static Func<Action, Task> Pool { get; } = work => Task.Run(work);

    /// <summary>The embedded skills catalogue, which the skills host takes (<see cref="SkillsCatalogueLoader.Embedded"/>).</summary>
    public static void PrimeSkillsCatalogue(Func<Action, Task> run) => run(() => _ = SkillsCatalogueLoader.Embedded);

    /// <summary>The SF-symbol mapping behind every icon.</summary>
    public static void PrimeSymbols(Func<Action, Task> run) => run(() => _ = SymbolCatalogue.SymbolNames);

    /// <summary>The wallpaper catalogue, only when the settings hold a library wallpaper.</summary>
    public static void PrimeWallpapers(ISettingsStore settings, Func<Action, Task> run)
    {
        if (WallpaperLibraryStore.HasSavedSelection(settings)) run(() => _ = WallpaperCatalogue.All);
    }
}
