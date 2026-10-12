namespace Marrow.Core.Support;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3, 5 and 8, unit U9, ED-NEW-inst-u9-1). Where nth Setup keeps what it
/// writes outside the install root, in one place, so the code that writes it and the code that sweeps it agree:
/// <c>%LOCALAPPDATA%\nth Setup\</c> holds one staging folder per version (<c>&lt;version&gt;\</c>: the Velopack engine, the
/// options and result files, the log) and the native libraries of the running setup
/// (<c>runtime\&lt;build hash&gt;\</c>); <c>%TEMP%\nth Uninstall\</c> is the uninstaller's temporary copy (PLAN.md 8). The
/// data folder <c>%LOCALAPPDATA%\nth</c>, the install root and everything else are not here and are never swept. This file
/// is also compiled into src/Marrow.Setup (like InstallOptions.cs), so it uses nothing from Marrow.Core but that file.
/// </summary>
public static class SetupFolders
{
    /// <summary>The named lock the setup (and the uninstaller) holds while its window is open (PLAN.md 3, step 1).</summary>
    public const string SetupLockName = @"Local\nth Setup";

    /// <summary>The folder under the setup's own that holds the native libraries, one folder per build (PLAN.md 5).</summary>
    public const string RuntimeFolderName = "runtime";

    /// <summary>Velopack's <c>Setup.exe</c> as staged (PLAN.md 5).</summary>
    public const string EngineFileName = "nth Setup Engine.exe";

    /// <summary>The file a running setup keeps open in a folder it is using (see <see cref="FolderClaim"/>).</summary>
    public const string ClaimFileName = "in-use.lock";

    /// <summary>The uninstaller's temporary copy lives in this folder under the temp folder (PLAN.md 8).</summary>
    public const string UninstallTempFolderName = "nth Uninstall";

    /// <summary><c>%LOCALAPPDATA%\nth Setup</c>.</summary>
    public static string Root(string localAppData)
    {
        RequireFull(localAppData, nameof(localAppData));
        return Path.Combine(localAppData, InstallOptions.SetupFolderName);
    }

    /// <summary><c>%LOCALAPPDATA%\nth Setup\runtime</c>.</summary>
    public static string RuntimeRoot(string localAppData) => Path.Combine(Root(localAppData), RuntimeFolderName);

    /// <summary><c>%LOCALAPPDATA%\nth Setup\runtime\&lt;build hash&gt;</c>, where the setup writes its native libraries.</summary>
    public static string RuntimeFolder(string localAppData, string buildHash)
    {
        if (!IsSegment(buildHash)) throw new ArgumentException($"'{buildHash}' is not a build hash that can name a folder.", nameof(buildHash));
        return Path.Combine(RuntimeRoot(localAppData), buildHash);
    }

    /// <summary>The staged Velopack engine: <c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;\nth Setup Engine.exe</c>.</summary>
    public static string EnginePath(string localAppData, string version) => Path.Combine(InstallOptions.StagingFolder(localAppData, version), EngineFileName);

    /// <summary><c>%TEMP%\nth Uninstall</c>: the uninstaller's temporary copy (PLAN.md 8).</summary>
    public static string UninstallTemp(string tempPath)
    {
        RequireFull(tempPath, nameof(tempPath));
        return Path.Combine(tempPath, UninstallTempFolderName);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is the setup's own folder under <paramref name="localAppData"/> or anything inside
    /// it, compared as full paths without case. Used before anything is deleted: nothing outside is ever removed.
    /// </summary>
    public static bool IsInsideRoot(string localAppData, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root(localAppData)));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether <paramref name="text"/> can be one folder name: letters, digits and <c>. - + _</c>, no longer than 128 characters.</summary>
    public static bool IsSegment(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128 || text is "." or "..") return false;
        foreach (var c in text)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' or '_')) return false;
        return true;
    }

    internal static void RequireFull(string path, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, name);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException($"'{path}' is not a full path.", name);
    }
}
