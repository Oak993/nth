// PORT-ONLY: RR-14 (ED-NEW-dl-7 to dl-11). The release catalogue's own items over SK-0's loopback https fake with fixture bytes: every pinned item plans its exact downloads, every runnable item
// installs by its kind through the production allow-list and publishes exactly its shims, links and exports; a wrong byte is refused, a cut download resumes or restarts cleanly, and an
// installed tool survives a restart, an update and its removal. No real network, no downloaded file is run, no installer starts: msiexec, Git's self-extractor and pip are scripted;
// the production launch itself runs a copy of the system's cmd.exe pinned in the self-extractor's place.
using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "RR-14")]
[Trait("Spec", "DL-19")]
[Trait("Rule", "R-TEST-10")]
[Collection("Dev data root env")]
public sealed class CatalogueToolInstallTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(60);

    // The installer each runnable item of the release is built by (ED-NEW-dl-7); written out, not derived, so a catalogue change that moves an item to another installer fails here.
    private static readonly Dictionary<string, InstallerKind> Kinds = new()
    {
        ["T1"] = InstallerKind.PythonEmbed, ["T1p"] = InstallerKind.Zip, ["T2"] = InstallerKind.Zip, ["T3"] = InstallerKind.MsiAdmin, ["T4"] = InstallerKind.Zip, ["T5"] = InstallerKind.Zip, ["T6"] = InstallerKind.Zip,
        ["T7"] = InstallerKind.Sfx7z, ["T8"] = InstallerKind.Zip, ["T9"] = InstallerKind.Zip, ["T10"] = InstallerKind.Zip,
        ["P1"] = InstallerKind.Pyset, ["P2"] = InstallerKind.Pyset, ["P3"] = InstallerKind.Pyset, ["P4"] = InstallerKind.Pyset, ["P5"] = InstallerKind.Pyset, ["P6"] = InstallerKind.Nodeset,
    };

    private static Task<string> Install(Marrow.Services.Port.Skills.Host.SkillsHost host, string id, CatalogueToolRig rig) =>
        host.Downloads.Install(rig.Item(id), new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);

    private static ItemRecord Checking(CatalogueItem item) => new(item.Id, ItemState.Checking, null, UpdateCheck.CataloguePin(item), null);

    [Fact]
    public async Task Every_item_of_the_release_plans_its_exact_downloads_and_an_item_taken_back_to_unpinned_fails_closed_with_the_one_message()
    {
        await using var rig = new CatalogueToolRig();
        var real = rig.Real;
        var plans = new CatalogueInstallPlans(rig.Root, null, real);
        Assert.Equal(29, real.Items.Count);
        Assert.Equal(Kinds.Keys.Order(StringComparer.Ordinal), rig.Catalogue.Items.Select(i => i.Id).Order(StringComparer.Ordinal));
        var perSkillCommits = 0;
        foreach (var item in real.Items)
        {
            Assert.True(CatalogueInstallPlans.CanPlan(item), item.Id);
            if (item.Kind == ItemKind.Pyset)
            {
                // A package set is installed by the installed Python's pip; before T1 is installed it has no interpreter to name.
                Assert.Equal("Install requires T1 first.", Assert.Throws<InvalidOperationException>(() => plans.Plan(item, Checking(item))).Message);
                continue;
            }
            var plan = plans.Plan(item, Checking(item));
            var requests = plans.Requests(plan);
            Assert.All(requests, r => Assert.StartsWith(Path.Combine(rig.Root, "addons", "downloads", "items", item.Id) + Path.DirectorySeparatorChar, r.DestinationPath, StringComparison.OrdinalIgnoreCase));
            if (item.Kind is ItemKind.Skillset or ItemKind.Plugin)
            {
                Assert.Equal(InstallerKind.Skillset, plan.Kind);
                var source = new Uri(item.SourceUrl!).AbsolutePath.Trim('/').Split('/');
                string Commit(string path) => string.IsNullOrEmpty(item.Pin) ? item.Skills.Single(s => s.Path is not null && path.StartsWith(s.Path + "/", StringComparison.Ordinal)).Pin! : item.Pin;
                string Raw(string path) => new Uri("https://raw.githubusercontent.com/" + source[0] + "/" + source[1] + "/" + Commit(path) + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString))).AbsoluteUri;
                Assert.Equal(item.Files.Select(f => (f.Path == item.LicenceFile ? item.LicenceUrl! : Raw(f.Path), f.Size, f.Sha256)),
                    requests.Select(r => (r.Url.AbsoluteUri, r.Size, r.Sha256)));
                if (string.IsNullOrEmpty(item.Pin)) perSkillCommits = item.Files.Where(f => f.Path != item.LicenceFile).Select(f => Commit(f.Path)).Distinct().Count();
                continue;
            }
            Assert.Equal(Kinds[item.Id], plan.Kind);
            Assert.Equal(item.Artifacts.Select(a => (a.Url!, a.Size!.Value, a.Sha256!)).Append((item.LicenceUrl!, item.Files.Single().Size, item.Files.Single().Sha256)),
                requests.Select(r => (r.Url.AbsoluteUri, r.Size, r.Sha256)));
            Assert.Equal(item.Install!.Markers, plan.Markers ?? []);
            switch (plan.Kind)
            {
                case InstallerKind.MsiAdmin: Assert.Equal(CatalogueInstallProcess.Msiexec, plan.Extractor); break;
                case InstallerKind.Sfx7z: Assert.Equal(plan.LocalFiles[item.Artifacts.Single().Name], plan.Extractor); break;
                case InstallerKind.Zip: Assert.Equal(item.Install.Archives.Select(a => (a.Artifact, a.Prefix)), plan.ZipArchives!.Select(z => (z.Artifact, z.Prefix))); break;
                case InstallerKind.PythonEmbed:
                    Assert.Equal(("python314._pth", "python314.zip", "3.14", "pip==26.2.1"), (plan.Python!.PthFile, plan.Python.StandardLibraryZip, plan.Python.AgentVersion, plan.Python.PipWheel!.Requirement));
                    break;
                case InstallerKind.Nodeset: Assert.Equal(item.Install.NodeTree.Select(p => (p.Path, p.Integrity)), plan.NodePackages!.Select(p => (p.TreePath, p.Integrity))); break;
            }
        }
        Assert.True(perSkillCommits > 1, "O3 is fetched file by file at the commits of its own skills");
        // ED-NEW-dl-13: pin run 4 pinned the six items that were blocked, so every item plans. One that lacks a value it must pin fails closed with the one message, as they did: T4 without
        // its artifact's digest, A6 without its commit, O5 with one plugin's licence unknown.
        var t4 = real.Items.Single(i => i.Id == "T4");
        var a6 = real.Items.Single(i => i.Id == "A6");
        var o5 = real.Items.Single(i => i.Id == "O5");
        foreach (var lacking in new[]
        {
            t4 with { PinState = PinState.Unpinned, Artifacts = t4.Artifacts.Select(a => a with { Sha256 = null }).ToList() },
            a6 with { PinState = PinState.Unpinned, Pin = null },
            o5 with { PinState = PinState.Unpinned, Folders = o5.Folders.Select((f, n) => n == 0 ? f with { LicenceName = null, LicenceFile = null } : f).ToList() },
        })
        {
            Assert.False(CatalogueInstallPlans.CanPlan(lacking), lacking.Id);
            Assert.Equal(LibraryWords.DownloadsUnavailable, Assert.Throws<FirstInstallUnavailableException>(() => plans.Plan(lacking, Checking(lacking))).Message);
        }

        // Once the Python the sets need is installed, each real set plans with that interpreter and every pinned wheel by its own pinned requirement.
        await Install(rig.Host, "T1", rig);
        var python = Path.Combine(rig.Host.Pipeline.InstalledFolder("T1")!, "python.exe");
        foreach (var set in real.Items.Where(i => i.Kind == ItemKind.Pyset))
        {
            var plan = plans.Plan(set, Checking(set));
            Assert.Equal(python, plan.Pyset!.PythonExecutable);
            Assert.Equal(set.Artifacts.Select(a => (a.Size!.Value, a.Sha256!, a.Name.Split('-')[0] + "==" + a.Name.Split('-')[1])), plan.Pyset.Wheels.Select(w => (w.Size, w.Sha256, w.Requirement)));
        }
    }

    [Fact]
    public async Task Every_runnable_item_installs_from_its_pinned_downloads_by_its_kind_and_publishes_exactly_its_shims_links_and_exports()
    {
        await using var rig = new CatalogueToolRig();
        foreach (var item in rig.Catalogue.Items) await Install(rig.Host, item.Id, rig);
        string Folder(string id) => rig.Host.Pipeline.InstalledFolder(id) ?? throw new InvalidOperationException(id + " is not installed.");
        foreach (var item in rig.Catalogue.Items)
        {
            var record = rig.Host.Pipeline.State.Get(item.Id);
            Assert.Equal((ItemState.Installed, UpdateCheck.CataloguePin(item)), (record.State, record.InstalledPin));
            Assert.StartsWith(rig.Host.Paths.Tools + Path.DirectorySeparatorChar, Folder(item.Id), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(rig.BytesOf(item.Id, item.LicenceFile!).Bytes, await File.ReadAllBytesAsync(Path.Combine(Folder(item.Id), item.LicenceFile!)));
            foreach (var shim in item.Install!.Shims.Where(s => s.Target is not null)) Assert.True(File.Exists(Path.Combine(Folder(item.Id), shim.Target!)), item.Id + ": " + shim.Target);
            foreach (var marker in item.Install.Markers) Assert.Equal(0, new FileInfo(Path.Combine(Folder(item.Id), marker)).Length);
        }
        // Nothing the downloads left behind: no inputs, no partial file, no installer staging folder.
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "addons", "downloads", "items")) && Directory.EnumerateFileSystemEntries(Path.Combine(rig.Root, "addons", "downloads", "items")).Any());
        Assert.Empty(Directory.EnumerateFiles(ManualFiles.PartFolder(rig.Root), "*.part"));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(rig.Root, "addons", "downloads"), "tmp-*"));

        // The processes: one administrative image by System32's msiexec, Git's own self-extractor where it was downloaded, and pip then compileall per package set, by T1's interpreter.
        var python = Path.Combine(Folder("T1"), "python.exe");
        var msi = rig.Commands.Single(c => c.Executable == CatalogueInstallProcess.Msiexec);
        Assert.Equal(new[] { "/a", Path.Combine(rig.Root, "addons", "downloads", "items", "T3", "artifacts", rig.Item("T3").Artifacts.Single().Name), "/qn" }, msi.Arguments.Take(3));
        Assert.True(msi.QuotePropertyValues);
        var git = rig.Item("T7").Artifacts.Single().Name;
        Assert.Equal(Path.Combine(rig.Root, "addons", "downloads", "items", "T7", "artifacts", git), rig.Commands.Single(c => c.Arguments is [_, "-y"]).Executable);
        var pip = rig.Commands.Where(c => c.Executable == python).ToArray();
        Assert.Equal(10, pip.Length);
        Assert.Equal(12, rig.Commands.Count);
        Assert.All(rig.Commands, c => Assert.StartsWith(Path.Combine(rig.Root, "addons") + Path.DirectorySeparatorChar, c.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
        Assert.All(pip, c =>
        {
            Assert.Equal(("1", "1", "1"), (c.Environment["PIP_NO_CACHE_DIR"], c.Environment["PIP_DISABLE_PIP_VERSION_CHECK"], c.Environment["PIP_NO_INPUT"]));
            Assert.StartsWith(Path.Combine(rig.Root, "addons", "downloads", "tmp-"), c.Environment["PIP_CONFIG_FILE"], StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.Combine(rig.Root, "addons", "downloads", "tmp-"), c.Environment["TEMP"], StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(5, pip.Count(c => c.Arguments is ["-m", "pip", "install", "--no-index", "--find-links", _, "--require-hashes", "--no-deps", "--target", _, "-r", _]));

        // T3: the image's own copy of the package is gone; T1: its pip, its ._pth and the site pip.ini that sends an agent's `pip install` to the work folder; P6: every package of its lock tree.
        Assert.Empty(Directory.EnumerateFiles(Folder("T3"), "*.msi", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(Folder("T1"), "Lib", "site-packages", "pip", "__init__.py")));
        var agent = Path.Combine(rig.Root, "addons", "work", "python-agent", "3.14");
        Assert.Equal("python314.zip\n.\nLib\\site-packages\n" + agent + "\nimport site\n", await File.ReadAllTextAsync(Path.Combine(Folder("T1"), "python314._pth")));
        Assert.Equal("[install]\ntarget = " + agent + "\n", await File.ReadAllTextAsync(Path.Combine(Folder("T1"), "pip.ini")));
        Assert.All(rig.Item("P6").Install!.NodeTree, p => Assert.True(File.Exists(Path.Combine(Folder("P6"), p.Path, "package.json")), p.Path));
        foreach (var set in new[] { "P1", "P2", "P3", "P4", "P5" })
            Assert.All(rig.Item(set).Artifacts, a => Assert.True(File.Exists(Path.Combine(Folder(set), a.Name.Split('-')[0], "__init__.py")), set + ": " + a.Name));

        // Routing: the Python shim sees every package set; pip is T1's Python with -m pip; npm runs T2's node with npm's own script; the exports reach every shim; nothing sets PATH.
        var state = rig.Host.Paths.StateFile;
        var py = PipelineRoutingReader.ResolveShim(state, "python.exe");
        Assert.Equal((python, Path.Combine(Folder("T1"), "python314._pth")), (py.Target, py.PythonPathFile));
        Assert.Equal(new[] { "P1", "P2", "P3", "P4", "P5" }.Select(Folder), py.PackagePaths);
        var pipShim = PipelineRoutingReader.ResolveShim(state, "pip.exe");
        Assert.Equal(python, pipShim.Target);
        Assert.Equal(["-m", "pip"], pipShim.PrefixArguments);
        var node = Path.Combine(Folder("T2"), "node-v24.21.0-win-x64");
        var npm = PipelineRoutingReader.ResolveShim(state, "npm.cmd");
        Assert.Equal((Path.Combine(node, "node.exe"), Path.Combine(node, "node_modules", "npm", "bin", "npm-cli.js")), (npm.Target, Assert.Single(npm.PrefixArguments)));
        var soffice = PipelineRoutingReader.ResolveShim(state, "soffice.exe");
        Assert.Equal((Path.Combine(Folder("T3"), "program", "soffice.exe"), true), (soffice.Target, soffice.LibreOffice));
        Assert.Equal(Path.Combine(Folder("T7"), "bin", "bash.exe"), PipelineRoutingReader.ResolveShim(state, "bash.exe").Target);
        Assert.Equal(Path.Combine(Folder("T8"), "gh", "bin", "gh.exe"), PipelineRoutingReader.ResolveShim(state, "gh.exe").Target);
        Assert.Equal(Path.Combine(Folder("T4"), "poppler-26.09.0", "Library", "bin", "pdftoppm.exe"), PipelineRoutingReader.ResolveShim(state, "pdftoppm.exe").Target);
        Assert.Equal(Path.Combine(Folder("T4"), "poppler-26.09.0", "Library", "bin", "pdftotext.exe"), PipelineRoutingReader.ResolveShim(state, "pdftotext.exe").Target);
        var exported = new Dictionary<string, string>
        {
            ["NODE_PATH"] = Path.Combine(Folder("P6"), "node_modules"), ["PLAYWRIGHT_BROWSERS_PATH"] = Folder("T10"), ["PLAYWRIGHT_SKIP_VALIDATE_HOST_REQUIREMENTS"] = "1",
        };
        Assert.Equal(exported.OrderBy(e => e.Key, StringComparer.Ordinal), py.Environment.OrderBy(e => e.Key, StringComparer.Ordinal));
        Assert.Equal(exported.OrderBy(e => e.Key, StringComparer.Ordinal), npm.Environment.OrderBy(e => e.Key, StringComparer.Ordinal));

        // addons\bin holds one launcher copy and sidecar per published name, kept in step by the pipeline's own change event.
        var names = new[] { "bash.exe", "gh.exe", "node.exe", "npm.cmd", "npx.cmd", "pandoc.exe", "pdftoppm.exe", "pdftotext.exe", "pip.exe", "py.exe", "python.exe", "python3.exe", "qpdf.exe", "sh.exe",
            "soffice.exe", "uv.exe" };
        Assert.Equal(names, PipelineShimSync.Published(rig.Host.Paths));
        var bin = Path.Combine(rig.Host.Paths.Addons, "bin");
        var launcher = await File.ReadAllBytesAsync(rig.Launcher);
        foreach (var name in names)
        {
            var executable = name.EndsWith(".cmd", StringComparison.Ordinal) ? Path.Combine(bin, ".launchers", Path.ChangeExtension(name, ".exe")) : Path.Combine(bin, name);
            Assert.Equal(launcher, await File.ReadAllBytesAsync(executable));
            Assert.True(File.Exists(Path.ChangeExtension(executable, ".shim")), name);
            if (name.EndsWith(".cmd", StringComparison.Ordinal)) Assert.True(File.Exists(Path.Combine(bin, name)), name);
        }
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(launcher)) + "\n", await File.ReadAllTextAsync(Path.Combine(bin, ".launcher")));
    }

    [Fact]
    public async Task A_download_whose_bytes_differ_from_the_pin_is_refused_with_nothing_installed_and_the_next_try_installs_the_right_bytes()
    {
        await using var rig = new CatalogueToolRig();
        var zip = rig.Item("T5").Artifacts.Single().Name;
        rig.Behave("T5", zip, new FakeBehavior { WrongByteAt = 100 });
        var refused = await Assert.ThrowsAsync<IOException>(() => Install(rig.Host, "T5", rig));
        Assert.Equal(DownloadMessages.Mismatch, refused.Message);
        Assert.Null(rig.Host.Pipeline.InstalledFolder("T5"));
        Assert.Null(rig.Host.Pipeline.State.Get("T5").InstalledPin);
        Assert.Empty(Directory.EnumerateFiles(rig.Root, "qpdf.exe", SearchOption.AllDirectories));
        Assert.False(File.Exists(rig.PartOf("T5", zip)));
        Assert.Empty(PipelineShimSync.Published(rig.Host.Paths));

        rig.Server.Serve(rig.PathOf("T5", zip), rig.BytesOf("T5", zip));
        var folder = await Install(rig.Host, "T5", rig);
        Assert.Equal("fixture qpdf-12.4.2-msvc64/bin/qpdf.exe ", await File.ReadAllTextAsync(Path.Combine(folder, "qpdf-12.4.2-msvc64", "bin", "qpdf.exe")));
    }

    [Fact]
    public async Task A_cut_download_keeps_its_partial_starts_nothing_after_a_restart_and_resumes_with_Range_when_pressed_again()
    {
        await using var rig = new CatalogueToolRig();
        var msi = rig.Item("T3").Artifacts.Single().Name;
        var length = rig.BytesOf("T3", msi).Length;
        rig.Behave("T3", msi, new FakeBehavior { DropAfterBytes = 40_000, FaultyRequests = 1 });
        await Assert.ThrowsAsync<IOException>(() => Install(rig.Host, "T3", rig));
        var kept = new FileInfo(rig.PartOf("T3", msi));
        Assert.True(kept.Exists && kept.Length > 0 && kept.Length < length, "the partial file stays for the resume");
        Assert.Empty(rig.Commands);
        var before = rig.Server.Requests.Count;

        var restarted = rig.Reopen();
        await Task.Delay(300);
        Assert.Equal(before, rig.Server.Requests.Count);
        Assert.Null(restarted.Pipeline.InstalledFolder("T3"));

        var folder = await Install(restarted, "T3", rig);
        var tries = rig.Server.Requests.Where(r => r.Path == rig.PathOf("T3", msi)).ToArray();
        Assert.Equal(kept.Length, tries[^1].RangeStart);
        Assert.Equal(206, tries[^1].Status);
        Assert.Equal(length, tries.Sum(r => r.BodyBytesSent)); // every byte crossed the wire once
        Assert.True(File.Exists(Path.Combine(folder, "program", "soffice.exe")));
        Assert.Single(rig.Commands);
    }

    [Fact]
    public async Task A_cut_download_from_a_source_that_ignores_Range_restarts_from_the_first_byte_and_installs()
    {
        await using var rig = new CatalogueToolRig();
        var sfx = rig.Item("T7").Artifacts.Single().Name;
        var bytes = rig.BytesOf("T7", sfx);
        rig.Behave("T7", sfx, new FakeBehavior { DropAfterBytes = 40_000, FaultyRequests = 1, AcceptRanges = false });
        await Assert.ThrowsAsync<IOException>(() => Install(rig.Host, "T7", rig));
        var folder = await Install(rig.Reopen(), "T7", rig);
        var last = rig.Server.Requests.Last(r => r.Path == rig.PathOf("T7", sfx));
        Assert.Equal((200, bytes.Length), (last.Status, last.BodyBytesSent));
        Assert.True(File.Exists(Path.Combine(folder, "bin", "bash.exe")));
        Assert.Empty(Directory.EnumerateFiles(ManualFiles.PartFolder(rig.Root), "*.part"));
    }

    [Fact]
    public async Task An_installed_tool_is_restored_after_a_restart_updated_to_a_new_release_s_pin_and_removed_with_its_shim()
    {
        await using var rig = new CatalogueToolRig();
        var first = await Install(rig.Host, "T5", rig);
        var bin = Path.Combine(rig.Host.Paths.Addons, "bin");
        Assert.True(File.Exists(Path.Combine(bin, "qpdf.exe")));

        var reopened = rig.Reopen();
        Assert.DoesNotContain("T5", reopened.Unrestored);
        Assert.Equal(first, reopened.Pipeline.InstalledFolder("T5"));
        Assert.Equal("12.4.2", reopened.Pipeline.LatestPin("T5"));

        rig.Release("T5", "12.4.3");
        var released = rig.Reopen();
        Assert.DoesNotContain("T5", released.Unrestored); // trusted through the entry its install was verified against
        Assert.Equal(first, released.Pipeline.InstalledFolder("T5"));
        Assert.Equal("12.4.3", released.Pipeline.LatestPin("T5"));
        Assert.Contains(released.Pipeline.UpdateItems(), u => u.Id == "T5");
        await released.Pipeline.UpdateAsync("T5", new PipelineProgress([])).WaitAsync(Guard);
        var second = released.Pipeline.InstalledFolder("T5")!;
        Assert.NotEqual(first, second);
        Assert.Equal("12.4.3", released.Pipeline.State.Get("T5").InstalledPin);
        Assert.Equal("fixture qpdf-12.4.2-msvc64/bin/qpdf.exe 12.4.3", await File.ReadAllTextAsync(Path.Combine(second, "qpdf-12.4.2-msvc64", "bin", "qpdf.exe")));
        Assert.Equal(Path.Combine(second, "qpdf-12.4.2-msvc64", "bin", "qpdf.exe"), PipelineRoutingReader.ResolveShim(released.Paths.StateFile, "qpdf.exe").Target);

        var again = rig.Reopen();
        Assert.DoesNotContain("T5", again.Unrestored);
        Assert.Equal(second, again.Pipeline.InstalledFolder("T5"));

        await again.Pipeline.RemoveAsync("T5").WaitAsync(Guard);
        Assert.Null(again.Pipeline.InstalledFolder("T5"));
        Assert.False(File.Exists(Path.Combine(bin, "qpdf.exe")));
        Assert.False(File.Exists(Path.Combine(bin, "qpdf.shim")));
        Assert.Empty(PipelineShimSync.Published(again.Paths));
        Assert.Null(rig.Reopen().Pipeline.InstalledFolder("T5"));
    }

    [Fact]
    public async Task A_kept_entry_that_no_longer_agrees_with_the_catalogue_at_its_own_pin_is_not_restored()
    {
        await using var rig = new CatalogueToolRig();
        await Install(rig.Host, "T9", rig);
        Assert.DoesNotContain("T9", rig.Reopen().Unrestored);
        rig.Release("T9", rig.Item("T9").Pin!); // the same pin, other bytes: not the build that was verified
        var reopened = rig.Reopen();
        Assert.Contains("T9", reopened.Unrestored);
        Assert.Null(reopened.Pipeline.InstalledFolder("T9"));
        Assert.Empty(PipelineShimSync.Published(reopened.Paths));
    }

    [Fact]
    public async Task The_production_allow_list_admits_exactly_the_installers_processes_and_rechecks_each_one()
    {
        await using var rig = new CatalogueToolRig();
        var allow = new CatalogueInstallProcess(rig.Root, rig.Catalogue);
        var work = Directory.CreateDirectory(Path.Combine(rig.Root, "addons", "downloads", "tmp-allow")).FullName;
        InstallCommand Run(string executable, string? directory = null) => new(executable, [], directory ?? work);

        allow.Allow(Run(CatalogueInstallProcess.Msiexec));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), CatalogueInstallProcess.Msiexec);
        Assert.Throws<InvalidOperationException>(() => allow.Allow(Run(Path.Combine(rig.Root, "msiexec.exe"))));
        Assert.Throws<InvalidOperationException>(() => allow.Allow(Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"))));
        Assert.Throws<InvalidOperationException>(() => allow.Allow(Run(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))));
        Assert.Throws<InvalidDataException>(() => allow.Allow(Run("msiexec.exe"))); // no search of PATH
        Assert.Throws<InvalidDataException>(() => allow.Allow(Run(CatalogueInstallProcess.Msiexec, rig.Root))); // only inside addons

        var name = rig.Item("T7").Artifacts.Single().Name;
        var sfx = Path.Combine(rig.Root, "addons", "downloads", "items", "T7", "artifacts", name);
        Directory.CreateDirectory(Path.GetDirectoryName(sfx)!);
        var bytes = rig.BytesOf("T7", name).Bytes;
        await File.WriteAllBytesAsync(sfx, bytes);
        allow.Allow(Run(sfx));
        var elsewhere = Path.Combine(work, name);
        File.Copy(sfx, elsewhere);
        Assert.Throws<InvalidOperationException>(() => allow.Allow(Run(elsewhere)));
        await File.WriteAllBytesAsync(sfx, [.. bytes[..^1], (byte)(bytes[^1] ^ 1)]);
        Assert.Throws<InvalidDataException>(() => allow.Allow(Run(sfx)));

        var unborn = Path.Combine(rig.Host.Paths.Tools, "T1", "python.exe");
        Assert.Throws<InvalidOperationException>(() => allow.Allow(Run(unborn)));
        await Install(rig.Host, "T1", rig);
        var folder = rig.Host.Pipeline.InstalledFolder("T1")!;
        var python = Path.Combine(folder, "python.exe");
        allow.Allow(Run(python));
        Assert.Throws<InvalidOperationException>(() => allow.Allow(Run(Path.Combine(folder, "python314.dll"))));
        File.SetAttributes(python, FileAttributes.Normal);
        await File.WriteAllTextAsync(python, "not the verified interpreter");
        Assert.Throws<InvalidDataException>(() => allow.Allow(Run(python)));
    }

    [Fact]
    public async Task An_allowed_installer_runs_in_its_kill_on_close_job_with_a_staging_temp_and_the_run_waits_for_what_it_started_and_a_cancel_ends_it()
    {
        // ED-NEW-dl-8, R-PROC-1: the production seam's own launch, no fake. A copy of the system's cmd.exe is pinned as T7's self-extractor, so only the allow-list's sfx rule admits it.
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        await using var rig = new CatalogueToolRig();
        var catalogue = rig.Catalogue with
        {
            Items = rig.Catalogue.Items.Select(i => i.Id != "T7" ? i : i with { Artifacts = [i.Artifacts[0] with { Size = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) }] }).ToList(),
        };
        var downloads = Path.Combine(rig.Root, "addons", "downloads");
        var sfx = Path.Combine(downloads, "items", "T7", "artifacts", rig.Item("T7").Artifacts.Single().Name);
        Directory.CreateDirectory(Path.GetDirectoryName(sfx)!);
        await File.WriteAllBytesAsync(sfx, bytes);
        var work = Directory.CreateDirectory(Path.Combine(downloads, "work")).FullName;
        var process = new CatalogueInstallProcess(rig.Root, catalogue);

        // The child's own exit code comes back only once what it started has ended too (ping -n 3 takes two seconds); its TEMP and HOME sit in a staging folder that is gone afterwards.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var command = new InstallCommand(sfx, ["/d", "/c", ">temp.txt echo %TEMP%& >home.txt echo %HOME%& >set.txt echo %NTH_FIXTURE%& start /b ping -n 3 127.0.0.1 >nul & exit 3"], work)
        {
            Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["NTH_FIXTURE"] = "set for this child" },
        };
        Assert.Equal(3, await process.RunAsync(command, CancellationToken.None));
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(1.5), clock.Elapsed.ToString());
        var temp = (await File.ReadAllTextAsync(Path.Combine(work, "temp.txt"))).Trim();
        Assert.StartsWith(Path.Combine(downloads, "tmp-"), temp, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(temp, "home"), (await File.ReadAllTextAsync(Path.Combine(work, "home.txt"))).Trim(), ignoreCase: true);
        Assert.Equal("set for this child", (await File.ReadAllTextAsync(Path.Combine(work, "set.txt"))).Trim());
        Assert.False(Directory.Exists(temp));

        // A cancel ends the child and everything in its job at once, not after ping's thirty seconds, and still removes the staging folder.
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        clock.Restart();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process.RunAsync(new InstallCommand(sfx, ["/d", "/c", "ping -n 30 127.0.0.1 >nul"], work), cancel.Token));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), clock.Elapsed.ToString());
        Assert.Empty(Directory.GetDirectories(downloads, "tmp-*"));

        // An executable the catalogue does not run never starts.
        await Assert.ThrowsAsync<InvalidOperationException>(() => process.RunAsync(new InstallCommand(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", ">ran.txt echo ran"], work), CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(work, "ran.txt")));
        for (var attempt = 0; File.Exists(sfx); attempt++)
        {
            try { File.Delete(sfx); }
            catch (UnauthorizedAccessException) when (attempt < 100) { await Task.Delay(50); }
        }
    }

    [Fact]
    public async Task The_shim_copies_follow_a_new_launcher_once_and_a_missing_launcher_is_reported_without_touching_the_copies()
    {
        await using var rig = new CatalogueToolRig();
        await Install(rig.Host, "T9", rig);
        var uv = Path.Combine(rig.Host.Paths.Addons, "bin", "uv.exe");
        Assert.Equal("fixture launcher 1", await File.ReadAllTextAsync(uv));

        await File.WriteAllTextAsync(rig.Launcher, "fixture launcher 2");
        rig.Host.SyncShims();
        Assert.Equal("fixture launcher 2", await File.ReadAllTextAsync(uv));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(rig.Launcher))) + "\n", await File.ReadAllTextAsync(Path.Combine(rig.Host.Paths.Addons, "bin", ".launcher")));

        File.Delete(rig.Launcher);
        Assert.Equal("The shim launcher is not beside the app: " + rig.Launcher, PipelineShimSync.Sync(rig.Host.Paths, rig.Launcher));
        Assert.Equal("fixture launcher 2", await File.ReadAllTextAsync(uv));
    }
}
