// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

internal static class SkillsetInstaller
{
    public static void Build(InstallPlan plan, string target)
    {
        foreach (var file in plan.Item.Files)
        {
            if (!plan.LocalFiles.TryGetValue(file.Path, out var local)) throw new InvalidDataException("Missing skill file.");
            InstallHash.Verify(local, file.Size, file.Sha256);
            InstallTree.Copy(local, InstallPaths.Under(target, file.Path));
        }
    }
}
