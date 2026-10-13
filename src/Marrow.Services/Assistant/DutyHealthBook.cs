// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Failed runs never retry endlessly", 15.3; unit AS-15; ED-NEW-dextra-as15-5). No Swift twin.
// What the breaker knows of every duty's runs, kept on disk (duty-health.json) so a duty the breaker paused stays paused after a
// restart. The format carries a version; a file with a newer version is read for nothing and never written over; fields this build
// does not know are written back; a line that is not a duty's health is skipped, never repaired. At most 512 duties are kept.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed class DutyHealthBook
{
    public const int FormatVersion = 1;

    public const int MaxDuties = 512;

    private readonly object gate = new();
    private readonly Dictionary<string, DutyHealth> duties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> unknown = new(StringComparer.Ordinal);
    private JsonObject? root;

    /// <summary>The file's version was newer than this build knows: nothing was read, and nothing is written over it.</summary>
    public bool ReadOnly { get; private set; }

    public event Action? Changed;

    /// <summary>What is known of a duty; a duty never seen is healthy.</summary>
    public DutyHealth Of(string dutyId)
    {
        lock (gate) return duties.TryGetValue(dutyId, out var health) ? health : new DutyHealth(dutyId);
    }

    public IReadOnlyList<DutyHealth> Paused
    {
        get { lock (gate) return [.. duties.Values.Where(h => h.Paused).OrderBy(h => h.PausedAt)]; }
    }

    /// <summary>Stores a duty's health. False when the book is read-only or full (a duty with nothing to remember is dropped, never refused).</summary>
    public bool Set(DutyHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        lock (gate)
        {
            if (ReadOnly) return false;
            if (health.ConsecutiveFailures == 0 && !health.Paused)
            {
                if (!duties.Remove(health.DutyId)) return true;
                unknown.Remove(health.DutyId);
            }
            else
            {
                if (!duties.ContainsKey(health.DutyId) && duties.Count >= MaxDuties) return false;
                duties[health.DutyId] = health;
            }
        }
        Changed?.Invoke();
        return true;
    }

    // ---- the stored document ----

    /// <summary>The document to store, or null when the file read was newer than this build knows (it is left as it was).</summary>
    public JsonObject? ToJson()
    {
        lock (gate)
        {
            if (ReadOnly) return null;
            var document = root is null ? new JsonObject() : (JsonObject)root.DeepClone();
            document["version"] = FormatVersion;
            document["duties"] = new JsonArray([.. duties.Values.OrderBy(h => h.DutyId, StringComparer.Ordinal).Select(Write)]);
            return document;
        }
    }

    private JsonNode Write(DutyHealth health)
    {
        var node = unknown.TryGetValue(health.DutyId, out var extra) ? (JsonObject)extra.DeepClone() : new JsonObject();
        node["duty"] = health.DutyId;
        node["failures"] = health.ConsecutiveFailures;
        node["paused"] = health.Paused;
        if (health.PausedAt is { } at) node["pausedAt"] = at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture); else node.Remove("pausedAt");
        node["reasons"] = new JsonArray([.. health.Reasons.Select(r => (JsonNode)r)]);
        return node;
    }

    public void Load(JsonObject? document)
    {
        lock (gate)
        {
            duties.Clear();
            unknown.Clear();
            root = null;
            ReadOnly = false;
            if (document is null) return;
            if (AssistantJson.Number(document["version"]) is { } number && number > FormatVersion)
            {
                ReadOnly = true;
                return;
            }
            root = (JsonObject)document.DeepClone();
            root.Remove("version");
            root.Remove("duties");
            if (document["duties"] is not JsonArray lines) return;
            foreach (var line in lines.OfType<JsonObject>())
            {
                if (duties.Count >= MaxDuties) break;
                if (Read(line) is not { } health || duties.ContainsKey(health.DutyId)) continue;
                duties[health.DutyId] = health;
                var extra = new JsonObject();
                foreach (var (name, value) in line)
                    if (name is not ("duty" or "failures" or "paused" or "pausedAt" or "reasons")) extra[name] = value?.DeepClone();
                if (extra.Count > 0) unknown[health.DutyId] = extra;
            }
        }
    }

    private static DutyHealth? Read(JsonObject line)
    {
        try
        {
            if (line["duty"] is not JsonValue idNode || !idNode.TryGetValue<string>(out var id) || id.Length == 0 || id.Length > 128) return null;
            if (AssistantJson.Number(line["failures"]) is not { } failures || failures != Math.Floor(failures) || failures is < 0 or > 1_000_000) return null;
            var paused = line["paused"] is JsonValue p && p.TryGetValue<bool>(out var flag) && flag;
            DateTimeOffset? at = line["pausedAt"] is JsonValue a && a.TryGetValue<string>(out var text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
            var reasons = line["reasons"] is JsonArray array
                ? array.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var r) ? DutyBreaker.OneLine(r) : "").Where(r => r.Length > 0).TakeLast(DutyBreaker.MaxConsecutiveFailures).ToArray()
                : [];
            if (failures == 0 && !paused) return null;
            return new DutyHealth(id) { ConsecutiveFailures = (int)failures, Paused = paused, PausedAt = paused ? at : null, Reasons = new ValueList<string>(reasons) };
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or OverflowException)
        {
            return null;
        }
    }
}
