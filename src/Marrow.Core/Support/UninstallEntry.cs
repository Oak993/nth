namespace Marrow.Core.Support;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8, unit U5, ED-NEW-inst-u5-1). The part of the current user's registry hive the
/// installer steps touch, so they run on a test hive (or an in-memory fake) and never on the real <c>HKCU</c> in a test.
/// Production: <c>src/Marrow.App/Port/RegistryUserHive.cs</c> over <c>Registry.CurrentUser</c>, constructed only in the
/// app's and the setup's <c>Program.cs</c>. Values are a <see cref="string"/> (<c>REG_SZ</c>, or <c>REG_EXPAND_SZ</c> read
/// unexpanded) or an <see cref="int"/> (<c>REG_DWORD</c>). No member creates a key.
/// </summary>
public interface IUserRegistry
{
    bool KeyExists(string subKey);

    /// <summary>
    /// Creates the key (and any missing parent) when it does not exist; an existing key is left as it is. Added for unit U8
    /// (ED-NEW-inst-u8-1): the sign-in entry goes in <c>...\CurrentVersion\Run</c>, which a profile may not have yet. The
    /// uninstall key is never made this way.
    /// </summary>
    void CreateKey(string subKey);

    /// <summary>The value, or null when the key or the value does not exist.</summary>
    object? GetValue(string subKey, string name);

    /// <summary>Sets a <c>REG_SZ</c> value in an existing key (throws when the key does not exist).</summary>
    void SetString(string subKey, string name, string value);

    /// <summary>Sets a <c>REG_DWORD</c> value in an existing key (throws when the key does not exist).</summary>
    void SetDword(string subKey, string name, int value);

    /// <summary>Deletes a value; a missing key or value is not an error.</summary>
    void DeleteValue(string subKey, string name);
}

