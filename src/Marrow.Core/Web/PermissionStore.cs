// PORT-ONLY: web surfaces W8b (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.5 "PermissionRequested is denied by default and nth
// shows its own glass prompt ... Allow this time / Always allow on this site / Block ... Stored per profile and origin ... and listed
// in Settings › Visus › Site permissions with Remove", "Kinds handled: camera, microphone, location, notifications, clipboard read,
// MIDI, sensors, local fonts, window management, file system access, autoplay with sound, multiple downloads", 8.4 pointer lock
// "asks, like a permission (5.5)", 10.1 the buttons named in full; the canvas's #perm2 ("localhost:5173 wants to use your camera",
// "Allow this time", "Always on this site", "Block", "Enter does nothing here. Esc blocks."); ED-NEW-web-w8b-1). No Swift
// counterpart. The site answers nth keeps, and the words every prompt and list uses for each kind.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marrow.Core.Web;

/// <summary>DESIGN 5.5 and 8.4: what a site may ask for in a Visus tab.</summary>
public enum SitePermissionKind
{
    Camera,
    Microphone,
    Location,
    Notifications,
    ClipboardRead,
    Midi,
    Sensors,
    LocalFonts,
    WindowManagement,
    FileSystemAccess,
    Autoplay,
    MultipleDownloads,

    /// <summary>8.4: "Pointer lock ... asks, like a permission (5.5)".</summary>
    PointerLock,
}

/// <summary>A kept answer: allowed or blocked on the site for good (until removed).</summary>
public enum SitePermissionState
{
    Allow,
    Block,
}

/// <summary>The three answers of the prompt (the canvas's .pbtn rows).</summary>
public enum SitePermissionAnswer
{
    Block,
    AllowOnce,
    AllowAlways,
}

/// <summary>One kept answer: a profile, an origin ("https://example.com", "http://localhost:5173"), a kind and its state.</summary>
public sealed record SitePermission(WebProfileKind Profile, string Origin, SitePermissionKind Kind, SitePermissionState State);

/// <summary>The words of each kind: the prompt's sentence, the buttons' full names (10.1) and the short noun of lists and notices.</summary>
public static class SitePermissions
{
    /// <summary>The prompt's foot, word for word (the canvas's #perm2 .foot).</summary>
    public const string Foot = "Enter does nothing here. Esc blocks.";

    public const string AllowOnceLabel = "Allow this time";

    public const string AllowAlwaysLabel = "Always on this site";

    public const string BlockLabel = "Block";

    /// <summary>What the site wants, after "wants to" ("use your camera").</summary>
    public static string Verb(SitePermissionKind kind) => kind switch
    {
        SitePermissionKind.Camera => "use your camera",
        SitePermissionKind.Microphone => "use your microphone",
        SitePermissionKind.Location => "know your location",
        SitePermissionKind.Notifications => "show notifications",
        SitePermissionKind.ClipboardRead => "see text and images you copy",
        SitePermissionKind.Midi => "use your MIDI devices",
        SitePermissionKind.Sensors => "use your device's motion sensors",
        SitePermissionKind.LocalFonts => "use the fonts on this PC",
        SitePermissionKind.WindowManagement => "manage windows on all your screens",
        SitePermissionKind.FileSystemAccess => "edit files and folders on this PC",
        SitePermissionKind.Autoplay => "play sound automatically",
        SitePermissionKind.MultipleDownloads => "download multiple files",
        SitePermissionKind.PointerLock => "hide and lock your pointer",
        _ => "use a feature of this PC",
    };

    /// <summary>The same in the "-ing" form, after "Block ... from" ("using your camera").</summary>
    public static string Gerund(SitePermissionKind kind) => kind switch
    {
        SitePermissionKind.Camera => "using your camera",
        SitePermissionKind.Microphone => "using your microphone",
        SitePermissionKind.Location => "knowing your location",
        SitePermissionKind.Notifications => "showing notifications",
        SitePermissionKind.ClipboardRead => "seeing text and images you copy",
        SitePermissionKind.Midi => "using your MIDI devices",
        SitePermissionKind.Sensors => "using your device's motion sensors",
        SitePermissionKind.LocalFonts => "using the fonts on this PC",
        SitePermissionKind.WindowManagement => "managing windows on all your screens",
        SitePermissionKind.FileSystemAccess => "editing files and folders on this PC",
        SitePermissionKind.Autoplay => "playing sound automatically",
        SitePermissionKind.MultipleDownloads => "downloading multiple files",
        SitePermissionKind.PointerLock => "hiding and locking your pointer",
        _ => "using a feature of this PC",
    };

