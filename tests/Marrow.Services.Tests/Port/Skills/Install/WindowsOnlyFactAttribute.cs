namespace Marrow.Services.Tests.Port.Skills.Install;
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "WINDOWS-ONLY: requires Windows executable loading, NTFS reparse points/streams, or Windows ACLs; no Linux substitute.";
    }
}
