// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

internal static class Sfx7zInstaller
{
    public static async Task BuildAsync(InstallPlan plan, string target, string archive, IInstallProcess process, CancellationToken cancellationToken)
    {
        if (plan.Extractor is not null && !Path.GetFullPath(plan.Extractor).Equals(Path.GetFullPath(archive), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The self extractor must be the pinned artifact.");
        var exit = await process.RunAsync(new InstallCommand(archive, ["-o" + target, "-y"], target), cancellationToken);
        if (exit != 0) throw new InstallProcessException(exit);
        if (InstallPaths.TreeFiles(target).Count == 0) throw new InvalidDataException("Extractor returned no payload files.");
    }
}
