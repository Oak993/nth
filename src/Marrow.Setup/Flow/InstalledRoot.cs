using Marrow.Core.Support;
using Microsoft.Win32;

namespace Marrow.Setup.Flow;

/// <summary>Reads the per-user uninstall entry Velopack writes for the package (HKCU\...\Uninstall\&lt;package id&gt;).</summary>
public interface IUninstallKeys
{
    /// <summary>The entry's <c>InstallLocation</c>, or null when there is no entry or no value.</summary>
    string? InstallLocation(string packageId);
}

/// <summary>The real registry, read only. Tests use a fake: no test reads or writes the real HKCU.</summary>
public sealed class RegistryUninstallKeys : IUninstallKeys
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public string? InstallLocation(string packageId)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var entry = Registry.CurrentUser.OpenSubKey(UninstallKey + @"\" + packageId, writable: false);
        return entry?.GetValue("InstallLocation") as string;
    }
}

/// <summary>Where nth is installed, and where it was found.</summary>
/// <param name="Root">The folder holding Update.exe and current\.</param>
/// <param name="Version">The installed version (current\sq.version), or null when nothing readable is installed there.</param>
/// <param name="FromUninstallKey">True when the uninstall entry named the folder.</param>
public sealed record InstalledRoot(string Root, string? Version, bool FromUninstallKey)
{
    /// <summary>
    /// nth installer PLAN.md 3, step 5: the installed root is the uninstall entry's <c>InstallLocation</c> when an install is
    /// there, else <paramref name="legacyRoot"/> (<c>%LOCALAPPDATA%\NthApp</c>, where 1.0.0 lives). An entry that names a
    /// folder with nothing readable in it (a stale entry) is passed over. An <c>--installto</c> given on the command line
    /// is the root as given (Velopack's Setup.exe looks there and nowhere else).
    /// </summary>
    public static InstalledRoot Find(IUninstallKeys keys, string packageId, string legacyRoot, string? installTo)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (installTo is not null) return new InstalledRoot(installTo, InstallerGate.ReadInstalledVersion(installTo), false);
        var location = keys.InstallLocation(packageId)?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(location) && Path.IsPathFullyQualified(location))
        {
            var version = InstallerGate.ReadInstalledVersion(location);
            if (version is not null) return new InstalledRoot(Path.TrimEndingDirectorySeparator(location), version, true);
        }
        return new InstalledRoot(legacyRoot, InstallerGate.ReadInstalledVersion(legacyRoot), false);
    }
}
