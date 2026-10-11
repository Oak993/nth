// PORT-ONLY: SK-13 / ED-326 (6)(a), ED-325 "Own the whole root". There is no Swift twin.
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Host;

/// <summary>
/// The composition of the skills and tools machinery for ONE data root: a single <see cref="SkillsPipeline"/> that is the root's only owner (leases, GC, update, repair, remove, reset), SK-9's
/// <see cref="SkillGenerations"/> bound to it through <see cref="IGenerationStore"/> (a second owner on the root would delete the other's trees: ED-326 (1)), and the terms step over the
/// pipeline's state. At most one host is open per root in a process: a second <see cref="Open"/> of the same root is refused. Opening settles the root as a restart does: trusted descriptors are
/// rebuilt (<see cref="CatalogueTrust"/>), records without one are dropped and reported (<see cref="Unrestored"/>), and the GC runs once for crash residue and a cut-short reset.
/// </summary>
public sealed class SkillsHost : IDisposable
{
    private static readonly Logger Log = new("skills", "host");
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SkillsHost> OpenHosts = new(StorePaths.Comparer);
    private readonly string key;
    private bool disposed;
    private Task? collection;
    private readonly object shimGate = new();
    private string? shimLauncher;

    private SkillsHost(string key, StorePaths paths, SkillsPipeline pipeline, SkillGenerations generations, ImportedSkillsLibrary imported, CustomSources custom, TermsStep terms, SkillsDownloads fetches, SkillsCatalogue catalogue,
        Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>>? downloads, IReadOnlyList<string> unrestored)
    {
        this.key = key;
        Paths = paths;
        ProviderSkills = new(() => LoginEnvironment.HomeDirectory, paths.Root);
        Pipeline = pipeline;
        Generations = generations;
        Imported = imported;
        CustomSources = custom;
        Terms = terms;
        Downloads = fetches;
        Catalogue = catalogue;
        GuardedDownloads = downloads;
        Unrestored = unrestored;
        pipeline.Changed += SyncShims;
    }

    public StorePaths Paths { get; }

    public Marrow.Services.Port.Skills.Pages.ProviderSkillReports ProviderSkills { get; }

    /// <summary>SK-14: the catalogue the host was opened with: the in-chat offers read it.</summary>
    public SkillsCatalogue Catalogue { get; }

    /// <summary>The root's one owner.</summary>
    public SkillsPipeline Pipeline { get; }

    /// <summary>The per-chat generations, bound to <see cref="Pipeline"/>.</summary>
    public SkillGenerations Generations { get; }

    /// <summary>SK-18 (OD-0118): the skills the user brought in from other apps. Their files live beside, not inside, the managed trees, so Reset skills and tools leaves them; the generations hand them out.</summary>
    public ImportedSkillsLibrary Imported { get; }

    /// <summary>OD-0099 / RR-18: explicit custom additions, with their own registry and provenance.</summary>
    public CustomSources CustomSources { get; }

    public TermsStep Terms { get; }

    /// <summary>
    /// SK-16 (DL-19): the one place a first download is started from, so Settings and the in-chat offers share ONE download per item (an offer asked for an item Settings is fetching joins it, and the
    /// reverse). Fetching is available only when the options bound the download and binding seams (<see cref="SkillsDownloads.Available"/>).
    /// </summary>
    public SkillsDownloads Downloads { get; }

    /// <summary>SK-16: the in-chat offers' download seam over the same <see cref="Downloads"/>: an offer joins a download Settings has running, and the reverse.</summary>
    public ISkillsOfferDownload OfferDownloads => new SkillsOfferInstalls(Downloads);

    /// <summary>SK-14: the in-chat offers' terms step: the same one the Settings pages use, so an acceptance is one record.</summary>
    public ISkillsOfferTerms OfferTerms => new HostOfferTerms(Terms);

    /// <summary>
    /// The download seam with the terms checked first (<see cref="TermsGate"/>): the one entry a first install goes through so no byte of a non-open item is requested before its terms are
    /// accepted. Null while no download seam is bound. The pipeline's own update and repair use this same guarded seam.
    /// </summary>
    public Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>>? GuardedDownloads { get; }

    /// <summary>Installed records no trusted descriptor could be rebuilt for at open; they were dropped from state.json and their trees are left alone.</summary>
    public IReadOnlyList<string> Unrestored { get; }

    public static SkillsHost? Find(string dataRoot)
    {
        var root = Normalize(dataRoot);
        lock (Gate) return OpenHosts.GetValueOrDefault(root);
    }

    public static SkillsHost Open(string dataRoot, SkillsHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var paths = new StorePaths(dataRoot);
        lock (Gate)
        {
            if (OpenHosts.ContainsKey(paths.Root)) throw new InvalidOperationException("The data root already has an open skills host: one pipeline owns a root.");
            var host = Build(paths, options);
            OpenHosts.Add(paths.Root, host);
            return host;
        }
    }

