// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

internal sealed class FailClosedInstallProcess : IInstallProcess
{
    public Task<int> RunAsync(InstallCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Installer process seam is not bound.");
}
