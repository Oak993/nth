// PORT-ONLY: first-release feature unit SK-15 (DL-17). Where the Plugins and Skills pages get their controllers. A test overrides it; the app's default builds them over the one SkillsHost.
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>
/// One <see cref="LibraryModel"/> per page, kept for the life of the app: the Settings window builds a page again each time the sidebar comes back to it, and a Download all in flight (and its
/// dialog, hidden or not) must be the same one it left. <see cref="Make"/> null means the skills host could not be opened (SkillsStartup logged why): the pages then show no list.
/// </summary>
internal sealed class LibraryControllers(Func<LibrarySection, LibraryController>? make)
{
    private readonly Dictionary<LibrarySection, LibraryModel> models = [];

    public Func<LibrarySection, LibraryController>? Make { get; } = make;

    public bool IsAvailable => Make is not null;

    public LibraryModel? For(LibrarySection section)
    {
        if (Make is null) return null;
        lock (models)
        {
            if (!models.TryGetValue(section, out var model)) models[section] = model = new LibraryModel(Make(section));
            return model;
        }
    }
}

internal static class SkillsPageSeams
{
    private static readonly Func<LibraryControllers> Default = () =>
    {
        SkillsHost host;
        try { host = SkillsSeams.Host.Current; }
        catch (SwiftTrapException) { return new LibraryControllers(null); }
        var catalogue = SkillsCatalogueLoader.LoadEmbedded();
        var backend = new HostLibraryBackend(host, catalogue, free: () => HostLibraryBackend.DriveOf(host.Paths.Root));
        return new LibraryControllers(section => new LibraryController(section, backend, action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); }));
    };

    /// <summary>The controllers of the pages. The default is made on first use, once the skills host is bound (Program.Main binds it before the first window).</summary>
    public static Ambient<LibraryControllers> Controllers { get; } = new(Default);
}
