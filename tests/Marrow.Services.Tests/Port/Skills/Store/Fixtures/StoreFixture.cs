// PORT-ONLY: offline SK-5 fixture. One teardown for every locked tree, including Windows ACL tests.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Port.Skills.Store.Fixtures;

public sealed class StoreFixture : IDisposable
{
    public StoreFixture(bool windows = false)
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent))
            throw new InvalidOperationException("SK-5 tests require MARROW_PORT_DATA_DIR naming a disposable absolute folder.");
        Root = Path.Combine(parent, "sk5 with spaces " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Protection = windows ? new WindowsStoreProtection() : Fake;
        Trees = new StoreTrees(Protection);
        Paths = new StorePaths(Root);
    }

    public string Root { get; }
    public FakeStoreProtection Fake { get; } = new();
    public IStoreProtection Protection { get; }
    public StoreTrees Trees { get; }
    public StorePaths Paths { get; }
    public FakeStoreDeletion Deletion { get; } = new();
    public FakeRepairDownloader Downloader { get; } = new();
    public FakeRepairInstaller Installer { get; } = new();
    public static readonly IReadOnlyDictionary<string, byte[]> Content = new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
        ["sample/SKILL.md"] = Encoding.UTF8.GetBytes("---\nname: sample\ndescription: Marrow-made offline fixture\n---\nRead the sample.\n"),
        ["sample/scripts/run.py"] = Encoding.UTF8.GetBytes("print('fixture')\n"),
        ["sample/native/fixture.exe"] = [1, 2, 3, 4],
        ["sample/native/fixture.DLL"] = [4, 3, 2, 1],
        ["sample/native/fixture.pyd"] = [5, 6],
        ["sample/native/fixture.node"] = [7, 8],
        ["LICENSE.txt"] = Encoding.UTF8.GetBytes("Marrow-made test fixture; no vendor text.\n"),
    };

    public StoreItem Create(string area = "tools", string name = "T1-1.0-abcd1234", ItemKind kind = ItemKind.Tool)
    {
        var folder = Path.Combine(area == "tools" ? Paths.Tools : Paths.Store, name);
        WriteContent(folder);
        var files = Content.Select(f => new CatalogueFile { Path = f.Key, Size = f.Value.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(f.Value)) }).ToArray();
        var item = new CatalogueItem
        {
            Id = "T1", Kind = kind, Title = "Fixture", Summary = "Offline fixture", Vendor = "Marrow", CountState = CountState.Plan,
            PinState = PinState.Pinned, Pin = "1.0", SourceUrl = "https://example.invalid/fixture", LicenceName = "MIT",
            LicenceFile = "LICENSE.txt", LicenceUrl = "https://example.invalid/LICENSE.txt", TermsClass = TermsClass.Open, ApproxSizeMb = null,
            Needs = [], Provides = [], Artifacts = [new CatalogueArtifact { Name = "fixture.zip", Url = "https://example.invalid/fixture.zip", Size = 1, Sha256 = new string('a', 64) }],
            Files = kind is ItemKind.Skillset or ItemKind.Plugin ? files : [],
            SkillsListed = true, SkillCount = 1, Skills = [new CatalogueSkill { Folder = "sample", Name = "sample", Path = "sample", Needs = [] }],
        };
        Trees.Lock(folder);
        return new StoreItem(item, folder, new StoreManifest(files));
    }

    public static void WriteContent(string folder)
    {
        foreach (var file in Content)
        {
            var path = Path.Combine(folder, file.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.Value);
        }
    }

    public SkillsStore Open(StoreItem item, bool downloader = true, bool installer = true) => new(Paths, [item],
        AddonsState.Restore([new ItemRecord(item.Catalogue.Id, ItemState.Installed, item.Catalogue.Pin, null, null)], [], [], []),
        Protection, Deletion, downloader ? Downloader : null, installer ? Installer : null);

    public void Tamper(StoreItem item, string relative, byte[] bytes)
    {
        Trees.Unlock(item.Folder);
        var path = Path.Combine(item.Folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        Trees.Lock(item.Folder);
    }

    public void Dispose()
    {
        // Unlock ALL before any delete. Fail the test if a deny entry or fixture remains.
        Trees.Unlock(Root);
        foreach (var path in StoreTrees.Entries(Root).Prepend(Root))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && Protection.IsLocked(path))
                throw new InvalidOperationException("A Marrow deny entry remains after fixture teardown: " + path);
        // Unlink planted links first, never following them: .NET's recursive delete refuses a junction for a standard user.
        foreach (var path in StoreTrees.Entries(Root).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0).ToArray())
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Directory.Delete(path); else File.Delete(path);
        Directory.Delete(Root, recursive: true);
        if (Directory.Exists(Root)) throw new InvalidOperationException("SK-5 teardown left a folder.");
    }
}
