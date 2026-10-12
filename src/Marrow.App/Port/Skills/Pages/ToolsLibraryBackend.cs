// PORT-ONLY RR-18: UI adapter for the optional tools library. Keeps Core/Services untouched for codex/skills-backend.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>
/// Adapts only the presentation catalogue to the existing list/run controller. Every operation reaches the backend with its authentic original catalogue item (including Tool/PackageSet kind).
/// The controller still owns consent, terms, cancellation, sequential installs and retries; this layer never runs a process or downloads a byte.
/// </summary>
internal sealed class ToolsLibraryBackend : ISkillsLibraryBackend
{
    private readonly ISkillsLibraryBackend backend;
    private readonly IFirstInstall installs;
    public ToolsLibraryBackend(ISkillsLibraryBackend backend)
    {
        this.backend = backend;
        Catalogue = backend.Catalogue with { Items = backend.Catalogue.Items.Where(i => i.Kind is ItemKind.Tool or ItemKind.Pyset or ItemKind.Nodeset).Select(i => i with { Kind = ItemKind.Skillset, Skills = [], Folders = [], SkillsListed = false }).ToList() };
        installs = new OriginalInstall(backend);
    }
    private sealed class OriginalInstall(ISkillsLibraryBackend backend) : IFirstInstall
    {
        public Task InstallAsync(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation) => backend.Installs.InstallAsync(backend.Catalogue.Items.Single(i => i.Id == item.Id), progress, cancellation);
    }
    public bool CanFetch => backend.CanFetch;
    public bool CanFetchItem(string itemId) => backend.CanFetchItem(itemId); // the authentic item (a tool, not the presentation's skill set)
    public ManualFallback? ManualFallbackOf(string itemId) => backend.ManualFallbackOf(itemId); // ED-NEW-dlhonest-1: a tool's fallback names its file, never "the repository at commit"
    public SkillsCatalogue Catalogue { get; }
    public AddonsState State => backend.State;
    public ISwitchReader Switches => backend.Switches;
    public IFirstInstall Installs => installs;
    public void SetSwitch(SwitchLine line, bool on) => backend.SetSwitch(line, on);
    public TermsOffer? Terms(string itemId) => backend.Terms(itemId);
    public Task<string> LicenceTextAsync(string itemId, CancellationToken cancellation) => backend.LicenceTextAsync(itemId, cancellation);
    public void AcceptTerms(string itemId) => backend.AcceptTerms(itemId);
    public FetchView? Fetching(string itemId) => backend.Fetching(itemId);
    public IDisposable? WatchFetching(Action changed) => backend.WatchFetching(changed);
    public string ManualFolder => backend.ManualFolder;
    public int PlaceManualFiles(CatalogueItem item) => backend.PlaceManualFiles(backend.Catalogue.Items.Single(i => i.Id == item.Id));
    public void OpenManualPage(Uri page) => backend.OpenManualPage(page);
    public void OpenManualFolder() => backend.OpenManualFolder();
    public void CancelFetching(string itemId) => backend.CancelFetching(itemId);
    public Task UpdateAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation) => backend.UpdateAsync(itemId, progress, cancellation);
    public Task<bool> RepairAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation) => backend.RepairAsync(itemId, progress, cancellation);
    public Task RemoveAsync(string itemId, CancellationToken cancellation) => backend.RemoveAsync(itemId, cancellation);
    public Task ResetAsync(CancellationToken cancellation) => backend.ResetAsync(cancellation);
    public DriveSpace? Free() => backend.Free();
    public IReadOnlyList<ProviderSkillGroup> Providers() => [];
}
