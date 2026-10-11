// PORT-ONLY: first-release feature unit SK-15 (DL-17). The library pages' backend over the app's one SkillsHost (SK-13): the pipeline's state and operations, the terms step and enabled.json.
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Pages;

/// <summary>
/// The backend of the pages over a <see cref="SkillsHost"/>. State, update, repair, remove and reset are the pipeline's (SK-8); the terms are the terms step's (SK-13); the switches are
/// enabled.json through SK-9's <see cref="SkillSwitches"/>, which this page is the writer of (the generations only read what they are handed). First installs, the providers' reports and the
/// free space come through seams that fail closed until bound: <see cref="FailClosedFirstInstall"/>, <see cref="NoProviderSkills"/> and no free-space reading.
/// ENGINEERING DECISION: enabled.json written by the page carries the catalogue's read date as its version when the file has none (SK-9 refuses a generation without a reviewed catalogue release
/// version), and a file that does not parse is never replaced: it reads as empty and a change to a switch is refused.
/// </summary>
public sealed class HostLibraryBackend(SkillsHost host, SkillsCatalogue catalogue, IFirstInstall? installs = null, IProviderSkills? providers = null, Func<DriveSpace?>? free = null) : ISkillsLibraryBackend
{
    private static readonly Logger Log = new("skills", "pages");
    private readonly object gate = new();
    private SkillSwitches? switches;
    private bool unreadable;
    private readonly bool injected = installs is not null;

    /// <summary>The catalogue the host was opened with (the host does not expose it): the caller passes the same one.</summary>
    public SkillsCatalogue Catalogue { get; } = catalogue;

    public AddonsState State => host.Pipeline.State;

    /// <summary>The first install: through the host's shared downloads when its download and binding seams are bound (SK-16), otherwise the seam that fails closed.</summary>
    public IFirstInstall Installs { get; } = installs ?? (host.Downloads.Available ? new SkillsFirstInstall(host.Downloads) : new FailClosedFirstInstall());

    public bool FetchingAvailable => CanFetch;

    public string ManualFolder => ManualFiles.ManualFolder(host.Paths.Root);

    public int PlaceManualFiles(CatalogueItem item) => ManualFiles.PlaceFolder(host.Paths.Root, item);

    public void OpenManualPage(Uri page)
    {
        if (SwiftURL.Create(page.AbsoluteUri) is { } url) _ = Marrow.Services.Port.Workspace.Open.Current(url);
    }

    public void OpenManualFolder()
    {
        Directory.CreateDirectory(ManualFolder);
        _ = Marrow.Services.Port.Workspace.Open.Current(SwiftURL.Create(new Uri(ManualFolder).AbsoluteUri)!);
    }

    public FetchView? Fetching(string itemId) => host.Downloads.Running(itemId);

    public IDisposable? WatchFetching(Action changed) => host.Downloads.Watch(changed);

    public void CancelFetching(string itemId) => host.Downloads.Stop(itemId);

    public bool CanFetch => Installs.IsAvailable;

    /// <summary>
    /// ED-NEW-dlhonest-1: through the host's shared downloads, the item's own answer (<see cref="SkillsDownloads.CanFetch"/>: pinned, needs pinned, installable by the bound seam). A first install the caller
    /// injected answers for itself, so only <see cref="CanFetch"/> applies to it.
    /// </summary>
    public bool CanFetchItem(string itemId) =>
        CanFetch && (injected || Catalogue.Items.FirstOrDefault(i => i.Id == itemId) is { } item && host.Downloads.CanFetch(item));

    public ISwitchReader Switches => new SwitchTruth(Current());

    private SkillSwitches Current()
    {
        lock (gate)
        {
            if (switches is not null) return switches;
            try { switches = SkillSwitches.Load(host.Paths.EnabledFile); }
            catch (Exception error) when (error is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
            {
                Log.Error("skills: enabled.json is not readable, so every switch reads off and none is changed: " + error.Message);
                unreadable = true;
                switches = SkillSwitches.Empty;
            }
            return switches;
        }
    }

    public void SetSwitch(SwitchLine line, bool on)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (gate)
        {
            var current = Current();
            if (unreadable) throw new InvalidOperationException("enabled.json is not readable; it is not replaced.");
            var withVersion = string.IsNullOrWhiteSpace(current.Version) ? new SkillSwitches(Catalogue.ReadOn, current.Skills, current.Plugins) : current;
            var next = line.Kind == SwitchKind.Skill
                ? withVersion.WithSkill(new GenerationSkill(line.ItemId, line.Label.TrimStart('/'), line.Key[(line.ItemId.Length + 1)..], "", SkillGroup.Other, 0, false), on)
                : withVersion.WithPlugin(line.Key, on);
            next.Save(host.Paths.EnabledFile);
            switches = next;
        }
    }

    public TermsOffer? Terms(string itemId) => host.Terms.Offer(itemId);

    public Task<string> LicenceTextAsync(string itemId, CancellationToken cancellation) => host.Terms.LicenceTextAsync(itemId, cancellation);

    public void AcceptTerms(string itemId) => host.Terms.Accept(itemId, readControlChecked: true);

    public Task UpdateAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation) => host.Pipeline.UpdateAsync(itemId, progress, cancellation);

    public Task<bool> RepairAsync(string itemId, IProgress<double?> progress, CancellationToken cancellation) => host.Pipeline.RepairAsync(itemId, progress, cancellation);

    public Task RemoveAsync(string itemId, CancellationToken cancellation) => host.Pipeline.RemoveAsync(itemId, cancellation);

    public Task ResetAsync(CancellationToken cancellation) => host.ResetAsync(cancellation);

    public DriveSpace? Free() => free?.Invoke();

    public IReadOnlyList<ProviderSkillGroup> Providers() => (providers ?? host.ProviderSkills).Report();

    /// <summary>The volume of the data root: the drive's label and its free bytes, or null when the drive cannot be read (a network root, a missing drive).</summary>
    public static DriveSpace? DriveOf(string dataRoot)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dataRoot));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? new DriveSpace(root.TrimEnd('\\', '/'), drive.AvailableFreeSpace) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class SwitchTruth(SkillSwitches switches) : ISwitchReader
    {
        public bool Skill(string itemId, string path) => switches.Skills.TryGetValue(itemId + "/" + path, out var on) && on;

        public bool PluginFolder(string itemId, string folder) => switches.PluginEnabled(itemId, folder);
    }
}
