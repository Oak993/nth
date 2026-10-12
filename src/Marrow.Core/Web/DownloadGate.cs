// PORT-ONLY: web surfaces W8b (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.4 "Every download asks. DownloadStarting is held
// and nth shows a glass prompt under the toolbar: file name, size, type, source site. Buttons: Save (to the Downloads folder),
// Save as…, Cancel. Esc cancels. No button has focus; Enter does nothing", "Risky types (.exe .msi .msix .appx .bat .cmd .ps1
// .vbs .js .jar .scr .lnk .reg .hta .dll, and archives that contain them when the server says so) add 'This file can run code
// on your PC.' and Save needs a second click within 3 s", "Files are never opened automatically", "Model-started downloads
// always wait for the user in the same prompt. The model cannot accept"; the canvas's #dl2 ("12 KB · CSV · from
// localhost:5173", "84 MB · Application · from downloads.example.com", "Click again to save", "Saved to Downloads. Never
// opened by itself."); ED-NEW-web-w8b-2). No Swift counterpart. The rules of the download prompt, with no pixel and no engine.
using System.Globalization;

namespace Marrow.Core.Web;

/// <summary>DESIGN 5.4: what the download prompt shows and where a saved file goes.</summary>
public static class DownloadGate
{
    /// <summary>How long the first click on a risky file's Save stays armed (5.4 "a second click within 3 s").</summary>
    public static readonly TimeSpan SecondClickWindow = TimeSpan.FromSeconds(3);

    /// <summary>The risky file's warning, word for word.</summary>
    public const string RiskyWarning = "This file can run code on your PC.";

    /// <summary>Save's label while armed (the canvas's words).</summary>
    public const string ArmedLabel = "Click again to save";

    public const string SaveLabel = "Save";

    public const string SaveAsLabel = "Save as…";

    public const string CancelLabel = "Cancel";

    /// <summary>The prompt's foot (the canvas's #dl2 .foot).</summary>
    public const string Foot = "Saved to Downloads. Never opened by itself.";

    /// <summary>What a file with no usable name is called.</summary>
    public const string FallbackName = "download";

    /// <summary>The longest file name nth writes (Windows allows 255 per component; room is left for " (99)").</summary>
    public const int MaxNameLength = 200;

