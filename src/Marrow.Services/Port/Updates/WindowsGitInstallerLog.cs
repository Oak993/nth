// PORT-ONLY: UE-3; explicitly composed read-only Git diagnostic binding, never a process operation.
namespace Marrow.Services.Port.Updates;

public static class WindowsGitInstallerLog
{
    public static async Task<string?> Read(string path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var allowed = GitInstallerLog.ValidatePath(path, GitInstallerLog.DirectoryPath);
        if (allowed is null) return null;
        try
        {
            var info = new FileInfo(allowed);
            if (!info.Exists || info.Length > 2 * 1024 * 1024) return null;
            return await File.ReadAllTextAsync(allowed, cancellation);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
