// PORT-ONLY: first-release feature unit SK-16 (DL-19). One skills host on a fresh data root (MARROW_PORT_DATA_DIR) with scripted download and binding seams: no network, no CLI, no installer.
using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

internal sealed class FetchRig : IDisposable
{
    private readonly Dictionary<string, (string Skill, string Licence)> inputs = [];
    private readonly Dictionary<string, TaskCompletionSource> entered = [];
    private readonly object gate = new();
    private SkillsHost? host;

    public FetchRig(bool bound = true)
    {
        Bound = bound;
        Root = SkillsHostRig.FreshRoot();
        Catalogue = SkillsHostRig.Catalogue(Make("S1", []), Make("S2", ["S1"]), Make("S3", []), Make("U1", [], pinned: false), Make("N1", [], terms: true));
    }

    public bool Bound { get; }

    public string Root { get; }

    public PipelineProtection Protection { get; } = new();

    public SkillsCatalogue Catalogue { get; }

    /// <summary>The ids the download seam was called for, in call order.</summary>
    public List<string> Downloaded { get; } = [];

    /// <summary>A download of this id waits here (after reporting 0.25) until the source is set or its token is cancelled.</summary>
    public Dictionary<string, TaskCompletionSource> Hold { get; } = [];

    /// <summary>A download of this id throws an IOException with this message.</summary>
    public Dictionary<string, string> Fail { get; } = [];

    public SkillsHost Host => host ??= Open();

    public CatalogueItem Item(string id) => Catalogue.Items.Single(i => i.Id == id);

    public SkillsHost Open() => SkillsHost.Open(Root, Options());

    /// <summary>A restart: the host is disposed and opened again on the same root.</summary>
    public SkillsHost Reopen()
    {
        host?.Dispose();
        return host = Open();
    }

    /// <summary>Completes when the download seam has been entered for the id.</summary>
    public Task Entered(string id)
    {
        lock (gate) return (entered.TryGetValue(id, out var source) ? source : entered[id] = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private SkillsHostOptions Options() => SkillsHostRig.Options(Protection, Catalogue) with
    {
        Downloads = Bound ? Download : null,
        Bindings = Bound ? Bind : null,
    };

    private async Task<InstallPlan> Download(CatalogueItem item, ItemRecord record, IProgress<double?> progress, CancellationToken cancellation)
    {
        lock (gate)
        {
            Downloaded.Add(item.Id);
            (entered.TryGetValue(item.Id, out var source) ? source : entered[item.Id] = new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        }
        progress.Report(0.25);
        if (Hold.TryGetValue(item.Id, out var hold)) await hold.Task.WaitAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (Fail.TryGetValue(item.Id, out var message)) throw new IOException(message);
        progress.Report(1);
        var (skill, licence) = inputs[item.Id];
        return new InstallPlan(item, record, Root, InstallerKind.Skillset, new Dictionary<string, string> { ["LICENSE"] = licence, ["skill/SKILL.md"] = skill }) { Source = item.Id, Spares = [] };
    }

    private static PipelineRoute Bind(CatalogueItem item, InstallResult result) => new(item.Id, UpdateCheck.CataloguePin(item)!, result.Path, result.SparePaths.ToArray(), new(StringComparer.Ordinal));

    private CatalogueItem Make(string id, string[] needs, bool pinned = true, bool terms = false)
    {
        var skillBytes = System.Text.Encoding.UTF8.GetBytes("Fixture skill instructions of " + id);
        var licenceBytes = System.Text.Encoding.UTF8.GetBytes("fixture licence of " + id);
        inputs[id] = (Write(id, "skill/SKILL.md", skillBytes), Write(id, "LICENSE", licenceBytes));
        var pin = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..40];
        return new CatalogueItem
        {
            Id = id, Kind = ItemKind.Skillset, Title = id + " title", Summary = "Offline test", Vendor = "Marrow tests", CountState = CountState.Added,
            PinState = pinned ? PinState.Pinned : PinState.Unpinned, Pin = pin, SourceUrl = "https://github.com/example/" + id, LicenceName = "fixture",
            LicenceUrl = "https://raw.githubusercontent.com/example/" + id + "/" + pin + "/LICENSE", LicenceFile = "LICENSE", TermsClass = terms ? TermsClass.Proprietary : TermsClass.Open,
            ApproxSizeMb = null, Needs = needs, Provides = [], Artifacts = [], SkillCount = 1, SkillsListed = true,
            Files = [new CatalogueFile { Path = "LICENSE", Size = licenceBytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(licenceBytes)) },
                new CatalogueFile { Path = "skill/SKILL.md", Size = skillBytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(skillBytes)) }],
            Skills = [new CatalogueSkill { Folder = "skill", Name = "skill", Path = "skill", Needs = [] }],
        };
    }

    private string Write(string id, string name, byte[] bytes)
    {
        var path = Path.Combine(Root, "fixture-inputs", id, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        host?.Dispose();
        SkillsHostRig.Delete(Root);
    }
}
