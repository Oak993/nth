// PORT-ONLY: first-release feature unit SK-16 (DL-19). The production seams over SK-0's loopback https fake: one skill set whose files are served at the raw-file paths, a tool, a fresh data root.
// Nothing here reaches a network, a CLI or an installer.
using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

internal sealed class LoopbackRig : IAsyncDisposable
{
    public const string Pin = "0123456789abcdef0123456789abcdef01234567";

    private readonly PipelineProtection protection = new();
    private SkillsHost? host;

    public LoopbackRig()
    {
        Server = FakeDownloadServer.StartHttps();
        Root = SkillsHostRig.FreshRoot();
        Big = FakeArtifact.Pattern(100_000, seed: 7);
        Small = FakeArtifact.FromText("a reference with a space in its name");
        Licence = FakeArtifact.FromText("fixture licence of L1");
        var prefix = "/example/L1/" + Pin + "/";
        Server.Serve(prefix + "LICENSE", Licence).Serve(prefix + "skill/SKILL.md", Big).Serve(prefix + "skill/refs/a%20b.md", Small);
        Handler = FakeDownloadTransport.CreateHandler(Server);
        Item = new CatalogueItem
        {
            Id = "L1", Kind = ItemKind.Skillset, Title = "L1 title", Summary = "Offline test", Vendor = "Marrow tests", CountState = CountState.Added, PinState = PinState.Pinned, Pin = Pin,
            SourceUrl = "https://github.com/example/L1", LicenceName = "fixture", LicenceUrl = Server.Url(prefix + "LICENSE").AbsoluteUri, LicenceFile = "LICENSE", TermsClass = TermsClass.Open,
            ApproxSizeMb = null, Needs = [], Provides = [], Artifacts = [], SkillCount = 1, SkillsListed = true,
            Files = [Pinned("LICENSE", Licence), Pinned("skill/SKILL.md", Big), Pinned("skill/refs/a b.md", Small)],
            Skills = [new CatalogueSkill { Folder = "skill", Name = "skill", Path = "skill", Needs = [] }],
        };
        Tool = Item with { Id = "T1", Kind = ItemKind.Tool, Title = "T1 title", Files = [Pinned("LICENSE", Licence)], Skills = [], SkillCount = null, SkillsListed = false,
            Artifacts = [new CatalogueArtifact { Name = "tool.zip", Url = "https://github.com/example/T1/tool.zip", Size = Big.Length, Sha256 = Big.Sha256Hex }] };
        Catalogue = SkillsHostRig.Catalogue(Item, Tool);
    }

    public FakeDownloadServer Server { get; }

    public string Root { get; }

    public FakeArtifact Big { get; }

    public FakeArtifact Small { get; }

    public FakeArtifact Licence { get; }

    public GuardedDownloadHandler Handler { get; }

    public CatalogueItem Item { get; }

    public CatalogueItem Tool { get; }

    public SkillsCatalogue Catalogue { get; }

    public Uri RawBase => Server.Url("");

    public Downloader Downloader() => new(ManualFiles.PartFolder(Root), new DownloadPolicy([Server.EndPoint.Address.ToString()]), new DownloadOptions { RetryDelays = [], FreeSpace = _ => long.MaxValue }, Handler);

    public SkillsHost Host => host ??= Open();

    public SkillsHost Open() => SkillsHost.Open(Root, SkillsHostRig.Options(protection, Catalogue).WithDownloads(Root, Downloader(), RawBase));

    /// <summary>A restart: the host is disposed and opened again on the same root (a new downloader, nothing carried over in memory).</summary>
    public SkillsHost Reopen()
    {
        host?.Dispose();
        return host = Open();
    }

    public string PartOf(FakeArtifact artifact) => Path.Combine(ManualFiles.PartFolder(Root), artifact.Sha256Hex[..8] + ".part");

    public static CatalogueFile Pinned(string path, FakeArtifact artifact) => new() { Path = path, Size = artifact.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(artifact.Bytes)) };

    public async ValueTask DisposeAsync()
    {
        host?.Dispose();
        Handler.Dispose();
        await Server.DisposeAsync();
        SkillsHostRig.Delete(Root);
    }
}
