// PORT-ONLY: SK-13 review (OD-0099 amendments): "each downloaded on the user's request from the vendor's own official source with its licence file kept beside it". Whatever the installer kind, the
// installed folder holds the pinned licence file byte for byte, it is part of the item's trusted inventory (so tampering or deleting it is damage), and an update brings the new pin's licence.
using System.Text;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Terms;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-15")]
[Trait("Review", "SK-13")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class LicenceBesideItemTests
{
    public static TheoryData<string> Kinds => new() { "zip", "skillset", "msi-admin" };

    private static PipelineRig Rig(string kind) => new(skillset: kind == "skillset", msi: kind == "msi-admin", root: SkillsHostRig.FreshRoot());

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_installed_folder_holds_the_pinned_licence_file_byte_for_byte_and_it_is_part_of_the_trusted_inventory(string kind)
    {
        using var rig = Rig(kind);
        await rig.Create();
        var trusted = rig.Trusted(rig.Old.Pin!);
        var licence = Path.Combine(trusted.Folder, "LICENSE");
        Assert.Equal("fixture licence", File.ReadAllText(licence));
        Assert.Equal(rig.Old.Files.Single(f => f.Path == rig.Old.LicenceFile).Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(licence))));
        Assert.Contains(trusted.Manifest.Files, f => f.Path == "LICENSE");
    }

    [Fact]
    public async Task Deleting_or_changing_the_licence_beside_an_item_is_damage_the_store_never_uses()
    {
        using var rig = Rig("zip");
        var pipeline = await rig.Create();
        var folder = rig.Trusted(rig.Old.Pin!).Folder;
        var trees = new Marrow.Services.Port.Skills.Store.StoreTrees(rig.Protection);
        trees.Unlock(folder);
        File.WriteAllText(Path.Combine(folder, "LICENSE"), "another licence");
        trees.Lock(folder);
        Assert.Null(pipeline.AcquireForViewBuild("fixture")); // the whole-tree check (the per-launch seal hashes only binaries: ED-321 (4))
        Assert.Equal(ItemState.Damaged, pipeline.State.Get("fixture").State);
    }

    [Fact]
    public async Task An_update_installs_the_new_pins_licence_beside_the_new_version()
    {
        using var rig = new PipelineRig(nonOpen: true, root: SkillsHostRig.FreshRoot());
        var pipeline = await rig.Create();
        pipeline.AcceptTerms("fixture", new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero)); // the new pin's licence text has a new hash
        await pipeline.UpdateAsync("fixture", rig.Progress);
        var updated = rig.Trusted(rig.Next.Pin!);
        Assert.Equal("new fixture licence", File.ReadAllText(Path.Combine(updated.Folder, "LICENSE"), Encoding.UTF8));
        Assert.Contains(updated.Manifest.Files, f => f.Path == "LICENSE");
    }
}
