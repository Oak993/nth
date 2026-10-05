// PORT-ONLY test fixture (SK-11f). ProjectGuidance reads the project's files with Windows path rules only, so the one test that needs a real AGENTS.md runs on Windows.
namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class WindowsGuidanceFactAttribute : FactAttribute
{
    public WindowsGuidanceFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "WINDOWS-ONLY: ProjectGuidance resolves the project's AGENTS.md with Windows path rules.";
    }
}