    /// <summary>The short noun of the site note, Settings and the notices ("Camera").</summary>
    public static string Noun(SitePermissionKind kind) => kind switch
    {
        SitePermissionKind.Camera => "Camera",
        SitePermissionKind.Microphone => "Microphone",
        SitePermissionKind.Location => "Location",
        SitePermissionKind.Notifications => "Notifications",
        SitePermissionKind.ClipboardRead => "Clipboard",
        SitePermissionKind.Midi => "MIDI devices",
        SitePermissionKind.Sensors => "Motion sensors",
        SitePermissionKind.LocalFonts => "Fonts",
        SitePermissionKind.WindowManagement => "Window management",
        SitePermissionKind.FileSystemAccess => "File editing",
        SitePermissionKind.Autoplay => "Sound",
        SitePermissionKind.MultipleDownloads => "Multiple downloads",
        SitePermissionKind.PointerLock => "Pointer lock",
        _ => "Feature",
    };

    /// <summary>"localhost:5173 wants to use your camera" (the canvas's heading, no full stop).</summary>
    public static string Title(string source, SitePermissionKind kind) => $"{source} wants to {Verb(kind)}";

    /// <summary>10.1: "Allow localhost:5173 to use your camera this time".</summary>
    public static string AllowOnceName(string source, SitePermissionKind kind) => $"Allow {source} to {Verb(kind)} this time";

    /// <summary>10.1: "Always allow localhost:5173 to use your camera".</summary>
    public static string AllowAlwaysName(string source, SitePermissionKind kind) => $"Always allow {source} to {Verb(kind)}";

    /// <summary>10.1: "Block localhost:5173 from using your camera".</summary>
    public static string BlockName(string source, SitePermissionKind kind) => $"Block {source} from {Gerund(kind)}";

    /// <summary>The quiet notice after an answer (the canvas's toasts): "Camera allowed this time".</summary>
    public static string Confirmation(string source, SitePermissionKind kind, SitePermissionAnswer answer) => answer switch
    {
        SitePermissionAnswer.AllowOnce => $"{Noun(kind)} allowed this time",
        SitePermissionAnswer.AllowAlways => $"{Noun(kind)} always allowed on {source}",
        _ => $"{Noun(kind)} blocked on this site",
    };

    /// <summary>A private tab's Block holds for the tab only: the notice says so.</summary>
    public static string PrivateBlockConfirmation(SitePermissionKind kind) => $"{Noun(kind)} blocked in this tab";

    /// <summary>The state as the site note and Settings say it ("Allowed", "Blocked").</summary>
    public static string StateWord(SitePermissionState state) => state == SitePermissionState.Allow ? "Allowed" : "Blocked";

    /// <summary>
    /// Whether a profile asks at all (5.5): browse, preview and private tabs ask; the agent profile and sandboxed content are
    /// always denied, no prompt; notifications are blocked in preview and private (they map to nth's toast in browse only).
    /// </summary>
    public static bool Asks(WebProfileKind profile, SitePermissionKind kind) => profile switch
    {
        WebProfileKind.Browse => true,
        WebProfileKind.Preview or WebProfileKind.Private => kind != SitePermissionKind.Notifications,
        _ => false,
    };

    /// <summary>
    /// Whether the prompt offers "Always on this site": only where answers are kept (browse and preview). A private tab offers
    /// Allow this time and Block, and keeps neither past the tab (8.1 "private tabs leave nothing").
    /// </summary>
    public static bool OffersAlways(WebProfileKind profile) => profile is WebProfileKind.Browse or WebProfileKind.Preview;
}

