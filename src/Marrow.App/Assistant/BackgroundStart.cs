// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4, 15.1; unit AS-10; ED-NEW-dextra-as10-13). No Swift twin.
// What nth does at launch for its duties, with the window open or not: when any assistant has duties, it opens Dextra's store and
// activity log, starts Dextra's controller (unless AS-8 already did) and starts the duties, so they run, catch up what came due while
// nth was closed, and keep running in the tray after the window is closed. It fails closed: a store that is busy, or an activity
// log that cannot be opened (no key, a second writer), starts nothing, because a duty run must be written down (12.5). It starts
// nothing at all for an owner with no duties. A start for one duty ("nth --duty id") with nothing to run asks to quit at once.
using System.Text.Json.Nodes;
using Marrow.App.Tray;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>The few things a test replaces: the folders, the audit key, and the duty host's own fakes.</summary>
public sealed class BackgroundStartSeams
{
    public Func<IAssistantStorageRoots> Roots { get; init; } = () => AssistantStorage.Roots.Current;

    public Func<AuditKeyStore> Keys { get; init; } = AuditKeyStore.Production;

    /// <summary>The one stop chord and the one tray icon; null makes the app's own (a real chord thread and the real tray).</summary>
    public Marrow.App.Safety.GlobalStop? StopKey { get; init; }

    public AppTray? Tray { get; init; }

    /// <summary>Fakes for the host (Task Scheduler, the Run key, the desktop, the clock); the rest is the app's own.</summary>
    public DutySeams? Host { get; init; }
}

public static class BackgroundStart
{
    /// <summary>Whether any assistant has a duties file: the cheapest check, made before anything is opened.</summary>
    public static bool HasDuties(IAssistantStorageRoots roots)
    {
        var folder = Path.Combine(roots.AssistantRoot, "assistants");
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateDirectories(folder).Any(d => File.Exists(Path.Combine(d, "duties.json")) && new FileInfo(Path.Combine(d, "duties.json")).Length > 2);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The ids of the assistants that have duties files, and the profiles to run them under: whatever the store's profile files say, and
    /// Dextra itself for an id that has none (AS-8 and AS-16 write the real ones).
    /// </summary>
    public static IReadOnlyList<AssistantProfile> Profiles(AssistantStore store, IReadOnlyList<string> ids)
    {
        var profiles = new List<AssistantProfile>();
        foreach (var id in ids)
        {
            var body = store.Profile(id);
            var name = body?["name"]?.GetValue<string>() is { Length: > 0 } given ? given : AssistantNames.Assistant;
            var parent = body?["parent"]?.GetValue<string>();
            profiles.Add(new AssistantProfile(id, name) { ParentId = parent is not null && ids.Contains(parent) && parent != id ? parent : null });
        }
        return profiles;
    }

    /// <summary>Whether any assistant's duties file holds the Morning digest duty (AS-11).</summary>
    public static bool HasDigestDuty(AssistantStore store, IReadOnlyList<string> ids) =>
        ids.Any(assistant => store.Duties(assistant) is JsonObject document && document["duties"] is JsonArray list
            && list.OfType<JsonObject>().Any(duty => duty["id"] is JsonValue value && value.TryGetValue<string>(out var text) && text == DigestDuty.Id));

    public static IReadOnlyList<string> AssistantsWithDuties(AssistantStore store)
    {
        var folder = Path.Combine(store.Root, "assistants");
        if (!Directory.Exists(folder)) return [];
        return [.. Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>()
            .Where(id => AssistantStore.IsId(id) && File.Exists(Path.Combine(folder, id, "duties.json"))).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Starts Dextra's duties for this launch. Returns the host, or null with <paramref name="problem"/> saying why not (nothing to run, or
    /// something that must fail closed).
    /// </summary>
    public static DutyHost? Run(AppModel model, StartupMode mode, Func<bool> windowHidden, Action open, Action quit, out string? problem, BackgroundStartSeams? seams = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        problem = null;
        // The shell needs its Open affordance even when there are no duties or Dextra is Off.
        if (seams is null || seams.Tray is not null)
            model.EnsureTray(windowHidden, null, open, seams?.Tray).Refresh();
        if (!model.AssistantHost.MayRun) return null;
        var roots = seams?.Roots() ?? AssistantStorage.Roots.Current;
        if (model.Duties is { } running) return running;
        if (!HasDuties(roots)) return null;
        var controller = model.AssistantHost.EnsureStarted(seams, mode, quit, restoringDuties: true);
        if (controller is not null) return model.Duties;
        if (model.AssistantHost.StartFault is { } fault)
            problem = "Dextra's duties did not start: " + fault;
        return null;
    }
}
