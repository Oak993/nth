// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

public sealed class InstallProcessException(int exitCode) : IOException("Installer process failed: " + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture))
{
    public int ExitCode { get; } = exitCode;
}
