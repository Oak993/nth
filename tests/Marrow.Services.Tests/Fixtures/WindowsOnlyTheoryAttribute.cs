// PORT-ONLY: integ-sk4-sk5. A theory that needs NTFS ACLs or Windows process launch is explicitly skipped elsewhere, never silently passed.
namespace Marrow.Services.Tests.Fixtures;

public sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "WINDOWS-ONLY: requires NTFS ACLs or Windows executable loading; no substitute on this host.";
    }
}