/// <summary>
/// DESIGN 5.5: the "Always on this site" and "Block" answers, per profile and origin, for browse and preview only. nth's store is
/// the only memory of an answer: the engine is told every answer with SavesInProfile off, so Remove always works (even with no
/// view open) and the list in Settings is the whole truth (ED-NEW-web-w8b-1). Every member runs on the UI thread.
/// </summary>
public sealed class PermissionStore
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly List<SitePermission> entries = [];

    /// <summary>Every kept answer, oldest first.</summary>
    public IReadOnlyList<SitePermission> All => entries;

    public event Action? Changed;

    /// <summary>The kept answer for the site and kind, or null (the prompt asks).</summary>
    public SitePermissionState? StateOf(WebProfileKind profile, string? origin, SitePermissionKind kind)
    {
        if (!Keeps(profile) || Clean(origin) is not { } site) return null;
        foreach (var entry in entries)
            if (entry.Profile == profile && entry.Kind == kind && entry.Origin == site) return entry.State;
        return null;
    }

    /// <summary>The kept answers of one site, in the order of <see cref="SitePermissionKind"/>.</summary>
    public IReadOnlyList<SitePermission> For(WebProfileKind profile, string? origin)
    {
        if (Clean(origin) is not { } site) return [];
        return entries.Where(entry => entry.Profile == profile && entry.Origin == site).OrderBy(entry => entry.Kind).ToList();
    }

    /// <summary>Keeps an answer; false (and nothing kept) for a profile that keeps none or an origin that is not a web site.</summary>
    public bool Set(WebProfileKind profile, string? origin, SitePermissionKind kind, SitePermissionState state)
    {
        if (!Keeps(profile) || Clean(origin) is not { } site || !Enum.IsDefined(kind)) return false;
        var index = entries.FindIndex(entry => entry.Profile == profile && entry.Kind == kind && entry.Origin == site);
        var entry = new SitePermission(profile, site, kind, state);
        if (index >= 0)
        {
            if (entries[index] == entry) return true;
            entries[index] = entry;
        }
        else
        {
            entries.Add(entry);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Applies the prompt's answer: Always keeps Allow, Block keeps Block, Allow this time keeps nothing.</summary>
    public void Record(WebProfileKind profile, string? origin, SitePermissionKind kind, SitePermissionAnswer answer)
    {
        switch (answer)
        {
            case SitePermissionAnswer.AllowAlways:
                Set(profile, origin, kind, SitePermissionState.Allow);
                break;
            case SitePermissionAnswer.Block:
                Set(profile, origin, kind, SitePermissionState.Block);
                break;
        }
    }

    /// <summary>Removes one answer (Settings › Site permissions › Remove, the site note's Reset).</summary>
    public bool Remove(WebProfileKind profile, string? origin, SitePermissionKind kind)
    {
        if (Clean(origin) is not { } site) return false;
        if (entries.RemoveAll(entry => entry.Profile == profile && entry.Origin == site && entry.Kind == kind) == 0) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes every answer of a profile, or of one site in it (Clear browsing data, Clear site data).</summary>
    public void Forget(WebProfileKind profile, string? origin = null)
    {
        var site = origin is null ? null : Clean(origin);
        if (origin is not null && site is null) return;
        if (entries.RemoveAll(entry => entry.Profile == profile && (site is null || entry.Origin == site)) > 0) Changed?.Invoke();
    }

    public string ToJson() => JsonSerializer.Serialize(entries, Json);

    /// <summary>The saved answers; anything a damaged or edited file holds beyond the rules is dropped.</summary>
    public static PermissionStore FromJson(string? json)
    {
        var result = new PermissionStore();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            foreach (var entry in JsonSerializer.Deserialize<List<SitePermission>>(json, Json) ?? [])
            {
                if (entry is null || !Keeps(entry.Profile) || !Enum.IsDefined(entry.Kind) || !Enum.IsDefined(entry.State)) continue;
                if (Clean(entry.Origin) is not { } site || !SitePermissions.Asks(entry.Profile, entry.Kind)) continue;
                var clean = entry with { Origin = site };
                if (result.entries.Any(kept => kept.Profile == clean.Profile && kept.Origin == clean.Origin && kept.Kind == clean.Kind)) continue;
                result.entries.Add(clean);
            }
        }
        catch (JsonException)
        {
        }
        return result;
    }

    /// <summary>Only browse and preview keep answers (private tabs leave nothing; agent tabs and sandboxed content never ask).</summary>
    public static bool Keeps(WebProfileKind profile) => profile is WebProfileKind.Browse or WebProfileKind.Preview;

    /// <summary>An http or https origin in its lower-case "scheme://host[:port]" form, or null.</summary>
    public static string? Clean(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)) return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}
