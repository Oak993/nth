namespace Marrow.Services.Tests.Port.Voice.Engine;

/// <summary>
/// VT-6: a test that needs Windows itself (a native Whisper.net load, DXGI, the registry, Win32). On any other system it is reported as SKIPPED
/// with the reason named, never as passed; the Windows verification run executes it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = "WINDOWS-ONLY (" + reason + "): not run on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + "; the Windows verification run executes it.";
    }
}
