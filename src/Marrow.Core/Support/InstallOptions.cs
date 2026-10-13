using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Support;

/// <summary>Which run of Velopack's Setup.exe the install hook is part of (nth installer PLAN.md 8).</summary>
public enum InstallKind
{
    /// <summary>A fresh install: the hook makes the desktop shortcut if the user left it on.</summary>
    Install,

    /// <summary>An update through nth Setup: the hook repairs only and never adds a shortcut.</summary>
    Update,
}

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8 and 8a, unit U0, ED-NEW-inst-u0-1). What nth Setup asks the app's
/// <c>--veloapp-install</c> hook to do, carried in <c>install-options.json</c> rather than through the window, so the
/// shortcut is still right if the window closes, crashes or loses power after Velopack's Setup.exe starts. nth Setup
/// writes it to <c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;\install-options.json</c> and passes that path in
/// <see cref="EnvironmentVariable"/>; the hook reads it (falling back to the same fixed path) and writes
/// <see cref="InstallResult"/> beside it. A fresh install is <c>{"version":1,"kind":"install","desktopShortcut":true}</c>
/// (or <c>false</c>), an update <c>{"version":1,"kind":"update"}</c>. An unknown version, an unknown kind, an install
/// without its boolean or anything that is not this JSON counts as no file, and no file means the hook does nothing.
/// This file is also compiled into src/Marrow.Setup (like InstallerGate.cs), so it uses nothing else from Marrow.Core
/// but InstallerGate's <see cref="InstallerAction"/>, and its JSON goes through a source-generated context (AOT-safe).
/// </summary>
public sealed record InstallOptions
{
    /// <summary>The only format both files have; any other number reads as no file.</summary>
    public const int FormatVersion = 1;

    public const string OptionsFileName = "install-options.json";

    public const string ResultFileName = "install-result.json";

    /// <summary>The variable nth Setup sets for Velopack's Setup.exe; the hook process inherits it (PLAN.md 15, item 10).</summary>
    public const string EnvironmentVariable = "NTH_SETUP_OPTIONS";

    /// <summary>The setup's own folder under %LOCALAPPDATA%: staging, the native runtime and these files (PLAN.md 5).</summary>
    public const string SetupFolderName = "nth Setup";

    /// <summary>nth Setup's switch for a silent install without the desktop shortcut; listed in <c>-h</c> (PLAN.md 8a).</summary>
    public const string NoDesktopShortcutArgument = "--no-desktop-shortcut";

    private InstallOptions(InstallKind kind, bool desktopShortcut)
    {
        Kind = kind;
        DesktopShortcut = desktopShortcut;
    }

    public InstallKind Kind { get; }

    /// <summary>The user's choice for a fresh install. Always false for an update, which never adds a shortcut.</summary>
    public bool DesktopShortcut { get; }

    /// <summary>An update through nth Setup: <c>{"version":1,"kind":"update"}</c>.</summary>
    public static InstallOptions ForUpdate { get; } = new(InstallKind.Update, desktopShortcut: false);

    /// <summary>A fresh install with the desktop shortcut on (the default) or off.</summary>
    public static InstallOptions ForInstall(bool desktopShortcut) => new(InstallKind.Install, desktopShortcut);

    /// <summary>
    /// The options for what the gate decided. Only an install or an upgrade runs Setup.exe; the up-to-date and
    /// newer-installed outcomes run nothing, so they have no options file and asking for one is a bug.
    /// </summary>
    public static InstallOptions ForAction(InstallerAction action, bool desktopShortcut) => action switch
    {
        InstallerAction.Install => ForInstall(desktopShortcut),
        InstallerAction.Upgrade => ForUpdate,
        InstallerAction.AlreadyUpToDate => throw new ArgumentOutOfRangeException(nameof(action), action, "Nothing runs when nth is up to date."),
        InstallerAction.RefuseNewerInstalled => throw new ArgumentOutOfRangeException(nameof(action), action, "Nothing runs when a newer nth is installed."),
    };

