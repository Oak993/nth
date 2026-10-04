using System.IO.Compression;
using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;

namespace Marrow.Services.Tests.Port.Skills.Install;

internal sealed class InstallRig : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "marrow-sk4-" + Guid.NewGuid().ToString("N"));
    public FakeInstallLock Lock { get; } = new();
    public FakeInstallProcess Process { get; } = new();
    public InstallRig() => Directory.CreateDirectory(Root);
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
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(text);
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
    public ItemInstaller Installer(Action<InstallStep, string>? checkpoint = null) => new(Lock, Process, checkpoint);
    public void AssertOnlyTemporaryFolders()
    {
        var tools = Path.Combine(Root, "addons", "tools");
        if (Directory.Exists(tools)) Assert.All(Directory.GetDirectories(tools), p => Assert.StartsWith("tmp-", Path.GetFileName(p), StringComparison.Ordinal));
        var store = Path.Combine(Root, "skills", "store");
        if (Directory.Exists(store)) Assert.All(Directory.GetDirectories(store), p => Assert.StartsWith("tmp-", Path.GetFileName(p), StringComparison.Ordinal));
    }
    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
