// PORT-ONLY: WU-8 capability cache identity, skills-PLAN.md 2.2 (path, size, modified time).
namespace Marrow.Services.Port.Skills.Providers;

internal sealed record CopilotExecutableIdentity(string Path, long Size, long ModifiedTicks)
{
    public static CopilotExecutableIdentity? Read(string? path)
    {
        if (path is null) return null;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            var fullPath = file.FullName;
            return new(OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath, file.Length, file.LastWriteTimeUtc.Ticks);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