/// <summary>What re-pointing the uninstall entry found and did.</summary>
/// <param name="KeyFound">Velopack's key <c>Uninstall\NthApp</c> exists.</param>
/// <param name="Ours">Its <c>InstallLocation</c> is this root (nothing is written otherwise).</param>
/// <param name="Writes">Values written because they differed (zero when everything was already right).</param>
/// <param name="UninstallerPresent"><c>&lt;root&gt;\nth Uninstall.exe</c> exists, so the uninstall strings point at it.</param>
public sealed record UninstallRepointResult(bool KeyFound, bool Ours, int Writes, bool UninstallerPresent);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8, unit U5, ED-NEW-inst-u5-1). Keeps Velopack's Apps-list entry
/// pointing at nth's own uninstaller. SOURCE FACT (Velopack 1.2.161, VELOPACK_CHECK.md checks 5, 8 and 15;
/// registry.rs:9-57): Setup.exe writes <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\NthApp</c> after the
/// install hook, and Update.exe rewrites it on every in-app update before the after-update hook, with
/// <c>UninstallString</c> and <c>QuietUninstallString</c> = <c>"&lt;root&gt;\Update.exe" --uninstall [--silent]</c>,
/// <c>EstimatedSize</c> = the size of <c>current\</c> only, <c>InstallLocation</c> = the root, <c>NoModify</c> and
/// <c>NoRepair</c> = 1. So three places re-point it, each writing only what differs: nth Setup after Setup.exe exits 0, the
/// after-update hook, and nth's start-time self-heal. The uninstall strings move to <c>"&lt;root&gt;\nth Uninstall.exe"
/// --uninstall [--silent]</c> only while that file exists (a package without nth Setup keeps Velopack's working strings).
/// This file is also compiled into src/Marrow.Setup.
/// </summary>
public static class UninstallEntry
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + InstallLayout.AppId;

    /// <summary>The per-user Run key; nth's sign-in entry is its value <see cref="SignInValueName"/> (unit U8 writes it).</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string SignInValueName = "nth";

    /// <summary>
    /// ED-NEW-secfinal-24: Dextra's "Start with Windows (hidden)" entry, its own Run value. It used to share <see cref="SignInValueName"/> with
    /// "Open at sign-in", so each overwrote or deleted the other.
    /// </summary>
    public const string DextraStartValueName = "nth Dextra";

    /// <summary>Windows' approval record for Run values; kept here because the uninstaller compiles this file without SignInEntry.</summary>
    public const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static string UninstallCommand(string root) => "\"" + InstallLayout.UninstallerPath(root) + "\" --uninstall";

    public static string QuietUninstallCommand(string root) => UninstallCommand(root) + " --silent";

    /// <summary>
    /// The values nth wants: the two uninstall strings (only when the uninstaller exists), <c>NoModify</c> and <c>NoRepair</c>
    /// = 1, and <c>EstimatedSize</c> in KB when it is known. <c>URLInfoAbout</c> and <c>HelpLink</c> wait for the owner's
    /// site (PLAN.md 8); none is written.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object>> Expected(string root, long? estimatedSizeKb, bool uninstallerPresent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var values = new List<KeyValuePair<string, object>>();
        if (uninstallerPresent)
        {
            values.Add(new("UninstallString", UninstallCommand(root)));
            values.Add(new("QuietUninstallString", QuietUninstallCommand(root)));
        }
        values.Add(new("NoModify", 1));
        values.Add(new("NoRepair", 1));
        if (estimatedSizeKb is { } size) values.Add(new("EstimatedSize", (int)Math.Clamp(size, 0, int.MaxValue)));
        return values;
    }

    /// <summary>
    /// Rewrites the values of <see cref="Expected"/> that differ, in Velopack's key for this root only: nothing is written
    /// when the key is missing (not installed, or already uninstalled) or its <c>InstallLocation</c> is another folder. Never
    /// creates the key.
    /// </summary>
    public static UninstallRepointResult Repoint(IUserRegistry registry, string root, long? estimatedSizeKb, bool uninstallerPresent)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!registry.KeyExists(KeyPath)) return new UninstallRepointResult(KeyFound: false, Ours: false, 0, uninstallerPresent);
        if (registry.GetValue(KeyPath, "InstallLocation") is not string location || !SameFolder(location, root))
            return new UninstallRepointResult(KeyFound: true, Ours: false, 0, uninstallerPresent);
        var writes = 0;
        foreach (var (name, value) in Expected(root, estimatedSizeKb, uninstallerPresent))
        {
            if (Equals(registry.GetValue(KeyPath, name), value)) continue;
            if (value is string text) registry.SetString(KeyPath, name, text);
            else registry.SetDword(KeyPath, name, (int)value);
            writes++;
        }
        return new UninstallRepointResult(KeyFound: true, Ours: true, writes, uninstallerPresent);
    }

    /// <summary>
    /// nth's footprint in KB with <c>T_disk</c>'s formula (PLAN.md 4: the full package + the app + Update.exe), measured on
    /// disk: the newest <c>packages\*-full.nupkg</c>, every file under <c>current\</c> and <c>Update.exe</c>. Null when the
    /// root has no <c>current\</c> or cannot be read. Measured, not stamped, so the setup and nth's self-heal agree on the
    /// same machine and never rewrite each other's value.
    /// </summary>
    public static long? MeasureFootprintKb(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        try
        {
            var current = new DirectoryInfo(InstallLayout.CurrentDirectory(root));
            if (!current.Exists) return null;
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            long bytes = 0;
            foreach (var file in current.EnumerateFiles("*", options)) bytes += file.Length;
            var packages = new DirectoryInfo(Path.Combine(root, InstallLayout.PackagesFolder));
            if (packages.Exists && packages.EnumerateFiles("*-full.nupkg").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() is { } package)
                bytes += package.Length;
            var update = new FileInfo(Path.Combine(root, InstallLayout.UpdateExe));
            if (update.Exists) bytes += update.Length;
            return bytes / 1024;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes nth's sign-in entry (the Run value <c>nth</c>) and its Windows approval record when the program it starts is inside the root (PLAN.md 8: the
    /// app's before-uninstall hook does it in case Velopack's uninstaller is run directly). An entry that starts anything
    /// else is left. Returns whether it was removed.
    /// </summary>
    public static bool RemoveSignInEntry(IUserRegistry registry, string root)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        // ED-NEW-secfinal-24: nth has two Run values, the sign-in switch's and Dextra's own; each goes only when it starts a program inside the root.
        var removed = RemoveOwnedRunValue(registry, root, SignInValueName);
        return RemoveOwnedRunValue(registry, root, DextraStartValueName) | removed;
    }

    private static bool RemoveOwnedRunValue(IUserRegistry registry, string root, string name)
    {
        if (registry.GetValue(RunKeyPath, name) is not string command) return false;
        if (CommandExecutable(command) is not { } program || !InstallLayout.IsInside(root, Environment.ExpandEnvironmentVariables(program))) return false;
        // C4b U8-1, ED-NEW-inst-cx-10: clear the approval first, retaining the ownership proof if that write is refused.
        registry.DeleteValue(StartupApprovedRunKeyPath, name);
        registry.DeleteValue(RunKeyPath, name);
        return true;
    }

    /// <summary>The program a command line starts: the quoted first token, or the text up to the first space.</summary>
    public static string? CommandExecutable(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var text = command.Trim();
        if (text.Length == 0) return null;
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }

    private static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
