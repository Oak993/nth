using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Lead only. CU-DX-OPEN writes and compiles this NTFS check; never runs it.
[Trait("Decision", "ED-NEW-dextra-open-4")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class AsturFilesNativeTests
{
    [Fact]
    public async Task A_junction_inside_an_Asturs_folder_is_refused_before_any_owned_or_sibling_file_is_deleted()
    {
        using var rig = new AsturTeamRig();
        await rig.RootAsync();
        var astur = (await rig.Team.CreateAsync(AsturTeamRig.Draft("Shop", AsturShape.Squircle))).Profile!;
        var own = rig.Store.AssistantFolder(astur.Id);
        var shared = rig.Store.SharedMemoryFolder();
        File.WriteAllText(Path.Combine(own, "keep.txt"), "fake owned");
        File.WriteAllText(Path.Combine(shared, "keep.txt"), "fake shared");
        var link = Path.Combine(own, "link");
        WindowsJunction.Create(Path.GetDirectoryName(rig.Store.Root)!, link, shared);
        try
        {
            await rig.Team.RetireAsync(astur.Id);
            Assert.False((await rig.Team.DeleteFilesAsync(astur.Id)).Ok);
            Assert.True(File.Exists(Path.Combine(own, "keep.txt")));
            Assert.True(File.Exists(Path.Combine(shared, "keep.txt")));
        }
        finally { Directory.Delete(link, recursive: false); }
    }
}
