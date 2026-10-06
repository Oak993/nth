using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port;

namespace Marrow.Core.Accounts.Pills;

/// <summary>What a load found. <see cref="Newer"/>: the stored text is from a newer app; <see cref="Problem"/>: it could not be read at all. Either blocks saving over it.</summary>
public sealed record PillLoad(IReadOnlyList<Pill> Pills, Guid? UseForNewThreads, int FoundVersion, int Skipped, bool Newer, string? Problem);

/// <summary>
/// Pills in settings as one JSON text under one key, versioned so an older or newer app never loses them (MA-7).
/// Version 1 is the first shape: <c>{version, useForNewThreads?, pills: [{id, name, earlierAccount?, handshakeMode, handshakeAll?, steps: [{account, key?, model?, effort?, fast?, handshake?}]}]}</c>.
/// A load migrates older text one version at a time through <c>migrations[v]</c> (v to v+1) in memory; text from a newer app is read for the fields this version knows and never written over;
/// unreadable text loads empty and is never written over; a pill or step that cannot be read is skipped and counted. The text is rewritten at the current version on the next change.
/// </summary>
public sealed class PillStorage(
    ISettingsStore settings,
    string key = PillStorage.DefaultKey,
    int currentVersion = 1,
    IReadOnlyDictionary<int, Func<JsonObject, JsonObject>>? migrations = null)
{
    public const string DefaultKey = "multiAccount.pills";

    private static readonly string[] EarlierValues = [DecisionValues.GoBack, DecisionValues.Stay, DecisionValues.Ask];

    private bool blocked;

    public PillLoad Load()
    {
        blocked = false;
        PillLoad Empty(int version, string? problem, bool newer = false)
        {
            blocked = problem is not null || newer;
            return new PillLoad([], null, version, 0, newer, problem);
        }

        var raw = settings.Object(key);
        if (raw is null) return Empty(currentVersion, null);
        if (raw is not string text) return Empty(0, "The stored pills are not text.");

        JsonObject root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException error)
        {
            return Empty(0, $"The stored pills could not be read: {error.Message}");
        }

        var found = ReadInt(root["version"]) ?? 1;
        var newer = found > currentVersion;
        if (!newer)
        {
            try
            {
                for (var version = found; version < currentVersion; version++)
                    if (migrations is not null && migrations.TryGetValue(version, out var migrate)) root = migrate(root);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                return Empty(found, $"The stored pills could not be updated: {error.Message}");
            }
        }

        var skipped = 0;
        var pills = new List<Pill>();
        var ids = new HashSet<Guid>();
        foreach (var node in root["pills"] as JsonArray ?? [])
        {
            if (ReadPill(node, ref skipped) is not { } pill) continue;
            if (ids.Add(pill.Id)) pills.Add(pill);
            else skipped++;
        }
        Guid? use = root["useForNewThreads"] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) && ids.Contains(g) ? g : null;
        blocked = newer;
        return new PillLoad(pills, use, found, skipped, newer, null);
    }

    /// <summary>Writes the pills. Refused (false, stored text untouched) after a load that found newer or unreadable text, unless <paramref name="overwrite"/>.</summary>
    public bool Save(IReadOnlyList<Pill> pills, Guid? useForNewThreads, bool overwrite = false)
    {
        if (blocked && !overwrite) return false;
        var root = new JsonObject { ["version"] = currentVersion };
        if (useForNewThreads is { } use) root["useForNewThreads"] = use.ToString();
        var array = new JsonArray();
        foreach (var pill in pills)
        {
            var steps = new JsonArray();
            foreach (var step in pill.Steps)
            {
                var o = new JsonObject { ["account"] = step.AccountId };
                if (step.KeyId is not null) o["key"] = step.KeyId;
                if (step.Model is not null) o["model"] = step.Model;
                if (step.Effort is not null) o["effort"] = step.Effort;
                if (step.Fast is { } fast) o["fast"] = fast;
                if (step.HandshakeAt is { } point) o["handshake"] = WritePoint(point);
                steps.Add(o);
            }
            var p = new JsonObject
            {
                ["id"] = pill.Id.ToString(),
                ["name"] = pill.Name,
                ["handshakeMode"] = pill.HandshakeMode == PillHandshakeMode.SameForEvery ? "sameForEvery" : "perStep",
            };
            if (pill.EarlierAccount is not null) p["earlierAccount"] = pill.EarlierAccount;
            if (pill.SameHandshake is { } same) p["handshakeAll"] = WritePoint(same);
            p["steps"] = steps;
            array.Add(p);
        }
        root["pills"] = array;
        settings.Set(key, root.ToJsonString());
        blocked = false;
        return true;
    }

    private static JsonNode WritePoint(HandshakePoint point) => point.Percent is { } percent ? JsonValue.Create(percent) : JsonValue.Create("smart");

    private static Guid? ReadId(JsonNode? node) =>
        node is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var id) ? id : null;

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        return v.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue ? (int)d : null;
    }

    private static string? ReadString(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static HandshakePoint? ReadPoint(JsonNode? node)
    {
        if (ReadString(node) is { } text) return text == "smart" ? HandshakePoint.Smart : null;
        return ReadInt(node) is { } percent and >= HandshakePoint.MinPercent and <= HandshakePoint.MaxPercent ? HandshakePoint.At(percent) : null;
    }

    private static Pill? ReadPill(JsonNode? node, ref int skipped)
    {
        if (node is not JsonObject o || ReadId(o) is not { } id || ReadString(o["name"])?.Trim() is not { Length: > 0 } name)
        {
            skipped++;
            return null;
        }
        var steps = new List<PillStep>();
        foreach (var s in o["steps"] as JsonArray ?? [])
        {
            if (s is not JsonObject so || ReadString(so["account"])?.Trim() is not { Length: > 0 } account)
            {
                skipped++;
                continue;
            }
            // An absent key means the account's own sign-in. A malformed present identity cannot grant that fallback.
            if (so.TryGetPropertyValue("key", out var storedKey) && ReadString(storedKey)?.Trim() is not { Length: > 0 })
            {
                skipped++;
                continue;
            }
            steps.Add(new PillStep(
                account,
                ReadString(so["model"]),
                ReadString(so["effort"]),
                so["fast"] is JsonValue fv && fv.TryGetValue<bool>(out var fast) ? fast : null,
                ReadPoint(so["handshake"]),
                ReadString(so["key"])));
        }
        var mode = ReadString(o["handshakeMode"]) == "sameForEvery" ? PillHandshakeMode.SameForEvery : PillHandshakeMode.PerStep;
        var earlier = ReadString(o["earlierAccount"]);
        return new Pill(
            name,
            steps,
            earlier is not null && EarlierValues.Contains(earlier, StringComparer.Ordinal) ? earlier : null,
            id,
            mode,
            mode == PillHandshakeMode.SameForEvery ? ReadPoint(o["handshakeAll"]) ?? HandshakePoint.Smart : null);
    }
}
