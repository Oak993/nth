// PORT-ONLY: UE-3; read-only enrichment of a failed Git installer, for the lead's in-use wording.
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Updates;

public static class GitInstallerLog
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", "LocalState", "DiagOutputDir");

    public static async Task<DevToolCommandResult> Attach(DevToolCommand command, DevToolCommandResult result, Func<string, CancellationToken, Task<string?>> read, CancellationToken cancellation)
    {
        if (command.Executable != "winget" || !command.Arguments.SequenceEqual(WingetUpdater.UpgradeCommand("Git.Git").Arguments) || result.ExitCode is null or 0) return result;
        var match = Regex.Match(result.Stdout + "\n" + result.Stderr, @"(?m)^Installer log is available at:\s*(?<path>[^\r\n]+)", RegexOptions.CultureInvariant);
        if (!match.Success) return result;
        var path = ValidatePath(Environment.ExpandEnvironmentVariables(match.Groups["path"].Value.Trim()), DirectoryPath);
        if (path is null) return result;
        try { return result with { InstallerLog = await read(path, cancellation) }; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return result; }
    }

    public static string? ValidatePath(string path, string directory)
    {
        if (!Path.IsPathFullyQualified(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(full);
            return string.Equals(parent, Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(Path.GetFileName(full), @"\AGit\.Git\.[0-9][0-9A-Za-z.-]*\.log\z", RegexOptions.CultureInvariant) ? full : null;
        }
        catch (ArgumentException) { return null; }
    }
}
