// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

/// <summary>Explicit binding only. No shell, PATH lookup, or executable runs when the seam is absent.</summary>
public interface IInstallProcess
{
    Task<int> RunAsync(InstallCommand command, CancellationToken cancellationToken);
}
