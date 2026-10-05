// PORT-ONLY: first-release feature unit SK-14 ("Download it yourself", the design's step 2 and 3: the folder, then Marrow checks every file against its pinned hash). Fresh data roots; no network, no browser.
using System.IO.Compression;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-17")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class ManualFolderTests
{
    [Fact]
    public void The_folder_is_the_designs_folder_under_the_downloads_folder()
    {
        var root = SkillsHostRig.FreshRoot();
        try
        {
            Assert.Equal(Path.Combine(root, "addons", "downloads", "manual"), ManualFiles.ManualFolder(root));
            Assert.EndsWith("addons\\downloads\\manual\\", ManualFallbackWords.Folder, StringComparison.Ordinal);
        }
        finally { SkillsHostRig.Delete(root); }
    }

    [Fact]
    public async Task A_tool_file_in_the_folder_is_kept_for_the_install_by_its_hash_whatever_its_name_and_a_stranger_is_left_out()
    {
        await using var rig = new LoopbackRig();
        var tool = rig.Tool; // one artifact: the big fixture
        var folder = ManualFiles.ManualFolder(rig.Root);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "whatever-the-browser-called-it.bin"), rig.Big.Bytes);
        File.WriteAllBytes(Path.Combine(folder, "stranger.bin"), [1, 2, 3]);
        Assert.Equal(1, ManualFiles.PlaceFolder(rig.Root, tool));
        Assert.Equal(rig.Big.Length, new FileInfo(rig.PartOf(rig.Big)).Length);
        Assert.True(File.Exists(Path.Combine(folder, "whatever-the-browser-called-it.bin"))); // the user's own file stays
    }

    [Fact]
    public async Task A_repository_zip_gives_every_file_of_a_set_that_its_pinned_hash_names_whatever_the_folder_inside_it_is_called()
    {
        await using var rig = new LoopbackRig();
        var folder = ManualFiles.ManualFolder(rig.Root);
        Directory.CreateDirectory(folder);
        using (var zip = ZipFile.Open(Path.Combine(folder, "L1-" + LoopbackRig.Pin[..7] + ".zip"), ZipArchiveMode.Create))
        {
            foreach (var (path, bytes) in new[] { ("L1-" + LoopbackRig.Pin + "/skill/SKILL.md", rig.Big.Bytes), ("L1-" + LoopbackRig.Pin + "/skill/refs/a b.md", rig.Small.Bytes), ("L1-" + LoopbackRig.Pin + "/README.md", new byte[] { 9 }) })
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        }
        Assert.Equal(2, ManualFiles.PlaceFolder(rig.Root, rig.Item));
        Assert.True(File.Exists(rig.PartOf(rig.Big)));
        Assert.True(File.Exists(rig.PartOf(rig.Small)));
        Assert.False(File.Exists(rig.PartOf(rig.Licence))); // not in the zip: fetched the normal way
    }

    [Fact]
    public async Task With_no_folder_or_nothing_in_it_nothing_matches_and_nothing_is_made_in_the_part_folder()
    {
        await using var rig = new LoopbackRig();
        Assert.Equal(0, ManualFiles.PlaceFolder(rig.Root, rig.Item));
        Directory.CreateDirectory(ManualFiles.ManualFolder(rig.Root));
        Assert.Equal(0, ManualFiles.PlaceFolder(rig.Root, rig.Item));
        Assert.Empty(Directory.EnumerateFiles(ManualFiles.PartFolder(rig.Root), "*.part"));
    }

    [Fact]
    public async Task Check_again_through_the_host_backend_installs_a_set_from_the_folder_with_no_request_for_a_file_it_holds()
    {
        await using var rig = new LoopbackRig();
        var folder = ManualFiles.ManualFolder(rig.Root);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "SKILL.md"), rig.Big.Bytes);
        var backend = new HostLibraryBackend(rig.Host, rig.Catalogue);
        Assert.Equal(folder, backend.ManualFolder);
        Assert.Equal(1, backend.PlaceManualFiles(rig.Item));
        var path = await rig.Host.Downloads.Install(rig.Item, new PipelineProgress([]), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain(rig.Server.Requests, r => r.Path.EndsWith("SKILL.md", StringComparison.Ordinal));
        Assert.Equal(rig.Big.Bytes, await File.ReadAllBytesAsync(Path.Combine(path, "skill", "SKILL.md")));
    }

    [Fact]
    public async Task The_backend_opens_the_page_and_the_folder_through_the_workspace_seam_and_makes_the_folder_first()
    {
        await using var rig = new LoopbackRig();
        var opened = new List<string>();
        using var seam = Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
        var backend = new HostLibraryBackend(rig.Host, rig.Catalogue);
        backend.OpenManualPage(new Uri("https://github.com/example/L1/tree/0123456"));
        Assert.False(Directory.Exists(ManualFiles.ManualFolder(rig.Root)));
        backend.OpenManualFolder();
        Assert.True(Directory.Exists(ManualFiles.ManualFolder(rig.Root)));
        Assert.Equal(2, opened.Count);
        Assert.Equal("https://github.com/example/L1/tree/0123456", opened[0]);
        Assert.StartsWith("file:", opened[1], StringComparison.Ordinal);
        Assert.Equal(new Uri(backend.ManualFolder).AbsoluteUri, opened[1]);
    }
}