    /// <summary>
    /// The desktop-shortcut choice a command line makes before anything is shown: on (the default, also for
    /// <c>--silent</c>) unless <see cref="NoDesktopShortcutArgument"/> appears before a <c>--</c>. The window then lets the
    /// user change it on Welcome and in Options.
    /// </summary>
    public static bool DesktopShortcutChoice(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        foreach (var argument in arguments)
        {
            if (argument == "--") break;
            if (string.Equals(argument, NoDesktopShortcutArgument, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>The setup's folder for this version: <c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;</c>.</summary>
    public static string StagingFolder(string localAppData, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        if (!IsPathSegment(version)) throw new ArgumentException($"'{version}' is not a version that can name a folder.", nameof(version));
        return Path.Combine(localAppData, SetupFolderName, version);
    }

    /// <summary>The fixed options path, used by the setup and by a hook that did not inherit the variable.</summary>
    public static string DefaultOptionsPath(string localAppData, string version) => Path.Combine(StagingFolder(localAppData, version), OptionsFileName);

    /// <summary>
    /// The options file the hook reads: <paramref name="environmentValue"/> (the inherited <see cref="EnvironmentVariable"/>)
    /// when it is a full path to a file named <see cref="OptionsFileName"/>, otherwise the fixed path for this version.
    /// </summary>
    public static string OptionsPathFor(string? environmentValue, string localAppData, string version)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue)
            && Path.IsPathFullyQualified(environmentValue)
            && string.Equals(Path.GetFileName(environmentValue), OptionsFileName, StringComparison.OrdinalIgnoreCase))
            return environmentValue;
        return DefaultOptionsPath(localAppData, version);
    }

    /// <summary>The result file is always beside the options file.</summary>
    public static string ResultPathFor(string optionsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionsPath);
        var folder = Path.GetDirectoryName(Path.GetFullPath(optionsPath)) ?? throw new ArgumentException("The options file has no folder.", nameof(optionsPath));
        return Path.Combine(folder, ResultFileName);
    }

    /// <summary>Exactly what nth Setup writes: <c>{"version":1,"kind":"install","desktopShortcut":true}</c> or <c>{"version":1,"kind":"update"}</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(
        new InstallOptionsJson
        {
            Version = FormatVersion,
            Kind = Kind == InstallKind.Install ? "install" : "update",
            DesktopShortcut = Kind == InstallKind.Install ? DesktopShortcut : null,
        },
        InstallFilesJsonContext.Default.InstallOptionsJson);

    /// <summary>The options in <paramref name="json"/>, or null when it is not a version-1 options document.</summary>
    public static InstallOptions? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>The options in these UTF-8 bytes (a byte-order mark is allowed), or null when they are not a version-1 options document.</summary>
    public static InstallOptions? Parse(ReadOnlySpan<byte> utf8)
    {
        InstallOptionsJson? document;
        try
        {
            document = JsonSerializer.Deserialize(InstallFiles.WithoutByteOrderMark(utf8), InstallFilesJsonContext.Default.InstallOptionsJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (document is null || document.Version != FormatVersion) return null;
        return document.Kind switch
        {
            "install" when document.DesktopShortcut is { } desktopShortcut => ForInstall(desktopShortcut),
            "update" => ForUpdate,
            _ => null,
        };
    }

    /// <summary>Writes the file whole or not at all (a temporary file moved over the old one), creating its folder.</summary>
    public void Write(string path) => InstallFiles.WriteAtomically(path, ToJson());

    /// <summary>The options file at <paramref name="path"/>, or null when it is missing, unreadable, too large or not a version-1 options document.</summary>
    public static InstallOptions? Read(string path) => InstallFiles.ReadSmall(path) is { } bytes ? Parse(bytes) : null;

    private static bool IsPathSegment(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128 || text is "." or "..") return false;
        foreach (var c in text)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' or '_')) return false;
        return true;
    }
}

/// <summary>What the install hook did about the desktop shortcut (<c>install-result.json</c>'s <c>desktopShortcut</c>).</summary>
public enum DesktopShortcutOutcome
{
    /// <summary><c>created</c>: the hook made <c>nth.lnk</c>.</summary>
    Created,