    /// <summary>
    /// 5.4's list, plus (stricter, ED-NEW-web-w8b-2) the other Windows types that run code or open a program when opened: .com
    /// .pif .cpl .msc .msp .mst .wsf .wsh .vbe .jse .psm1 .psd1 .ps1xml .appxbundle .msixbundle .application .appref-ms .url
    /// .settingcontent-ms .xll .gadget .inf .scf .chm, and the disk images that mount with programs inside (.iso .img .vhd .vhdx).
    /// </summary>
    public static IReadOnlySet<string> RiskyExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "msi", "msix", "appx", "bat", "cmd", "ps1", "vbs", "js", "jar", "scr", "lnk", "reg", "hta", "dll",
        "com", "pif", "cpl", "msc", "msp", "mst", "wsf", "wsh", "vbe", "jse", "psm1", "psd1", "ps1xml", "appxbundle",
        "msixbundle", "application", "appref-ms", "url", "settingcontent-ms", "xll", "gadget", "inf", "scf", "chm",
        "iso", "img", "vhd", "vhdx",
    };

    /// <summary>Archive types that may carry a risky file (5.4 "archives that contain them").</summary>
    public static IReadOnlySet<string> ArchiveExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "xz", "cab", "arj", "lzh", "z",
    };

    /// <summary>
    /// Content types a server uses for programs and scripts: an archive (or any file) served as one of these is risky (5.4 "when
    /// the server says so").
    /// </summary>
    public static IReadOnlySet<string> RiskyMimeTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-msdownload", "application/x-msdos-program", "application/x-ms-installer", "application/x-msi",
        "application/x-ms-application", "application/vnd.microsoft.portable-executable", "application/x-dosexec",
        "application/x-executable", "application/hta", "application/x-ms-shortcut", "application/java-archive",
        "application/x-java-archive", "application/x-bat", "application/x-msdos-batch", "application/x-powershell",
        "application/vnd.ms-cab-compressed", "application/x-ms-wim", "application/msix", "application/appx",
    };

    /// <summary>
    /// 5.4: a file that can run code when opened: its own type is risky; or it is an archive whose name carries a risky type
    /// ("setup.exe.zip"); or the server sent it as a program.
    /// </summary>
    public static bool IsRisky(string? fileName, string? mimeType)
    {
        var name = SafeFileName(fileName);
        var parts = name.Split('.');
        if (parts.Length > 1 && RiskyExtensions.Contains(parts[^1])) return true;
        if (parts.Length > 2 && ArchiveExtensions.Contains(parts[^1]))
            for (var i = 1; i < parts.Length - 1; i++)
                if (RiskyExtensions.Contains(parts[i])) return true;
        return mimeType is not null && RiskyMimeTypes.Contains(MimeOnly(mimeType));
    }

    /// <summary>
    /// A name Windows can write in a folder of nth's choosing: no folder parts, no characters Windows refuses, no trailing dots
    /// or spaces (Windows drops them, which could turn "a.exe." into "a.exe" after the check), no device names, at most
    /// <see cref="MaxNameLength"/> characters with the type kept.
    /// </summary>
    public static string SafeFileName(string? suggested)
    {
        var name = suggested ?? "";
        var slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (slash >= 0) name = name[(slash + 1)..];
        var chars = name.Select(c => c < 0x20 || c is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '\u007f' ? '_' : c).ToArray();
        name = new string(chars);
        // Bidirectional overrides make "exe.txt" read as "txt.exe" (and the reverse): they never reach a file name.
        name = new string(name.Where(c => c is not ('\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e' or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\u200e' or '\u200f')).ToArray());
        name = name.Trim().TrimEnd('.', ' ').TrimStart('.', ' ');
        if (name.Length == 0) return FallbackName;
        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var extension = dot > 0 ? name[dot..] : "";
        if (IsDeviceName(stem)) stem = "_" + stem;
        if (stem.Length + extension.Length > MaxNameLength)
        {
            if (extension.Length > 20) extension = extension[..20];
            stem = stem[..Math.Max(1, MaxNameLength - extension.Length)];
        }
        return stem + extension;
    }

    /// <summary>
    /// The first free path for <paramref name="name"/> in <paramref name="folder"/>: the name itself, else "name (1).ext",
    /// "name (2).ext" and on, as Windows names copies. <paramref name="exists"/> answers for files and folders.
    /// </summary>
    public static string UniquePath(string folder, string name, Func<string, bool> exists)
    {
        var safe = SafeFileName(name);
        var path = Path.Combine(folder, safe);
        if (!exists(path)) return path;
        var dot = safe.LastIndexOf('.');
        var stem = dot > 0 ? safe[..dot] : safe;
        var extension = dot > 0 ? safe[dot..] : "";
        for (var n = 1; n < 10_000; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}){extension}");
            if (!exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    /// <summary>The type the prompt names ("CSV", "Application", "Script", "Archive"): the canvas's middle part.</summary>
    public static string TypeLabel(string? fileName, string? mimeType)
    {
        var name = SafeFileName(fileName);
        var dot = name.LastIndexOf('.');
        var extension = dot > 0 ? name[(dot + 1)..].ToLowerInvariant() : "";
        switch (extension)
        {
            case "exe" or "msi" or "msix" or "appx" or "appxbundle" or "msixbundle" or "scr" or "com" or "pif" or "application" or "appref-ms":
                return "Application";
            case "bat" or "cmd" or "ps1" or "psm1" or "vbs" or "vbe" or "js" or "jse" or "wsf" or "wsh" or "hta":
                return "Script";
            case "zip" or "7z" or "rar" or "tar" or "gz" or "tgz" or "bz2" or "xz" or "cab":
                return extension.ToUpperInvariant() + " archive";
            case "lnk" or "url":
                return "Shortcut";
            case "iso" or "img" or "vhd" or "vhdx":
                return "Disk image";
        }
        if (extension.Length is > 0 and <= 5 && extension.All(char.IsAsciiLetterOrDigit)) return extension.ToUpperInvariant();
        return MimeOnly(mimeType ?? "") switch
        {
            var m when m.StartsWith("image/", StringComparison.Ordinal) => "Image",
            var m when m.StartsWith("video/", StringComparison.Ordinal) => "Video",
            var m when m.StartsWith("audio/", StringComparison.Ordinal) => "Audio",
            var m when m.StartsWith("text/", StringComparison.Ordinal) => "Text",
            _ => "File",
        };
    }

    /// <summary>A size as the prompt shows it: "12 KB", "84 MB", "3.1 GB" (1,024 to a unit, as File Explorer counts).</summary>
    public static string SizeText(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return bytes == 1 ? "1 byte" : bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var digits = value < 10 ? value.ToString("0.#", CultureInfo.InvariantCulture) : Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        return digits + " " + units[unit];
    }

    /// <summary>The prompt's detail line: "12 KB · CSV · from localhost:5173" (no size part when the server sent none).</summary>
    public static string Detail(long? bytes, string? fileName, string? mimeType, string source)
    {
        var parts = new List<string>(3);
        if (bytes is { } size and >= 0) parts.Add(SizeText(size));
        parts.Add(TypeLabel(fileName, mimeType));
        parts.Add("from " + source);
        return string.Join(" · ", parts);
    }

    /// <summary>10.1: Save's full name ("Save plans.csv to Downloads").</summary>
    public static string SaveName(string fileName) => $"Save {fileName} to Downloads";

    public static string SaveAsName(string fileName) => $"Save {fileName} somewhere else";

    public static string CancelName(string fileName) => $"Cancel the download of {fileName}";

    /// <summary>The quiet notice when a saved file is complete, or at once for Cancel (the canvas's toasts).</summary>
    public static string Confirmation(DownloadChoice choice, string fileName) => choice switch
    {
        DownloadChoice.Save => "Saved to Downloads · " + fileName,
        DownloadChoice.SaveAs => "Saved · " + fileName,
        _ => "Download cancelled",
    };

    private static string MimeOnly(string mime)
    {
        var semicolon = mime.IndexOf(';', StringComparison.Ordinal);
        return (semicolon >= 0 ? mime[..semicolon] : mime).Trim().ToLowerInvariant();
    }

    private static bool IsDeviceName(string stem)
    {
        var upper = stem.TrimEnd().ToUpperInvariant();
        var dot = upper.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0) upper = upper[..dot];
        return upper is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) && upper[3] is >= '0' and <= '9' or '¹' or '²' or '³';
    }
}

/// <summary>The three answers of the download prompt.</summary>
public enum DownloadChoice
{
    Cancel,
    Save,
    SaveAs,
}

/// <summary>
/// DESIGN 5.4: Save on a risky file needs a second click within 3 s; the first only arms it ("Click again to save"), and it
/// disarms by itself when the window passes. A safe file saves on the first click.
/// </summary>
public sealed class DownloadSaveArm(bool risky, TimeProvider time)
{
    private long? armedAt;

    public bool IsRisky { get; } = risky;

    /// <summary>Whether a first click armed Save and the window has not passed.</summary>
    public bool IsArmed => armedAt is { } at && time.GetElapsedTime(at) < DownloadGate.SecondClickWindow;

    /// <summary>Save's label now.</summary>
    public string Label => IsArmed ? DownloadGate.ArmedLabel : DownloadGate.SaveLabel;

    /// <summary>A click on Save: true when it saves now; false when it only armed it.</summary>
    public bool Press()
    {
        if (!IsRisky || IsArmed) return true;
        armedAt = time.GetTimestamp();
        return false;
    }

    /// <summary>Back to Save (the window passed, or the prompt was answered another way).</summary>
    public void Disarm() => armedAt = null;
}
