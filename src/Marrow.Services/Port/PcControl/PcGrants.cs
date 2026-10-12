// PORT-ONLY: Tactus, PC control (OD-0144, OD-0145, ED-NEW-pcctl-25; DESIGN.md 4.1 "Grants are stored in nth settings
// (SettingsStore), not in any provider's config. "Always" grants are listed and removable in Settings", 4.7 "Apps: list
// with icon, name, See and Use chips, This chat / Always scope, remove button", 10.4 approval body "Use implies See").
// The app grants: "Always" grants in nth's settings under pcControl.grants (one entry per app: its key, name, exe path
// and whether Use is granted; See is implied by the entry), and "This chat" grants in memory per chat thread (a chat's
// grant ends with the app's session). Hydra heads never read or write an "Always" grant (critique C14): the engine asks
// them afresh, and their answers are kept per chat only.
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>Whether a grant is for one chat or for every chat.</summary>
public enum PcGrantScope
{
    Chat,
    Always,
}

/// <summary>One app's grant: See always, Use when <see cref="Use"/>.</summary>
public sealed record PcGrant(string Key, string Name, string ExePath, bool Use, PcGrantScope Scope, string? Thread = null)
{
    /// <summary>Whether this grant covers <paramref name="capability"/>: Use implies See.</summary>
    public bool Covers(PcCapability capability) => capability == PcCapability.See || Use;
}

public sealed class PcGrants
{
    /// <summary>The settings key of the "Always" grants (an array of dictionaries).</summary>
    public const string Key = "pcControl.grants";

    private const string AppField = "app";
    private const string NameField = "name";
    private const string PathField = "path";
    private const string UseField = "use";

    private readonly Lock gate = new();
    private readonly ISettingsStore store;
    private readonly Dictionary<(string Thread, string App), PcGrant> chat = [];

    public PcGrants(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>A grant was made, changed or removed (Settings redraws its list).</summary>
    public event Action? Changed;

    /// <summary>The "Always" grants, in the order they were made.</summary>
    public IReadOnlyList<PcGrant> Always
    {
        get
        {
            lock (gate) return Read();
        }
    }

    /// <summary>Every grant: the "Always" ones, then each chat's, in the order they were made.</summary>
    public IReadOnlyList<PcGrant> All
    {
        get
        {
            lock (gate) return [.. Read(), .. chat.Values];
        }
    }

    /// <summary>The standing "Always" grant for this app and capability (the engine's <c>Standing</c>), or null.</summary>
    public PcApproval? Standing(PcAppIdentity app, PcCapability capability)
    {
        ArgumentNullException.ThrowIfNull(app);
        lock (gate) return Read().FirstOrDefault(g => g.Key == app.Key) is { } grant && grant.Covers(capability) ? PcApproval.Always : null;
    }

    /// <summary>This chat's grant for this app and capability, or null (an "Always" grant is not a chat grant).</summary>
    public PcApproval? ForChat(string thread, PcAppIdentity app, PcCapability capability)
    {
        ArgumentNullException.ThrowIfNull(app);
        lock (gate) return chat.TryGetValue((thread, app.Key), out var grant) && grant.Covers(capability) ? PcApproval.Chat : null;
    }

    /// <summary>Records an answer: "Allow for this chat" in memory, "Always allow" in settings. A denial records nothing.</summary>
    public void Grant(string thread, PcAppIdentity app, string name, bool use, PcGrantScope scope)
    {
        ArgumentNullException.ThrowIfNull(app);
        lock (gate)
        {
            if (scope == PcGrantScope.Chat)
            {
                var had = chat.TryGetValue((thread, app.Key), out var old) && old.Use;
                chat[(thread, app.Key)] = new PcGrant(app.Key, name, app.ExePath, use || had, PcGrantScope.Chat, thread);
            }
            else
            {
                var grants = Read().ToList();
                var at = grants.FindIndex(g => g.Key == app.Key);
                var grant = new PcGrant(app.Key, name, app.ExePath, use || (at >= 0 && grants[at].Use), PcGrantScope.Always);
                if (at >= 0) grants[at] = grant;
                else grants.Add(grant);
                Write(grants);
                chat.Remove((thread, app.Key));
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Settings' Use chip: turns Use on or off for an "Always" grant (See stays).</summary>
    public bool SetUse(string key, bool use)
    {
        lock (gate)
        {
            var grants = Read().ToList();
            var at = grants.FindIndex(g => g.Key == key);
            if (at < 0 || grants[at].Use == use) return false;
            grants[at] = grants[at] with { Use = use };
            Write(grants);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Settings' remove button: drops the app's "Always" grant and every chat's grant for it.</summary>
    public bool Remove(string key)
    {
        var removed = false;
        lock (gate)
        {
            var grants = Read().ToList();
            if (grants.RemoveAll(g => g.Key == key) > 0)
            {
                Write(grants);
                removed = true;
            }
            foreach (var entry in chat.Keys.Where(k => k.App == key).ToList())
            {
                chat.Remove(entry);
                removed = true;
            }
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>A chat's session ended: its grants go.</summary>
    public void EndChat(string thread)
    {
        var removed = false;
        lock (gate)
        {
            foreach (var entry in chat.Keys.Where(k => k.Thread == thread).ToList())
            {
                chat.Remove(entry);
                removed = true;
            }
        }
        if (removed) Changed?.Invoke();
    }

    // A malformed entry (not a dictionary, no key) is skipped, never repaired in place; the next write keeps only the readable ones.
    private List<PcGrant> Read()
    {
        var list = new List<PcGrant>();
        if (store.Array(Key) is not { } entries) return list;
        foreach (var entry in entries)
        {
            if (entry is not IReadOnlyDictionary<string, object> map || map.GetValueOrDefault(AppField) is not string { Length: > 0 } key) continue;
            if (list.Exists(g => g.Key == key)) continue;
            var name = map.GetValueOrDefault(NameField) as string ?? key;
            var path = map.GetValueOrDefault(PathField) as string ?? "";
            var use = Plist.AsBool(map.GetValueOrDefault(UseField)) ?? false;
            list.Add(new PcGrant(key, name, path, use, PcGrantScope.Always));
        }
        return list;
    }

    private void Write(IReadOnlyList<PcGrant> grants)
    {
        if (grants.Count == 0)
        {
            store.RemoveObject(Key);
            return;
        }
        store.Set(Key, grants.Select(g => (object)new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [AppField] = g.Key,
            [NameField] = g.Name,
            [PathField] = g.ExePath,
            [UseField] = g.Use,
        }).ToList());
    }
}
