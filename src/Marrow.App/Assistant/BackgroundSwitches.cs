// PORT-ONLY: Dextra (DESIGN.md rev 5, 0 point 10, 7.4; canvas 5; unit AS-10; ED-NEW-dextra-as10-14). No Swift twin.
// The two switches the first duty's card offers, both off until the owner taps them: "Keep working when the window is closed"
// (nth stays in the tray and the scheduler keeps running) and "Start with Windows, hidden" (a per-user Run entry for nth --hidden).
// The first is kept in the assistant store (background.json); the second is the Run entry itself, so what the switch shows is what
// Windows will do. Letting agents spend allowance in the background is the owner's explicit choice, so nothing here turns either on.
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class BackgroundSwitches
{
    public const string KeepWorkingWords = "Keep working when the window is closed";

    public const string KeepWorkingNote = "nth stays in the tray; Stop is in the tray menu.";

    public const string StartWithWindowsWords = "Start with Windows, hidden";

    public const string StartWithWindowsNote = "Duties catch up once if nth was closed.";

    private readonly Func<JsonObject?> load;
    private readonly Action<JsonObject> save;
    private readonly StartWithWindows startup;
    private readonly Func<string> exePath;
    private readonly object gate = new();
    private readonly List<string> approvedFolders = [];
    private readonly JsonObject? kept;
    private readonly bool newer;
    private bool keepWorking;
    private bool offered;

    /// <summary>The version of background.json this build writes.</summary>
    public const int CurrentVersion = 1;

    public BackgroundSwitches(Func<JsonObject?> load, Action<JsonObject> save, StartWithWindows startup, Func<string> exePath)
    {
        this.load = load ?? throw new ArgumentNullException(nameof(load));
        this.save = save ?? throw new ArgumentNullException(nameof(save));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        kept = load()?.DeepClone() as JsonObject;
        newer = kept?["version"] is JsonValue version && version.TryGetValue<int>(out var written) && written > CurrentVersion;
        keepWorking = kept?["keepWorking"] is JsonValue keep && keep.TryGetValue<bool>(out var on) && on;
        offered = kept?["firstDutyOffered"] is JsonValue shown && shown.TryGetValue<bool>(out var seen) && seen;
        if (kept?["approvedFolders"] is JsonArray folders)
            approvedFolders.AddRange(folders.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var path) ? path : null).OfType<string>().Where(p => Path.IsPathFullyQualified(p)).Take(FolderLimit));
    }

    /// <summary>The most folders the owner's duties may have approved for watching.</summary>
    public const int FolderLimit = 200;

    /// <summary>A switch changed.</summary>
    public event Action? Changed;

    /// <summary>Closing the window leaves nth running in the tray. Off until the owner taps it.</summary>
    public bool KeepWorkingWhenClosed
    {
        get
        {
            lock (gate) return keepWorking;
        }
    }

    /// <summary>nth starts hidden at sign-in. Off until the owner taps it.</summary>
    public bool StartWithWindowsOn => startup.IsOn(exePath());

    /// <summary>The card with the two switches was shown (once, for the first duty).</summary>
    public bool FirstDutyOffered
    {
        get
        {
            lock (gate) return offered;
        }
    }

    /// <summary>
    /// The owner said yes to a duty that watches this folder (his card, or his own Save): that is the grant, kept here and not in the duties file,
    /// so a duties file someone else wrote cannot approve a folder for itself (7.2: only folders the owner granted).
    /// </summary>
    public string? ApproveFolder(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return "That is not a full path.";
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        lock (gate)
        {
            if (approvedFolders.Any(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase))) return null;
            if (approvedFolders.Count >= FolderLimit) return "nth keeps at most " + FolderLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) + " watched folders.";
            approvedFolders.Add(full);
            if (Persist() is { } problem)
            {
                approvedFolders.Remove(full);
                return problem;
            }
        }
        return null;
    }

    /// <summary>Whether a folder, or one above it, was approved by the owner for a duty.</summary>
    public bool IsFolderApproved(string path)
    {
        lock (gate) return approvedFolders.Any(root => PathRules.IsInside(path, root, strict: false));
    }

    public string? SetKeepWorking(bool on)
    {
        lock (gate)
        {
            if (keepWorking == on) return null;
            keepWorking = on;
            if (Persist() is { } problem)
            {
                keepWorking = !on;
                return problem;
            }
        }
        Changed?.Invoke();
        return null;
    }

    public string? SetStartWithWindows(bool on)
    {
        var problem = startup.Turn(on, exePath());
        if (problem is null) Changed?.Invoke();
        return problem;
    }

    /// <summary>The first duty's card was shown; it is not offered again.</summary>
    public void MarkFirstDutyOffered()
    {
        lock (gate)
        {
            if (offered) return;
            offered = true;
            _ = Persist();
        }
        Changed?.Invoke();
    }

    // Under the lock. Null when written; else why not (the switch then keeps its old value).
    private string? Persist()
    {
        try
        {
            // A file a newer nth wrote is left as it is; a file of this version keeps the fields this build does not know.
            if (newer) return "nth could not save that choice: its settings file was written by a newer nth, which nth leaves alone.";
            var document = kept?.DeepClone() as JsonObject ?? [];
            document["version"] = CurrentVersion;
            document["keepWorking"] = keepWorking;
            document["firstDutyOffered"] = offered;
            document["approvedFolders"] = new JsonArray([.. approvedFolders.Select(f => (JsonNode)f)]);
            save(document);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            return "nth could not save that choice: " + error.Message;
        }
    }
}
