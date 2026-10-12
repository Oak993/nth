// PORT-ONLY: RR-14 (ED-NEW-dl-7 to dl-10). The real catalogue's runnable items (its pins, kinds, needs and installer parameters exactly) with every downloaded byte swapped for Marrow-made
// fixture bytes served by SK-0's loopback https fake, a fresh data root, the production process allow-list in front of a scripted process, and a fake shim launcher file.
// Nothing here reaches a network, runs a downloaded file, starts an installer or writes outside the fresh root: msiexec, the self-extractor and pip are scripted (FakeInstallProcess), and
// the allow-list only checks each command, as it does before every real run.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Tests.Port.Skills.Install;
using Marrow.Services.Tests.Port.Skills.Pipelines;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Fixtures;

public sealed class CatalogueToolRig : IAsyncDisposable
{
    private readonly PipelineProtection protection = new();
    private readonly FakeInstallProcess fake = new();
    private readonly Dictionary<string, (string Path, FakeArtifact Bytes)> served = new(StringComparer.Ordinal);
    private readonly GuardedDownloadHandler online;
    private readonly GuardedDownloadHandler offline;
    private SkillsHost? host;

    public CatalogueToolRig()
    {
        Server = FakeDownloadServer.StartHttps();
        Root = SkillsHostRig.FreshRoot();
        online = FakeDownloadTransport.CreateHandler(Server);
        offline = FakeDownloadTransport.CreateHandler();
        Launcher = Path.Combine(Root, "app", "Marrow.Shim.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(Launcher)!);
        File.WriteAllText(Launcher, "fixture launcher 1");
        Real = SkillsCatalogueLoader.LoadEmbedded();
        Catalogue = SkillsHostRig.Catalogue([.. Real.Items.Where(i => i.Install is not null && CatalogueInstallPlans.CanPlan(i)).Select(i => Fixture(i, ""))]);
        fake.OnRun = Script;
    }

    /// <summary>The embedded release catalogue, as read.</summary>
    public SkillsCatalogue Real { get; }

    /// <summary>The runnable items of <see cref="Real"/>, each with fixture bytes at the fake server in place of its downloads and its licence file.</summary>
    public SkillsCatalogue Catalogue { get; private set; }

    public FakeDownloadServer Server { get; }

    public string Root { get; }

    public string Launcher { get; }

    /// <summary>While true, every download reaches no server ("Couldn't reach 127.0.0.1."); a host already open follows the switch at its next request.</summary>
    public bool Offline { get; set; }

    /// <summary>Every command an installer asked the process seam to run, each after the production allow-list accepted it.</summary>
    public IReadOnlyList<InstallCommand> Commands => fake.Commands;

    public SkillsHost Host => host ??= Open();

    public Uri RawBase => Server.Url("/");

    public Downloader Downloader() => new(ManualFiles.PartFolder(Root), new DownloadPolicy([Server.EndPoint.Address.ToString()]),
        new DownloadOptions { RetryDelays = [], FreeSpace = _ => long.MaxValue }, new Switch(() => Offline, online, offline));

    public SkillsHostOptions Options() => (SkillsHostRig.Options(protection, Catalogue) with { ShimLauncher = Launcher })
        .WithDownloads(Root, Downloader(), RawBase, new AllowedProcess(new CatalogueInstallProcess(Root, Catalogue), fake));

    private SkillsHost Open() => SkillsHost.Open(Root, Options());

    /// <summary>A restart: the host is disposed and opened again on the same root with the rig's current catalogue (nothing carried over in memory).</summary>
    public SkillsHost Reopen()
    {
        host?.Dispose();
        return host = Open();
    }

    /// <summary>The catalogue a later release ships: <paramref name="id"/> at <paramref name="pin"/>, with new fixture bytes for every download. Takes effect at the next <see cref="Reopen"/>.</summary>
    public void Release(string id, string pin)
    {
        var real = Real.Items.Single(i => i.Id == id) with { Pin = pin };
        Catalogue = Catalogue with { Items = [.. Catalogue.Items.Select(i => i.Id == id ? Fixture(real, pin) : i)] };
    }

    public CatalogueItem Item(string id) => Catalogue.Items.Single(i => i.Id == id);

    /// <summary>The fake server path the fixture bytes of <paramref name="name"/> (an artifact or the licence file) of item <paramref name="id"/> are served at.</summary>
    public string PathOf(string id, string name) => new Uri(ArtifactUrl(Item(id), name)).AbsolutePath;

    public FakeArtifact BytesOf(string id, string name) => served[PathOf(id, name)].Bytes;

    public void Behave(string id, string name, FakeBehavior behavior) => Server.Serve(PathOf(id, name), BytesOf(id, name), behavior);

    public string PartOf(string id, string name) => Path.Combine(ManualFiles.PartFolder(Root), BytesOf(id, name).Sha256Hex[..8] + ".part");

    private static string ArtifactUrl(CatalogueItem item, string name) =>
        item.Artifacts.FirstOrDefault(a => a.Name == name)?.Url ?? (name == item.LicenceFile ? item.LicenceUrl! : throw new KeyNotFoundException(name));

    private CatalogueItem Fixture(CatalogueItem real, string salt)
    {
        var artifacts = real.Artifacts.Select(a =>
        {
            var original = new Uri(a.Url!);
            var bytes = Serve("/" + original.Host + original.AbsolutePath + (salt.Length == 0 ? "" : "." + salt), () => Bytes(real, a.Name, salt));
            return a with { Url = Server.Url(bytes.Path).AbsoluteUri, Size = bytes.Bytes.Length, Sha256 = bytes.Bytes.Sha256Hex };
        }).ToList();
        var licence = Serve("/licence/" + real.Id + (salt.Length == 0 ? "" : "/" + salt) + "/" + real.LicenceFile, () => FakeArtifact.FromText("Fixture licence of " + real.Id + " " + salt));
        var install = real.Install!;
        if (install.Kind == InstallKind.Nodeset)
        {
            install = install with
            {
                NodeTree = [.. install.NodeTree.Select(p =>
                {
                    var bytes = artifacts.Single(a => a.Name == p.Artifact);
                    return p with { Integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(served[new Uri(bytes.Url!).AbsolutePath].Bytes.Bytes)) };
                })],
            };
        }
        return real with
        {
            Artifacts = artifacts, Install = install, LicenceUrl = Server.Url(licence.Path).AbsoluteUri,
            Files = [new CatalogueFile { Path = real.LicenceFile!, Size = licence.Bytes.Length, Sha256 = licence.Bytes.Sha256Hex }],
        };
    }

