// PORT-ONLY: ED-NEW-dl-13. The production licence reader of a plugin set whose licences are recorded per plugin, over SK-0's loopback https fake: it reads each named licence file at the
// item's pin and nothing else of the item. No real network; every connection but the fake's is refused by the guarded handler.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "RR-14")]
[Trait("Decision", "ED-NEW-dl-13")]
[Trait("Rule", "R-TEST-10")]
[Collection("Dev data root env")]
public sealed class PluginLicenceFetchTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Each_licence_file_of_a_set_under_licences_per_plugin_is_read_at_the_pin_and_no_other_file_of_the_set_is()
    {
        await using var rig = new LoopbackRig();
        var one = FakeArtifact.FromText("fixture licence of plugin one");
        var prefix = "/example/L1/" + LoopbackRig.Pin + "/";
        rig.Server.Serve(prefix + "plugins/one/LICENSE", one);
        var plugin = rig.Item with
        {
            Kind = ItemKind.Plugin, LicenceFile = null, LicenceUrl = null,
            Folders = [new CatalogueFolder { Name = "plugins/one", SkillCount = 1, LicenceName = "MIT", LicenceFile = "plugins/one/LICENSE" }],
            Skills = [new CatalogueSkill { Folder = "skill", Name = "skill", Path = "plugins/one/skill", Needs = [] }],
            Files = [LoopbackRig.Pinned("plugins/one/.claude-plugin/plugin.json", rig.Small), LoopbackRig.Pinned("plugins/one/LICENSE", one), LoopbackRig.Pinned("plugins/one/skill/SKILL.md", rig.Big)],
        };
        var reader = new DownloaderLicenceText(rig.Downloader(), rig.Root, rig.RawBase);
        Assert.Equal(one.Bytes, await reader.ReadFileAsync(plugin, "plugins/one/LICENSE", CancellationToken.None).WaitAsync(Guard));
        Assert.Equal([prefix + "plugins/one/LICENSE"], rig.Server.Requests.Select(r => r.Path).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadFileAsync(plugin, "plugins/one/skill/SKILL.md", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadFileAsync(plugin with { Folders = [plugin.Folders[0] with { LicenceName = null, LicenceFile = null }] }, "plugins/one/LICENSE", CancellationToken.None));
        Assert.Single(rig.Server.Requests);
        // The item's own licence file still comes from its pinned licence URL.
        Assert.Equal(rig.Licence.Bytes, await reader.ReadFileAsync(rig.Item, "LICENSE", CancellationToken.None).WaitAsync(Guard));
        Assert.Equal(new Uri(rig.Item.LicenceUrl!).AbsolutePath, rig.Server.Requests.Last().Path);
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "addons", "downloads", "licence")) && Directory.EnumerateFileSystemEntries(Path.Combine(rig.Root, "addons", "downloads", "licence")).Any());
    }
}