    /// <summary><c>kept</c>: an <c>nth.lnk</c> into the install root was already there (and was repaired if its target was gone).</summary>
    Kept,

    /// <summary><c>off</c>: the user turned the shortcut off; nothing was made.</summary>
    Off,

    /// <summary><c>name-taken</c>: a different <c>nth.lnk</c> (target outside the root) is on the desktop; it was left untouched.</summary>
    NameTaken,

    /// <summary><c>failed</c>: Windows refused (with <see cref="InstallResult.HResult"/>), or the hook left no readable result.</summary>
    Failed,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, unit U0, ED-NEW-inst-u0-1). What the install hook reports in
/// <c>install-result.json</c> beside the options file:
/// <c>{"version":1,"desktopShortcut":"created"|"kept"|"off"|"name-taken"|"failed","hresult":"0x80070005"?,"iconsHidden":bool,"scanCapped":bool,"startAppIdRestamped":bool}</c>.
/// Ready shows this outcome, never the user's choice. A missing, unknown-version or malformed file reads as
/// <see cref="Missing"/> (<c>failed</c>, the hook was killed), so Ready never claims a shortcut it did not see.
/// Missing booleans read as false; <c>hresult</c> is written only when known.
/// </summary>
/// <param name="DesktopShortcut">What happened to the desktop shortcut.</param>
/// <param name="HResult">The Windows error behind <see cref="DesktopShortcutOutcome.Failed"/>, when there was one.</param>
/// <param name="IconsHidden">Explorer's "Show desktop icons" was off (<c>SHGetSetSettings(SSF_HIDEICONS)</c>).</param>
/// <param name="ScanCapped">A desktop scan stopped at its cap (2,000 links or 3 s); nth's self-heal finishes it.</param>
/// <param name="StartAppIdRestamped">The Start shortcut's AppUserModelID was re-stamped to nth's.</param>
public sealed record InstallResult(
    DesktopShortcutOutcome DesktopShortcut,
    int? HResult = null,
    bool IconsHidden = false,
    bool ScanCapped = false,
    bool StartAppIdRestamped = false)
{
    public const int FormatVersion = InstallOptions.FormatVersion;

    /// <summary>What Ready reads when the hook left no readable result: failed, with no HRESULT.</summary>
    public static InstallResult Missing { get; } = new(DesktopShortcutOutcome.Failed);

    /// <summary>A refused shortcut with the HRESULT Windows gave.</summary>
    public static InstallResult FailedWith(int hresult) => new(DesktopShortcutOutcome.Failed, hresult);

    /// <summary><c>0x80070005</c>: eight upper-case hex digits, the form the file and the <c>failed:&lt;HRESULT&gt;</c> notes use.</summary>
    public static string FormatHResult(int hresult) => "0x" + hresult.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Reads <c>0x</c> and one to eight hex digits (either case) back into an HRESULT.</summary>
    public static bool TryParseHResult(string? text, out int hresult)
    {
        hresult = 0;
        if (text is null || text.Length is < 3 or > 10 || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
        if (!uint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
        hresult = unchecked((int)value);
        return true;
    }

    /// <summary>The outcome's word in the file: <c>created</c>, <c>kept</c>, <c>off</c>, <c>name-taken</c> or <c>failed</c>.</summary>
    public static string OutcomeName(DesktopShortcutOutcome outcome) => outcome switch
    {
        DesktopShortcutOutcome.Created => "created",
        DesktopShortcutOutcome.Kept => "kept",
        DesktopShortcutOutcome.Off => "off",
        DesktopShortcutOutcome.NameTaken => "name-taken",
        DesktopShortcutOutcome.Failed => "failed",
    };

    /// <summary>The outcome a word in the file names (exact, lower case), or null for any other word.</summary>
    public static DesktopShortcutOutcome? ParseOutcome(string? name) => name switch
    {
        "created" => DesktopShortcutOutcome.Created,
        "kept" => DesktopShortcutOutcome.Kept,
        "off" => DesktopShortcutOutcome.Off,
        "name-taken" => DesktopShortcutOutcome.NameTaken,
        "failed" => DesktopShortcutOutcome.Failed,
        _ => null,
    };

    /// <summary>Exactly what the hook writes, in the field order of PLAN.md 8a.</summary>
    public string ToJson() => JsonSerializer.Serialize(
        new InstallResultJson
        {
            Version = FormatVersion,
            DesktopShortcut = OutcomeName(DesktopShortcut),
            HResult = HResult is { } hresult ? FormatHResult(hresult) : null,
            IconsHidden = IconsHidden,
            ScanCapped = ScanCapped,
            StartAppIdRestamped = StartAppIdRestamped,
        },
        InstallFilesJsonContext.Default.InstallResultJson);

    /// <summary>The result in <paramref name="json"/>, or null when it is not a version-1 result document.</summary>
    public static InstallResult? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>The result in these UTF-8 bytes (a byte-order mark is allowed), or null when they are not a version-1 result document.</summary>
    public static InstallResult? Parse(ReadOnlySpan<byte> utf8)
    {
        InstallResultJson? document;
        try
        {
            document = JsonSerializer.Deserialize(InstallFiles.WithoutByteOrderMark(utf8), InstallFilesJsonContext.Default.InstallResultJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (document is null || document.Version != FormatVersion || ParseOutcome(document.DesktopShortcut) is not { } outcome) return null;
        int? hresult = null;
        if (document.HResult is not null)
        {
            if (!TryParseHResult(document.HResult, out var parsed)) return null;
            hresult = parsed;
        }
        return new InstallResult(outcome, hresult, document.IconsHidden ?? false, document.ScanCapped ?? false, document.StartAppIdRestamped ?? false);
    }

    /// <summary>Writes the file whole or not at all (a temporary file moved over the old one), creating its folder.</summary>
    public void Write(string path) => InstallFiles.WriteAtomically(path, ToJson());

    /// <summary>The result file at <paramref name="path"/>, or null when it is missing, unreadable, too large or not a version-1 result.</summary>
    public static InstallResult? Read(string path) => InstallFiles.ReadSmall(path) is { } bytes ? Parse(bytes) : null;

    /// <summary>What Ready shows: the file's result, or <see cref="Missing"/> (failed) when there is no readable one.</summary>
    public static InstallResult ReadForReady(string path) => Read(path) ?? Missing;
}

/// <summary>The on-disk shape of install-options.json (field order is the file's order).</summary>
internal sealed class InstallOptionsJson
{
    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("desktopShortcut")]
    public bool? DesktopShortcut { get; set; }
}

/// <summary>The on-disk shape of install-result.json (field order is the file's order).</summary>
internal sealed class InstallResultJson
{
    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("desktopShortcut")]
    public string? DesktopShortcut { get; set; }

    [JsonPropertyName("hresult")]
    public string? HResult { get; set; }

    [JsonPropertyName("iconsHidden")]
    public bool? IconsHidden { get; set; }

    [JsonPropertyName("scanCapped")]
    public bool? ScanCapped { get; set; }

    [JsonPropertyName("startAppIdRestamped")]
    public bool? StartAppIdRestamped { get; set; }
}

/// <summary>Source-generated, so the trimmed AOT setup and the app read and write the same bytes without reflection.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InstallOptionsJson))]
[JsonSerializable(typeof(InstallResultJson))]
internal sealed partial class InstallFilesJsonContext : JsonSerializerContext
{
}

/// <summary>Small, whole-file reads and writes for the two install files.</summary>
internal static class InstallFiles
{
    /// <summary>Both files are tens of bytes; anything over this is not ours and reads as no file.</summary>
    public const int MaxBytes = 64 * 1024;

    public static ReadOnlySpan<byte> WithoutByteOrderMark(ReadOnlySpan<byte> utf8) =>
        utf8.StartsWith("\uFEFF"u8) ? utf8[3..] : utf8;

    public static byte[]? ReadSmall(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > MaxBytes) return null;
            var bytes = new byte[file.Length];
            file.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public static void WriteAtomically(string path, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full) ?? throw new ArgumentException("The file has no folder.", nameof(path));
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json));
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // already moved, or nothing to undo
        }
    }
}
