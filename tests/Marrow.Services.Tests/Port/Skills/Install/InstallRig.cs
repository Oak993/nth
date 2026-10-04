using System.IO.Compression;
using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;

namespace Marrow.Services.Tests.Port.Skills.Install;

internal sealed class InstallRig : IDisposable
{
    public string Root { get; }
    public FakeInstallLock Lock { get; } = new();
    public FakeInstallProcess Process { get; } = new();
    public InstallRig() : this(Path.Combine(Path.GetTempPath(), "marrow-sk4-" + Guid.NewGuid().ToString("N"))) { }
    // integ-sk8-sk9: a crash test reattaches to the root a worker process left behind.
    public InstallRig(string root) { Root = root; Directory.CreateDirectory(Root); }
    public string Input(string name, byte[] bytes)
    {
        var path = Path.Combine(Root, "inputs", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
    public static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var (name, text) in files)
            {
                // integ-sk8-sk9: a fixed entry time makes the archive (and so its pinned digest) the same in every process.
                var entry = zip.CreateEntry(name);
                entry.LastWriteTime = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }
        return memory.ToArray();
    }
    public InstallPlan Plan(InstallerKind kind, byte[]? bytes = null)
    {
        bytes ??= Zip(("payload.txt", "offline fixture"));
        var artifact = Input("payload.zip", bytes);
        var licence = Input("LICENSE", "fixture licence"u8.ToArray());
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var item = new CatalogueItem { Id = "fixture", Kind = ItemKind.Tool, Title = "Fixture", Summary = "Offline test",
            Vendor = "Marrow tests", CountState = CountState.Added, PinState = PinState.Pinned, Pin = "1234567890abcdef1234567890abcdef12345678",
            SourceUrl = "https://github.com/example/fixture", LicenceName = "fixture", LicenceUrl = "https://github.com/example/fixture/LICENSE",
            LicenceFile = "LICENSE", TermsClass = TermsClass.Open, ApproxSizeMb = null, Needs = [], Provides = [],
            Artifacts = [new CatalogueArtifact { Name = "payload.zip", Url = "https://github.com/example/fixture/payload.zip", Size = bytes.Length, Sha256 = digest }],
            Files = [new CatalogueFile { Path = "LICENSE", Size = new FileInfo(licence).Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(licence))) }] };
        return new InstallPlan(item, new ItemRecord(item.Id, ItemState.Checking, null, item.Pin, null), Root, kind,
            new Dictionary<string, string> { ["payload.zip"] = artifact, ["LICENSE"] = licence }) { Source = "test-source" };
    }
    public InstallPlan AddArtifact(InstallPlan plan, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var name = Path.GetFileName(path);
        return plan with { PrimaryArtifact = plan.PrimaryArtifact ?? plan.Item.Artifacts[0].Name,
            Item = plan.Item with { Artifacts = plan.Item.Artifacts.Concat([new CatalogueArtifact { Name = name, Url = "https://github.com/example/fixture/" + name, Size = bytes.Length, Sha256 = InstallHash.Sha256(bytes) }]).ToArray() },
            LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { [name] = path } };
    }
    public InstallPlan AddPip(InstallPlan plan)
    {
        var bytes = Zip(("pip/__init__.py", "fixture pip"));
        var file = Input("pip.whl", bytes);
        return AddArtifact(plan, file) with { Python = new PythonEmbedOptions("python314._pth", "python314.zip", "3.14", new LocalWheel(file, bytes.Length, InstallHash.Sha256(bytes), "pip==26.2.1")) };
    }
    public ItemInstaller Installer(Action<InstallStep, string>? checkpoint = null) => new(Lock, Process, checkpoint);
    public void AssertOnlyTemporaryFolders()
    {
        AssertOnlyTemporaryViews();
        var tools = Path.Combine(Root, "addons", "tools");
        if (Directory.Exists(tools)) Assert.All(Directory.GetDirectories(tools), p => Assert.StartsWith("tmp-", Path.GetFileName(p), StringComparison.Ordinal));
        var store = Path.Combine(Root, "skills", "store");
        if (Directory.Exists(store)) Assert.All(Directory.GetDirectories(store), p => Assert.StartsWith("tmp-", Path.GetFileName(p), StringComparison.Ordinal));
    }
        // Assert no final spare survives a failed transaction, including failures during spare publication.
    public void AssertOnlyTemporaryViews()
    {
        var views = Path.Combine(Root, "skills", "views");
        if (Directory.Exists(views)) foreach (var provider in Directory.GetDirectories(views))
            Assert.All(Directory.GetDirectories(provider), p => Assert.StartsWith("tmp-", Path.GetFileName(p), StringComparison.Ordinal));
    }
    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