    private (string Path, FakeArtifact Bytes) Serve(string path, Func<FakeArtifact> make)
    {
        if (served.TryGetValue(path, out var known)) return known;
        var bytes = make();
        Server.Serve(path, bytes);
        return served[path] = (path, bytes);
    }

    /// <summary>Fixture bytes of the kind the item's installer reads: a zip holding every path the installer parameters name, a wheel, an npm tarball, or opaque bytes for what a scripted process unpacks.</summary>
    private static FakeArtifact Bytes(CatalogueItem item, string name, string salt)
    {
        var install = item.Install!;
        byte[] Text(string text) => Encoding.UTF8.GetBytes(text + " " + salt);
        if (name.EndsWith(".whl", StringComparison.Ordinal))
        {
            var parts = name.Split('-');
            return FakeArtifact.Zip((parts[0] + "/__init__.py", Text("# " + name)), (parts[0] + "-" + parts[1] + ".dist-info/METADATA", Text("Name: " + parts[0])));
        }
        if (name.EndsWith(".tgz", StringComparison.Ordinal))
        {
            var tree = install.NodeTree.First(p => p.Artifact == name).Path;
            // npm drops the one top folder of a tarball: "package" for npm pack, the package's own name in DefinitelyTyped's @types tarballs (ED-NEW-dl-7).
            var top = tree.Contains("/@types/", StringComparison.Ordinal) ? tree[(tree.LastIndexOf('/') + 1)..] : "package";
            return new FakeArtifact(TarFixture.Package((top + "/package.json", "{\"name\":\"" + tree + "\"}"), (top + "/index.js", "module.exports = '" + name + salt + "';")));
        }
        if (install.Kind == InstallKind.PythonEmbed)
            return FakeArtifact.Zip(("python.exe", Text("fixture python")), ("python314.dll", Text("fixture dll")), ("python314.zip", Text("fixture stdlib")), (install.Python!.PthFile, Text("python314.zip\n.\n")));
        if (install.Kind is InstallKind.MsiAdmin or InstallKind.Sfx7z) return new FakeArtifact([.. Text("opaque " + name), .. FakeArtifact.Pattern(100_000, seed: name.Length).Bytes]);
        var prefix = install.Archives.Single(a => a.Artifact == name).Prefix;
        var named = install.Shims.Where(s => s.Target is not null).Select(s => s.Target!)
            .Concat(install.Shims.SelectMany(s => s.Arguments).Where(a => a.StartsWith("{folder}/", StringComparison.Ordinal)).Select(a => a["{folder}/".Length..]))
            .Distinct(StringComparer.Ordinal)
            .Where(p => prefix is null ? install.Archives[0].Artifact == name : p.StartsWith(prefix + "/", StringComparison.Ordinal))
            .Select(p => prefix is null ? p : p[(prefix.Length + 1)..]);
        return FakeArtifact.Zip([.. named.Select(p => (p, Text("fixture " + p))), ("fixture-" + name + ".txt", Text("payload of " + name))]);
    }

