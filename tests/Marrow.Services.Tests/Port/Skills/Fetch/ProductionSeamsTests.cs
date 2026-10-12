// PORT-ONLY: first-release feature unit SK-16 (DL-19): the production download, binding and licence seams over SK-0's loopback https fake. Restart and resume are proved against the fake server's own log.
// No real network, CLI or installer; every connection but the fake's is refused by the guarded handler.
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Port.Skills.Pipelines;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-326")]
[Trait("Rule", "R-TEST-10")]
[Collection("Dev data root env")]
public sealed class ProductionSeamsTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_skill_set_is_fetched_file_by_file_from_the_raw_paths_installed_and_its_downloaded_inputs_are_gone()
    {
        await using var rig = new LoopbackRig();
        var path = await rig.Host.Downloads.Install(rig.Item, new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        var prefix = "/example/L1/" + LoopbackRig.Pin + "/";
        Assert.Equal([prefix + "LICENSE", prefix + "skill/SKILL.md", prefix + "skill/refs/a%20b.md"], rig.Server.Requests.Select(r => r.Path).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(rig.Big.Bytes, await File.ReadAllBytesAsync(Path.Combine(path, "skill", "SKILL.md")));
        Assert.Equal(rig.Small.Bytes, await File.ReadAllBytesAsync(Path.Combine(path, "skill", "refs", "a b.md")));
        Assert.Equal(ItemState.Installed, rig.Host.Pipeline.State.Get("L1").State);
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "addons", "downloads", "items", "L1")));
        Assert.Empty(Directory.EnumerateFiles(ManualFiles.PartFolder(rig.Root), "*.part"));
    }

    [Fact]
    public async Task A_download_the_connection_dropped_in_keeps_its_partial_and_nothing_starts_after_a_restart_until_Download_is_pressed_which_resumes_with_Range()
    {
        await using var rig = new LoopbackRig();
        var prefix = "/example/L1/" + LoopbackRig.Pin + "/";
        rig.Server.Serve(prefix + "skill/SKILL.md", rig.Big, new FakeBehavior { DropAfterBytes = 40_000, FaultyRequests = 1 });
        var first = await Assert.ThrowsAsync<IOException>(() => rig.Host.Downloads.Install(rig.Item, new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard));
        Assert.NotEmpty(first.Message);
        Assert.Null(rig.Host.Pipeline.InstalledFolder("L1"));
        var kept = new FileInfo(rig.PartOf(rig.Big));
        Assert.True(kept.Exists && kept.Length is > 0 and < 100_000, "the partial file stays for the resume");
        var before = rig.Server.Requests.Count;

        var restarted = rig.Reopen(); // the app was closed and started again
        await Task.Delay(300); // nothing starts on its own
        Assert.Equal(before, rig.Server.Requests.Count);
        Assert.False(restarted.Downloads.IsRunning("L1"));
        Assert.Null(restarted.Pipeline.InstalledFolder("L1"));

        var path = await restarted.Downloads.Install(rig.Item, new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        var big = rig.Server.Requests.Where(r => r.Path.EndsWith("SKILL.md", StringComparison.Ordinal)).ToArray();
        Assert.Equal(kept.Length, big.Last().RangeStart);
        Assert.Equal(206, big.Last().Status);
        Assert.Equal(100_000L, big.Sum(r => r.BodyBytesSent)); // every byte crossed the wire exactly once
        Assert.Equal(rig.Big.Bytes, await File.ReadAllBytesAsync(Path.Combine(path, "skill", "SKILL.md")));
    }

    [Fact]
    public async Task A_file_the_user_placed_by_hand_is_taken_without_a_request_for_it_and_a_wrong_file_is_refused()
    {
        await using var rig = new LoopbackRig();
        var wrong = Path.Combine(rig.Root, "not-it.bin");
        await File.WriteAllBytesAsync(wrong, [1, 2, 3]);
        var refused = Assert.Throws<InvalidDataException>(() => ManualFiles.Place(rig.Root, rig.Item, wrong));
        Assert.Equal(DownloadMessages.Mismatch, refused.Message);
        Assert.False(Directory.Exists(ManualFiles.PartFolder(rig.Root)) && Directory.EnumerateFiles(ManualFiles.PartFolder(rig.Root)).Any());

        var mine = Path.Combine(rig.Root, "SKILL.md");
        await File.WriteAllBytesAsync(mine, rig.Big.Bytes);
        Assert.Equal("skill/SKILL.md", ManualFiles.Place(rig.Root, rig.Item, mine));
        Assert.Equal(rig.Big.Length, new FileInfo(rig.PartOf(rig.Big)).Length);

        var path = await rig.Host.Downloads.Install(rig.Item, new PipelineProgress([]), CancellationToken.None).WaitAsync(Guard);
        Assert.DoesNotContain(rig.Server.Requests, r => r.Path.EndsWith("SKILL.md", StringComparison.Ordinal));
        Assert.Equal(2, rig.Server.Requests.Count);
        Assert.Equal(rig.Big.Bytes, await File.ReadAllBytesAsync(Path.Combine(path, "skill", "SKILL.md")));
    }

    [Fact]
    public async Task Requests_name_each_pinned_file_with_its_pinned_size_and_hash_and_the_licence_comes_from_its_own_pinned_url()
    {
        await using var rig = new LoopbackRig();
        var plans = new CatalogueInstallPlans(rig.Root, rig.RawBase);
        var record = new ItemRecord("L1", ItemState.Checking, null, LoopbackRig.Pin, null);
        var plan = plans.Plan(rig.Item, record);
        Assert.Equal(("L1", Marrow.Services.Port.Skills.Install.InstallerKind.Skillset), (plan.Source, plan.Kind));
        Assert.Empty(plan.Spares!);
        var requests = plans.Requests(plan);
        Assert.Equal(rig.Item.Files.Select(f => (f.Size, f.Sha256)), requests.Select(r => (r.Size, r.Sha256)));
        Assert.Equal(rig.Item.LicenceUrl, requests.Single(r => r.Size == rig.Licence.Length).Url.AbsoluteUri);
        Assert.Equal(rig.RawBase.GetLeftPart(UriPartial.Authority) + "/example/L1/" + LoopbackRig.Pin + "/skill/refs/a%20b.md", plans.Url(rig.Item, "skill/refs/a b.md").AbsoluteUri);
        Assert.All(requests, r => Assert.StartsWith(Path.Combine(rig.Root, "addons", "downloads", "items"), r.DestinationPath, StringComparison.OrdinalIgnoreCase));
    }

    // ED-NEW-dl-7 (RR-14) replaces ED-368 (5)'s interim test, which also held a set at per-skill pins closed: a tool without installer parameters still fails closed with the one message, and a
    // set at per-skill pins (O3) now fetches each file at its own skill's commit, while a file no listed skill holds keeps the set from being offered.
    [Fact]
    public async Task A_tool_without_installer_parameters_fails_closed_and_a_set_at_per_skill_pins_fetches_each_file_at_its_own_skill_s_commit()
    {
        await using var rig = new LoopbackRig();
        var plans = new CatalogueInstallPlans(rig.Root, rig.RawBase);
        Assert.False(CatalogueInstallPlans.CanPlan(rig.Tool));
        var tool = Assert.Throws<FirstInstallUnavailableException>(() => plans.Plan(rig.Tool, new ItemRecord("T1", ItemState.Checking, null, LoopbackRig.Pin, null)));
        Assert.Equal(LibraryWords.DownloadsUnavailable, tool.Message);

        const string Other = "89abcdef0123456789abcdef0123456789abcdef";
        var multiPin = rig.Item with { Pin = null, Skills = [new Marrow.Core.Port.Skills.Catalogue.CatalogueSkill { Folder = "skill", Name = "skill", Path = "skill", Needs = [], Pin = Other }] };
        Assert.True(CatalogueInstallPlans.CanPlan(multiPin));
        var plan = plans.Plan(multiPin, new ItemRecord("L1", ItemState.Checking, null, Marrow.Core.Port.Skills.State.UpdateCheck.CataloguePin(multiPin), null));
        var at = rig.RawBase.GetLeftPart(UriPartial.Authority) + "/example/L1/" + Other + "/";
        Assert.Equal([rig.Item.LicenceUrl!, at + "skill/SKILL.md", at + "skill/refs/a%20b.md"], plans.Requests(plan).Select(r => r.Url.AbsoluteUri).ToArray());

        var stray = multiPin with { Files = [.. multiPin.Files, LoopbackRig.Pinned("other/README.md", rig.Small)] };
        Assert.False(CatalogueInstallPlans.CanPlan(stray));
        Assert.Throws<FirstInstallUnavailableException>(() => plans.Plan(stray, new ItemRecord("L1", ItemState.Checking, null, Marrow.Core.Port.Skills.State.UpdateCheck.CataloguePin(stray), null)));
        Assert.Empty(rig.Server.Requests);
    }

    [Fact]
    public async Task The_licence_text_is_fetched_from_the_pinned_licence_url_alone_and_leaves_nothing_behind()
    {
        await using var rig = new LoopbackRig();
        var text = await new DownloaderLicenceText(rig.Downloader(), rig.Root).ReadAsync(rig.Item, CancellationToken.None).WaitAsync(Guard);
        Assert.Equal(rig.Licence.Bytes, text);
        Assert.Equal([new Uri(rig.Item.LicenceUrl!).AbsolutePath], rig.Server.Requests.Select(r => r.Path).ToArray());
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "addons", "downloads", "licence")) && Directory.EnumerateFileSystemEntries(Path.Combine(rig.Root, "addons", "downloads", "licence")).Any());
    }

    [Fact]
    public async Task The_production_options_bind_the_download_binding_and_licence_seams_and_leave_the_rest_as_they_were()
    {
        await using var rig = new LoopbackRig();
        var bare = Marrow.Services.Port.Skills.Host.SkillsHostOptions.Production();
        var bound = bare.WithDownloads(rig.Root, rig.Downloader(), rig.RawBase);
        Assert.NotNull(bound.Downloads);
        Assert.NotNull(bound.Bindings);
        Assert.NotNull(bound.LicenceText);
        Assert.Same(bare.Protection.GetType(), bound.Protection.GetType());
        Assert.Same(bare.Catalogue, bound.Catalogue);
        Assert.Null(bare.Downloads);
        await Task.CompletedTask;
    }
}
