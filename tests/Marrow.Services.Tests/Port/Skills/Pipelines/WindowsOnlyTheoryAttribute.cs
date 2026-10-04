// PORT-ONLY: integ-sk8-sk9. Native ACL theories are explicitly skipped, never silently passed, off Windows.
namespace Marrow.Services.Tests.Port.Skills.Pipelines;

public sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "WINDOWS-ONLY: requires NTFS ACLs that survive the worker process; this worker is not Windows.";
    }
}
