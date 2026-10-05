// PORT-ONLY: integ-sk8-sk9, cross-unit gap ED-323 (5)(a): SK-5's GC versus SK-4 builds in flight and fresh spares, on real NTFS.
using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Install;
using WindowsOnlyFactAttribute = Marrow.Services.Tests.Port.Skills.Store.Fixtures.WindowsOnlyFactAttribute;

namespace Marrow.Services.Tests.Port.Skills.Store;

[Trait("Platform", "WINDOWS-ONLY")]
[Trait("Spec", "DL-5")]
public sealed class StoreReservationTests
{
    [WindowsOnlyFact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Store_GC_at_every_install_step_keeps_the_build_and_its_fresh_spares_while_reserved()
    {
        using var rig = new InstallRig();
        var paths = new StorePaths(rig.Root);
        var protection = new WindowsStoreProtection();
        try
        {
            var store = new SkillsStore(paths, [], AddonsState.Empty, protection);
            var steps = new List<InstallStep>();
            var installer = new ItemInstaller(new WindowsInstallLock(), checkpoint: (step, _) => { steps.Add(step); store.Collect(); }, reserve: store.Reserve);
            var result = await installer.InstallAsync(SkillsetPlan(rig));
            Assert.Equal(Enum.GetValues<InstallStep>(), steps.Distinct());
            store.Collect();
            Assert.True(File.Exists(Path.Combine(result.Path, "skill", "SKILL.md")));
            var spare = Assert.Single(result.SparePaths);
            Assert.True(File.Exists(Path.Combine(spare, "skills", "skill", "SKILL.md")));
            Assert.Empty(Directory.GetDirectories(paths.Store, "tmp-*"));
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(spare)!, "tmp-*"));
            // The exclusion is the reservation, not a blanket skip: once released, unregistered trees are ordinary GC candidates.
            Assert.NotNull(result.Reservation);
            result.Reservation!.Dispose();
            store.Collect();
            Assert.False(Directory.Exists(result.Path));
            Assert.False(Directory.Exists(spare));
        }
        finally { Unlock(paths, protection); }
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_failed_build_releases_its_reservation_so_GC_removes_the_tmp_residue()
    {
        using var rig = new InstallRig();
        var paths = new StorePaths(rig.Root);
        var protection = new WindowsStoreProtection();
        try
        {
            var store = new SkillsStore(paths, [], AddonsState.Empty, protection);
            var installer = new ItemInstaller(new WindowsInstallLock(), checkpoint: (step, _) => { if (step == InstallStep.Locked) throw new IOException("Injected failure"); }, reserve: store.Reserve);
            await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(SkillsetPlan(rig)));
            Assert.NotEmpty(Directory.GetDirectories(paths.Store, "tmp-*"));
            store.Collect();
            Assert.Empty(Directory.GetFileSystemEntries(paths.Store));
            var views = Path.Combine(paths.Views, "claude");
            Assert.True(!Directory.Exists(views) || Directory.GetFileSystemEntries(views).Length == 0);
        }
        finally { Unlock(paths, protection); }
    }

    private static InstallPlan SkillsetPlan(InstallRig rig)
    {
        var plan = rig.Plan(InstallerKind.Skillset);
        var skill = "Fixture skill instructions"u8.ToArray();
        return plan with
        {
            Item = plan.Item with
            {
                Kind = ItemKind.Skillset, Artifacts = [], SkillCount = 1, SkillsListed = true,
                Files = plan.Item.Files.Append(new CatalogueFile { Path = "skill/SKILL.md", Size = skill.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(skill)) }).ToArray(),
                Skills = [new CatalogueSkill { Folder = "skill", Name = "skill", Path = "skill", Needs = [] }],
            },
            LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { ["skill/SKILL.md"] = rig.Input("skill", skill) },
            Spares = [new SpareProjection("claude", "fixture", ["skill/SKILL.md"]) { Prefix = "skills" }],
        };
    }

    private static void Unlock(StorePaths paths, IStoreProtection protection)
    {
        var trees = new StoreTrees(protection);
        foreach (var root in new[] { paths.Tools, paths.Store, paths.Views }) trees.Unlock(root);
    }
}
