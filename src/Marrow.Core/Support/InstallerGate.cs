using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Marrow.Core.Support;

/// <summary>What the Marrow installer does next to what is already installed (OD-0072, ED-247).</summary>
public enum InstallerAction
{
    /// <summary>Nothing readable is installed: Velopack's Setup.exe installs (or offers its own repair prompt).</summary>
    Install,

    /// <summary>An older version is installed: Velopack's Setup.exe replaces it in place; the data folder is untouched.</summary>
    Upgrade,

    /// <summary>The same version is installed: the user is told so and nothing runs.</summary>
    AlreadyUpToDate,

    /// <summary>A newer version is installed: the installer refuses and changes nothing.</summary>
    RefuseNewerInstalled,
}

/// <summary>The installer the gate is part of: its product title, its version and where Velopack installs it.</summary>
/// <param name="Title">The package title ("Marrow"), as vpk pack's --packTitle.</param>
/// <param name="Version">The package version, as vpk pack's --packVersion.</param>
/// <param name="DefaultInstallRoot">%LOCALAPPDATA%\&lt;package id&gt;, Velopack's default install folder.</param>
/// <param name="LaunchDirectory">The folder of the installer file, against which Setup.exe resolves a relative --installto.</param>
public sealed record InstallerIdentity(string Title, string Version, string DefaultInstallRoot, string LaunchDirectory);

/// <summary>The Win32 side of the gate: a message box and the embedded Velopack Setup.exe.</summary>
public interface IInstallerGateHost
{
    void ShowMessage(string caption, string text, bool warning);

    /// <summary>Runs Velopack's Setup.exe with these arguments, waits for it, and returns its exit code.</summary>
    int RunVelopackSetup(IReadOnlyList<string> arguments);
}

/// <summary>
/// PORT-ONLY (no Swift twin; OD-0072, ED-247). The check the Marrow Setup bootstrapper (src/Marrow.Setup) runs before it
/// hands over to Velopack's Setup.exe. Velopack 1.2.161's Setup.exe has no pre-install check: next to an existing install
/// it shows its own dialog that offers Update, Repair (same version) or Downgrade (newer installed) and then moves the old
/// folder aside, and the app's --veloapp-install hook runs only after that and cannot cancel (ED-247). So the decision is
/// made here, from the same files Setup.exe reads (&lt;root&gt;\Update.exe and &lt;root&gt;\current\sq.version), with the
/// same strict SemVer reading. This file is also compiled into src/Marrow.Setup, so it uses nothing else from Marrow.Core.
/// </summary>
public static class InstallerGate
{
    /// <summary>The exit code of a refused installer (Velopack's Setup.exe exits 1 on any failure).</summary>
    public const int RefusedExitCode = 1;

    // The silent exit codes below are the contract units U3 and U4 build on (nth installer PLAN.md 3a, 13; ED-NEW-inst-u0-1),
    // taken with their names and meanings from the unmerged unit/installer-close (e1fd0019c) as VELOPACK_CHECK.md asks.
    // Nothing returns them yet: the logic that does arrives with the running-nth handshake and the install hold.

    /// <summary>The exit code when the user cancels closing a running nth: nothing was changed and nothing was closed.</summary>
    public const int CancelledExitCode = 2;

    /// <summary>The exit code when a silent setup's request to close a running nth was not answered in time, or was not allowed: nothing was changed.</summary>
    public const int NotClosedExitCode = 3;

    /// <summary>The exit code when a silent setup found another installation of nth in progress: nothing was asked, run or changed.</summary>
    public const int InProgressExitCode = 4;

    public static string Caption(string title) => title + " Setup"; // Velopack's title-setup, locales/en-US.ftl

    public static string UpToDateMessage(string title, string version) => $"{title} {version} is already installed and up to date.";

    public static string NewerInstalledMessage(string title, string installedVersion) => $"A newer version of {title} ({installedVersion}) is already installed.";

    /// <summary>
    /// The decision. <paramref name="installedVersion"/> is the installed manifest's version, or null when nothing is
    /// installed; one that is not a SemVer version counts as nothing readable (Setup.exe then offers its repair).
    /// </summary>
    public static InstallerAction Decide(string? installedVersion, string installerVersion)
    {
        var installer = Parse(installerVersion);
        if (installedVersion is null || !TryParse(installedVersion, out var installed)) return InstallerAction.Install;
        var order = Compare(installed, installer);
        return order < 0 ? InstallerAction.Upgrade : order == 0 ? InstallerAction.AlreadyUpToDate : InstallerAction.RefuseNewerInstalled;
    }

