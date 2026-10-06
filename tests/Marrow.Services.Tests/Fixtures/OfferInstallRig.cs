using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
namespace Marrow.Services.Tests.Fixtures;
public sealed class OfferInstallRig : IDisposable
{
    private readonly OfferByteHandler handler;
    public string Root { get; } = Path.Combine(Marrow.Testing.TestDataDirectory.Read() ?? throw new InvalidOperationException("No test root."), "OfferBridge", Guid.NewGuid().ToString("N"));
    public CatalogueItem Item { get; }
    public OfferInstallLock Lock { get; } = new();
    public ItemInstaller Installer { get; }
    public PipelineDownloadSource Source { get; }
    public int Publications { get; private set; }
    public OfferInstallRig()
    {
        Directory.CreateDirectory(Root);
        var files = new Dictionary<string, byte[]> { ["LICENSE.txt"] = Encoding.UTF8.GetBytes("fixture licence"), ["fixture/SKILL.md"] = Encoding.UTF8.GetBytes("fixture skill instructions") };
        Item = SkillsCatalogueLoader.LoadEmbedded().Items.Single(i => i.Id == "A1") with
        {
            Pin = new string('a', 40), PinState = PinState.Pinned, Needs = [],
            SourceUrl = "https://fixtures.invalid/source", LicenceUrl = "https://fixtures.invalid/LICENSE.txt", LicenceFile = "LICENSE.txt",
            Skills = [], SkillCount = 0, SkillsListed = true, Artifacts = [],
            Files = files.Select(p => new CatalogueFile { Path = p.Key, Size = p.Value.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(p.Value)) }).ToArray()
        };
        handler = new(files);
        var downloader = new Downloader(Path.Combine(Root, "parts"), new DownloadPolicy(["fixtures.invalid"]), new DownloadOptions { FreeSpace = _ => long.MaxValue }, handler);
        Source = new(downloader, (item, record) => new InstallPlan(item, record, Root, InstallerKind.Skillset,
            item.Files.ToDictionary(f => f.Path, f => Path.Combine(Root, "work", f.Path.Replace('/', Path.DirectorySeparatorChar)))) { Source = "fixture", Spares = [] },
            plan => plan.Item.Files.Select(f => new DownloadRequest(new Uri("https://fixtures.invalid/" + f.Path), plan.LocalFiles[f.Path], f.Size, f.Sha256)).ToArray());
        Installer = new(Lock);
    }
    public string Publish(CatalogueItem item, InstallResult result)
    {
        if (!Lock.IsLocked(result.Path) || result.Record.Id != item.Id || result.Record.InstalledPin != item.Pin) throw new InvalidDataException("Unverified fixture publication.");
        Publications++;
        return result.Path;
    }
    public void Dispose() { handler.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
