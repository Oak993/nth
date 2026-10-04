namespace Marrow.Services.Tests.Port.Skills.Shims;

// PORT-ONLY: SK-6 Windows acceptance is explicitly skipped on other hosts.
public sealed class WindowsShimFactAttribute : FactAttribute
{
    public WindowsShimFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "WINDOWS-ONLY: requires Windows shells and Job Objects.";
    }
}