    /// <summary>SemVer 2.0.0 precedence (section 11): negative when <paramref name="a"/> is older. Build metadata is ignored.</summary>
    public static int CompareVersions(string a, string b) => Compare(Parse(a), Parse(b));

    /// <summary>
    /// The version installed in <paramref name="installRoot"/>, as Velopack's Setup.exe finds it: null when the folder is
    /// missing, empty or marked dead (.dead), when Update.exe or current\sq.version is missing, or when the manifest has
    /// no SemVer version.
    /// </summary>
    public static string? ReadInstalledVersion(string installRoot)
    {
        try
        {
            if (!Directory.Exists(installRoot) || !Directory.EnumerateFileSystemEntries(installRoot).Any()) return null;
            if (File.Exists(Path.Combine(installRoot, ".dead"))) return null;
            if (!File.Exists(Path.Combine(installRoot, "Update.exe"))) return null;
            var manifest = Path.Combine(installRoot, "current", "sq.version");
            if (!File.Exists(manifest)) return null;
            var version = XDocument.Parse(File.ReadAllText(manifest)).Descendants()
                .LastOrDefault(element => element.Name.LocalName == "version")?.Value.Trim();
            return version is not null && TryParse(version, out _) ? version : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    /// <summary>Whether Setup.exe would run silently (--silent or -s, before the -- separator): then no message is shown.</summary>
    public static bool IsSilent(IReadOnlyList<string> arguments) => ReadArguments(arguments, Environment.CurrentDirectory).Silent;

    /// <summary>
    /// The gate: reads what is installed where Setup.exe would install, decides, and either tells the user (same or newer
    /// version installed; nothing runs and nothing is written) or hands every argument to Setup.exe and returns its exit
    /// code. Help goes straight to Setup.exe. Otherwise throws FormatException, before anything runs, when the
    /// installer's own version is not SemVer (Decide reads it first).
    /// </summary>
    public static int Run(IInstallerGateHost host, InstallerIdentity identity, IReadOnlyList<string> arguments)
    {
        var read = ReadArguments(arguments, identity.LaunchDirectory);
        if (read.Help) return host.RunVelopackSetup(arguments);

        var installed = ReadInstalledVersion(read.InstallTo ?? identity.DefaultInstallRoot);
        switch (Decide(installed, identity.Version))
        {
            case InstallerAction.Install:
            case InstallerAction.Upgrade:
                return host.RunVelopackSetup(read.Forwarded);
            case InstallerAction.AlreadyUpToDate:
                if (!read.Silent) host.ShowMessage(Caption(identity.Title), UpToDateMessage(identity.Title, identity.Version), warning: false);
                return 0;
            case InstallerAction.RefuseNewerInstalled:
                if (!read.Silent) host.ShowMessage(Caption(identity.Title), NewerInstalledMessage(identity.Title, installed!), warning: true);
                return RefusedExitCode;
        }
        throw new InvalidOperationException("Unknown installer action.");
    }

    private readonly record struct SetupArguments(bool Silent, bool Help, string? InstallTo, IReadOnlyList<string> Forwarded);

    // Velopack 1.2.161's Setup.exe arguments (src/bins/src/setup.rs, clap): -s/--silent, -v/--verbose, -l/--log <FILE>,
    // -t/--installto <DIR>, -h/--help, and the app's arguments after "--". Short flags may be clustered (-sv) and a value
    // may follow its option in the same token (-tDIR, -t=DIR, --installto=DIR). A relative --installto is resolved against
    // the installer's folder (Setup.exe changes to its own folder first) and passed on whole, as --installto <full path>.
    private static SetupArguments ReadArguments(IReadOnlyList<string> arguments, string launchDirectory)
    {
        bool silent = false, help = false;
        string? installTo = null;
        var forwarded = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (token == "--")
            {
                forwarded.AddRange(arguments.Skip(i));
                break;
            }
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var equals = token.IndexOf('=', StringComparison.Ordinal);
                var name = equals < 0 ? token : token[..equals];
                switch (name)
                {
                    case "--installto":
                        installTo = equals >= 0 ? token[(equals + 1)..] : i + 1 < arguments.Count ? arguments[++i] : null;
                        continue;
                    case "--log":
                        forwarded.Add(token);
                        if (equals < 0 && i + 1 < arguments.Count) forwarded.Add(arguments[++i]);
                        continue;
                    case "--silent":
                        silent = true;
                        break;
                    case "--help":
                        help = true;
                        break;
                }
                forwarded.Add(token);
                continue;
            }
            if (token.Length > 1 && token[0] == '-')
            {
                var flags = "-";
                string? log = null;
                for (var at = 1; at < token.Length; at++)
                {
                    var flag = token[at];
                    if (flag is 't' or 'l')
                    {
                        var rest = token[(at + 1)..];
                        if (rest.StartsWith('=')) rest = rest[1..];
                        var value = rest.Length > 0 ? rest : i + 1 < arguments.Count ? arguments[++i] : null;
                        if (flag == 't') installTo = value;
                        else log = value;
                        break;
                    }
                    if (flag == 's') silent = true;
                    if (flag == 'h') help = true;
                    flags += flag;
                }
                if (flags.Length > 1) forwarded.Add(flags);
                if (log is not null) forwarded.AddRange(["-l", log]);
                continue;
            }
            forwarded.Add(token);
        }
        if (installTo is not null)
        {
            installTo = Path.GetFullPath(installTo, launchDirectory);
            forwarded.InsertRange(0, ["--installto", installTo]);
        }
        return new SetupArguments(silent, help, installTo, forwarded);
    }

