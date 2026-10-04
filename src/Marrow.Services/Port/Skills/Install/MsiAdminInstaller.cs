// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

internal static class MsiAdminInstaller
{
    public static async Task BuildAsync(InstallPlan plan, string target, string archive, IInstallProcess process, CancellationToken cancellationToken)
    {
        var executable = plan.Extractor ?? throw new InvalidDataException("An explicit msiexec path is required.");
        var exit = await process.RunAsync(new InstallCommand(executable, ["/a", archive, "/qn", "TARGETDIR=" + target], target), cancellationToken);
        // Windows Installer ERROR_SUCCESS_REBOOT_REQUIRED reports a completed image. REBOOT_INITIATED does not satisfy a quiet image build.
        if (exit is not (0 or 3010)) throw new InstallProcessException(exit);
        if (InstallPaths.TreeFiles(target).Count == 0) throw new InvalidDataException("Extractor returned no payload files.");
    }
}
