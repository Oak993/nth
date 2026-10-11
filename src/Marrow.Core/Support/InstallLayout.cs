namespace Marrow.Core.Support;

/// <summary>What copying the payload-free uninstaller to the root did (unit U5).</summary>
public enum UninstallerCopyOutcome
{
    /// <summary><c>&lt;root&gt;\nth Uninstall.exe</c> was written from <c>current\nth-Setup.exe</c>.</summary>
    Copied,

    /// <summary>This build carries no <c>current\nth-Setup.exe</c> (a package made before unit U7), so nothing was copied.</summary>
    NoSource,

    /// <summary>The copy or the move failed (see the HRESULT); the previous copy, if any, is unchanged.</summary>
    Failed,
}

/// <summary>The outcome of <see cref="InstallLayout.CopyUninstaller"/>, with the Windows error when it failed.</summary>
public sealed record UninstallerCopyResult(UninstallerCopyOutcome Outcome, int? HResult = null);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8 and 8a, unit U5, ED-NEW-inst-u5-1). The names of a Velopack install
/// of nth and the few file steps on them. SOURCE FACT (Velopack 1.2.161, VELOPACK_CHECK.md checks 2 and 6): the root holds
/// <c>Update.exe</c>, the launch stub <c>nth.exe</c>, <c>packages\</c> and <c>current\</c>; an update swaps <c>current\</c> by
/// rename, so its path never changes. This file is also compiled into src/Marrow.Setup, so it uses nothing else from
/// Marrow.Core.
/// </summary>
public static class InstallLayout
{
    /// <summary>Velopack's package id (ED-235, override 10: it stays <c>NthApp</c>); it names the uninstall key.</summary>
    public const string AppId = "NthApp";

    public const string CurrentFolder = "current";

    public const string PackagesFolder = "packages";

    /// <summary>The main executable after override 8.</summary>
    public const string MainExe = "nth.exe";

    /// <summary>1.0.0's main executable (before override 8); a link to it inside the root has a target that is gone.</summary>
    public const string LegacyMainExe = "Marrow.App.exe";

    public const string UpdateExe = "Update.exe";

    /// <summary>The payload-free nth Setup that the package carries in <c>current\</c> (unit U7).</summary>
    public const string PayloadFreeSetup = "nth-Setup.exe";

    /// <summary>The uninstaller's stable path outside <c>current\</c>; every install and update replaces it (PLAN.md 8).</summary>
    public const string RootUninstaller = "nth Uninstall.exe";

    /// <summary>1.0.0 was packed with <c>--shortcuts StartMenu</c>, so its Start link sits in <c>Programs\nth\</c>.</summary>
    public const string OldStartFolder = "nth";

    public static string CurrentDirectory(string root) => Path.Combine(root, CurrentFolder);

    /// <summary><c>&lt;root&gt;\current\nth.exe</c>: what Velopack's own Start shortcut targets (VELOPACK_CHECK.md check 2).</summary>
    public static string CurrentExe(string root) => Path.Combine(root, CurrentFolder, MainExe);

    public static string UninstallerPath(string root) => Path.Combine(root, RootUninstaller);

    /// <summary>The install root of an app running from <c>&lt;root&gt;\current\</c>: the parent of its base directory.</summary>
    public static string? RootOf(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory)));
    }

    /// <summary>
    /// Whether <paramref name="path"/> (already expanded) is the root itself or anything under it, compared as full paths
    /// without case. A path that cannot be made full is outside.
    /// </summary>
    public static bool IsInside(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full, folder;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        return string.Equals(full, folder, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Copies <c>current\nth-Setup.exe</c> to <c>&lt;root&gt;\nth Uninstall.exe</c> (PLAN.md 8): first to a temporary file
    /// beside it, then moved over the old copy, so the uninstaller is always whole. Never throws.
    /// </summary>
    public static UninstallerCopyResult CopyUninstaller(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var source = Path.Combine(root, CurrentFolder, PayloadFreeSetup);
        if (!File.Exists(source)) return new UninstallerCopyResult(UninstallerCopyOutcome.NoSource);
        var target = UninstallerPath(root);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: true);
            return new UninstallerCopyResult(UninstallerCopyOutcome.Copied);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new UninstallerCopyResult(UninstallerCopyOutcome.Failed, error.HResult);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // already moved, or nothing to undo
        }
    }
}