    private readonly record struct SemVer(ulong Major, ulong Minor, ulong Patch, string[] Prerelease);

    private static SemVer Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a SemVer version.");

    // Strict SemVer 2.0.0, as the semver crate Velopack's Setup.exe reads sq.version with: MAJOR.MINOR.PATCH, numbers
    // without leading zeros, an optional -pre-release and +build of dot-separated [0-9A-Za-z-] identifiers.
    private static bool TryParse(string text, out SemVer version)
    {
        version = default;
        var core = text.Trim();
        var plus = core.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            if (!Identifiers(core[(plus + 1)..], numericLeadingZero: true, out _)) return false;
            core = core[..plus];
        }
        string[] prerelease = [];
        var dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            if (!Identifiers(core[(dash + 1)..], numericLeadingZero: false, out prerelease)) return false;
            core = core[..dash];
        }
        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        var numbers = new ulong[3];
        for (var i = 0; i < 3; i++)
            if (!Numeric(parts[i]) || !ulong.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        version = new SemVer(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    private static bool Identifiers(string text, bool numericLeadingZero, out string[] identifiers)
    {
        identifiers = text.Split('.');
        foreach (var identifier in identifiers)
        {
            if (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
            if (!numericLeadingZero && identifier.All(char.IsAsciiDigit) && !Numeric(identifier)) return false;
        }
        return true;
    }

    private static bool Numeric(string part) => part.Length > 0 && part.All(char.IsAsciiDigit) && (part.Length == 1 || part[0] != '0');

    private static int Compare(SemVer a, SemVer b)
    {
        var order = a.Major.CompareTo(b.Major);
        if (order == 0) order = a.Minor.CompareTo(b.Minor);
        if (order == 0) order = a.Patch.CompareTo(b.Patch);
        if (order != 0) return order;
        // a version with a pre-release has lower precedence than the same version without one
        if (a.Prerelease.Length == 0 && b.Prerelease.Length == 0) return 0;
        if (a.Prerelease.Length == 0) return 1;
        if (b.Prerelease.Length == 0) return -1;
        for (var i = 0; i < Math.Min(a.Prerelease.Length, b.Prerelease.Length); i++)
        {
            var x = a.Prerelease[i];
            var y = b.Prerelease[i];
            var xNumeric = x.All(char.IsAsciiDigit);
            var yNumeric = y.All(char.IsAsciiDigit);
            order = (xNumeric, yNumeric) switch
            {
                (true, true) => x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y),
                (true, false) => -1,
                (false, true) => 1,
                (false, false) => string.CompareOrdinal(x, y),
            };
            if (order != 0) return Math.Sign(order);
        }
        return a.Prerelease.Length.CompareTo(b.Prerelease.Length);
    }
}