    private static SkillsHost Build(StorePaths paths, SkillsHostOptions options)
    {
        Directory.CreateDirectory(paths.Addons);
        var state = AddonsStateStore.Load(paths.StateFile);
        var trust = (options.Trust ?? CatalogueTrust.Restore)(paths, options.Catalogue, state);
        if (trust.Unrestored.Count > 0)
        {
            // The pipeline refuses an installed record without a trusted descriptor, and a record is never trusted without one: it is dropped, loudly. Its trees are NOT: they are marked as orphans
            // (addons/orphans.json) before the record goes, and every GC of every launch retains them until the item is removed or Reset runs.
            SkillsPipeline.MarkOrphans(paths, state.Items.Where(r => trust.Unrestored.Contains(r.Id)).Select(r => new PipelineOrphan(r.Id, CatalogueTrust.OrphanFolders(paths, r))));
            var kept = AddonsState.Restore(state.Items.Where(r => !trust.Unrestored.Contains(r.Id)), state.AcceptedTerms, state.Declined, state.Found);
            AddonsStateStore.Save(paths.StateFile, kept);
            Log.Error("skills: no trusted descriptor for installed " + string.Join(", ", trust.Unrestored) + "; dropped from state.json, trees retained as orphans");
        }
        SkillsPipeline? pipeline = null;
        var guarded = options.Downloads is null ? null : TermsGate.Guard(() => pipeline!.State, options.Downloads);
        pipeline = new SkillsPipeline(paths, options.Catalogue, trust.Versions, AddonsState.Empty, new ItemInstaller(options.InstallLock, options.InstallProcess), options.Protection,
            options.Deletion, guarded, options.Bindings, trustedDirectories: trust.Directories);
        var imported = new ImportedSkillsLibrary(paths.Skills, options.Clock ?? TimeProvider.System);
        var custom = new CustomSources(Path.Combine(paths.Skills, "own"), options.CustomFiles ?? new DiskCustomSourceFiles(), options.CustomGitHub ?? new PublicGitHubSource(), options.Clock ?? TimeProvider.System);
        var generations = new SkillGenerations(paths, pipeline, options.Protection, options.ClashSource, new CombinedOwnSkillSource(imported, custom), custom);
        var terms = new TermsStep(options.Catalogue, () => pipeline.State, (id, at) => pipeline.AcceptTerms(id, at), options.LicenceText, options.Clock);
        return new SkillsHost(paths.Root, paths, pipeline, generations, imported, custom, terms, new SkillsDownloads(pipeline, options.Catalogue, options.Installable), options.Catalogue, guarded, trust.Unrestored)
            { shimLauncher = options.ShimLauncher };
    }

    /// <summary>
    /// RR-14 (ONDEMAND-PLAN.md 3.6, ED-NEW-dl-10): <c>addons\bin</c> follows what the selected pins publish, after every install, update, repair, removal or reset (the pipeline's
    /// <see cref="SkillsPipeline.Changed"/>) and once per launch with the startup collection, off the launch path. A failure is logged, never thrown: the next change or launch tries again.
    /// </summary>
    public void SyncShims()
    {
        lock (shimGate)
        {
            try { if (PipelineShimSync.Sync(Paths, shimLauncher) is { } problem) Log.Error("skills: " + problem); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
            {
                Log.Error("skills: the shims could not be brought up to date: " + error.Message);
            }
        }
    }

    /// <summary>
    /// The startup GC (crash residue: tmp- trees, ended chats, spares; a reset a held handle cut short: ED-326 (6)(d)), run once on the pool, never on the launch path. The caller starts it after
    /// the main window shows. Orphans (<see cref="SkillsPipeline.Orphans"/>) are retained by it on every launch. A failure is logged, never thrown. The same task is returned on a second call.
    /// </summary>
    public Task CollectInBackground()
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return collection ??= Task.Run(() =>
            {
                try { Pipeline.Collect(); SyncShims(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
                {
                    Log.Error("skills: the startup collection failed: " + error.Message);
                }
            });
        }
    }

    /// <summary>Reset skills and tools (ONDEMAND-PLAN.md 3.8): removes everything under <c>addons\</c> and <c>skills\store</c> and the views, keeps <c>enabled.json</c> and the accepted terms.</summary>
    public Task ResetAsync(CancellationToken cancellation = default) => Pipeline.ResetAsync(cancellation);

    public void Dispose()
    {
        lock (Gate)
        {
            if (disposed) return;
            disposed = true;
            OpenHosts.Remove(key);
        }
        try { collection?.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { /* logged inside the task */ }
        Generations.Dispose();
    }

    private static string Normalize(string dataRoot) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
}
