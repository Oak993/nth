// PORT-ONLY: user-authorized native ACL tests must be explicitly skipped, never silently passed, on Linux.
using Xunit;

namespace Marrow.Services.Tests.Port.Skills.Store.Fixtures;

public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "WINDOWS-ONLY: requires NTFS ACLs, named streams and Windows delete-sharing semantics; this worker is not Windows.";
    }
}