    /// <summary>What the scripted installer processes leave: an administrative image with its package copy, a self-extracted Git, or pip's packages in its target.</summary>
    private static void Script(InstallCommand command)
    {
        string Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
        if (command.Arguments is ["/a", var msi, "/qn", var targetDir] && targetDir.StartsWith("TARGETDIR=", StringComparison.Ordinal))
        {
            var target = targetDir["TARGETDIR=".Length..];
            Write(Path.Combine(target, "program", "soffice.exe"), "fixture soffice");
            File.Copy(msi, Path.Combine(target, Path.GetFileName(msi)));
        }
        else if (command.Arguments is [var output, "-y"] && output.StartsWith("-o", StringComparison.Ordinal))
        {
            Write(Path.Combine(output[2..], "bin", "bash.exe"), "fixture bash");
            Write(Path.Combine(output[2..], "bin", "sh.exe"), "fixture sh");
        }
        else if (command.Arguments is ["-m", "pip", "install", .., "--target", var target, "-r", var lockFile])
        {
            foreach (var line in File.ReadAllLines(lockFile).Where(l => l.Length > 0))
                Write(Path.Combine(target, line[..line.IndexOf("==", StringComparison.Ordinal)], "__init__.py"), "# " + line);
        }
        else if (command.Arguments is not ["-m", "compileall", ..]) throw new InvalidOperationException("Unscripted installer command: " + command.Executable);
    }

    private sealed class Switch(Func<bool> offline, HttpMessageHandler online, HttpMessageHandler none) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker on = new(online, disposeHandler: false);
        private readonly HttpMessageInvoker off = new(none, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => (offline() ? off : on).SendAsync(request, cancellationToken);
    }

    private sealed class AllowedProcess(CatalogueInstallProcess allow, IInstallProcess inner) : IInstallProcess
    {
        public Task<int> RunAsync(InstallCommand command, CancellationToken cancellationToken)
        {
            allow.Allow(command);
            return inner.RunAsync(command, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        host?.Dispose();
        online.Dispose();
        offline.Dispose();
        await Server.DisposeAsync();
        SkillsHostRig.Delete(Root);
    }
}
