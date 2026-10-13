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
        seams ??= new BackgroundStartSeams();
        problem = null;
        var roots = seams.Roots();
        if (!HasDuties(roots)) return null;
        if (model.Duties is { } running) return running;
        AssistantStore? store = null;
        AuditLog? log = null;
        try
        {
            store = model.DextraStore ?? AssistantStore.Open(roots);
            var ids = AssistantsWithDuties(store);
            if (model.Dextra is null)
            {
                log = AuditLog.Open(roots, seams.Keys());
                var profiles = Profiles(store, ids);
                var held = log;
                // The same roots as the window's start (DextraHost.Production): the audit folder, beside what StartDextra always protects (ED-NEW-dextra-f4-1).
                model.StartDextra(store, entry => held.Append(entry), () => profiles, protectedRoots: [roots.AuditDirectory], stopKey: seams.StopKey ?? model.EnsureStopKey(),
                    tray: seams.Tray ?? model.EnsureTray(windowHidden, null, open), duties: model.DutyToolsGate);
                log = null; // the controller's audit callback owns it for the life of the app
            }
            var controller = model.Dextra!;
            // The launch brings back what is there: the digest is bound only when the Morning digest duty already is (so it is composed, not started as a
            // mission); making it is turning Dextra on (AS-8), not a launch.
            var host = model.StartDuties(controller, store, mode, quit: quit, assistantIds: () => AssistantsWithDuties(store), tray: seams.Tray ?? model.Tray, seams: seams.Host,
                digest: model.DigestSetupOf(controller, roots.AuditDirectory), withDigest: HasDigestDuty(store, ids));
            var notes = host.Start();
            problem = notes.Count == 0 ? null : string.Join(" ", notes);
            return host;
        }
        catch (Exception error) when (error is AssistantStoreBusyException or AuditLogBusyException or AuditKeyUnavailableException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            problem = "Dextra's duties did not start: " + error.Message;
            log?.Dispose();
            if (model.DextraStore is null) store?.Dispose();
            return null;
        }
    }
}
