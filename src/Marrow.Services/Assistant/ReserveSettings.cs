// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.3, 11.5, 11.7; unit AS-13; ED-NEW-dextra-as13-7). No Swift twin.
// The owner's standing reserve rules and the two switches of Settings > Reserves: "Hold reserves until the reset" (easing off)
// and "Always ask before starting". Held in memory, read from and written to reserves.json through the store. The format
// carries a version; a file with a newer version is read for nothing and never written over; fields this build does not know
// are written back; a line that is not a rule is skipped, never repaired.
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed class ReserveSettings
{
    public const int FormatVersion = 1;

    /// <summary>The most standing rules kept (a runaway list never grows the file without bound).</summary>
    public const int MaxRules = 64;

    private readonly object gate = new();
    private readonly List<ReserveRule> rules = [];
    private readonly Dictionary<string, JsonObject> unknown = new(StringComparer.Ordinal);
    private JsonObject? root;

    /// <summary>The file's version was newer than this build knows: nothing was read, and nothing is written over it.</summary>
    public bool ReadOnly { get; private set; }

    /// <summary>"Hold reserves until the reset" (Settings > Reserves): standing floors do not ease near a reset.</summary>
    public bool HoldReserves { get; private set; }

    /// <summary>"Always ask before starting": every plan waits for the owner's Start (11.4).</summary>
    public bool AlwaysAskBeforeStarting { get; private set; }

    public event Action? Changed;

    /// <summary>The standing rules, in the order the owner added them.</summary>
    public IReadOnlyList<ReserveRule> Rules
    {
        get { lock (gate) return [.. rules]; }
    }

    /// <summary>Adds a standing rule (a task rule is never stored). Null when added, else why not.</summary>
    public string? Add(ReserveRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.Lifetime != ReserveLifetime.Standing) return "Only a standing rule is kept.";
        if (!double.IsFinite(rule.KeepPercent) && rule.KeepDollars is null) return "Say how much to keep.";
        lock (gate)
        {
            if (ReadOnly) return "These settings were saved by a newer version; this one leaves them as they are.";
            if (rules.Count >= MaxRules) return $"At most {MaxRules} standing rules are kept.";
            var stored = rule.Normalized() with { Id = string.IsNullOrWhiteSpace(rule.Id) || rules.Any(r => r.Id == rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id };
            rules.Add(stored);
        }
        Changed?.Invoke();
        return null;
    }

    public bool Remove(string id)
    {
        lock (gate)
        {
            if (ReadOnly || rules.RemoveAll(r => r.Id == id) == 0) return false;
            unknown.Remove(id);
        }
        Changed?.Invoke();
        return true;
    }

    public bool SetHoldReserves(bool value)
    {
        lock (gate)
        {
            if (ReadOnly) return false;
            HoldReserves = value;
        }
        Changed?.Invoke();
        return true;
    }

    public bool SetAlwaysAskBeforeStarting(bool value)
    {
        lock (gate)
        {
            if (ReadOnly) return false;
            AlwaysAskBeforeStarting = value;
        }
        Changed?.Invoke();
        return true;
    }

    // ---- the stored document ----

    /// <summary>The document to store, or null when the file read was newer than this build knows.</summary>
    public JsonObject? ToJson()
    {
        lock (gate)
        {
            if (ReadOnly) return null;
            var document = root is null ? new JsonObject() : (JsonObject)root.DeepClone();
            document["version"] = FormatVersion;
            document["holdReserves"] = HoldReserves;
            document["alwaysAskBeforeStarting"] = AlwaysAskBeforeStarting;
            document["rules"] = new JsonArray([.. rules.Select(Write)]);
            return document;
        }
    }

    private JsonNode Write(ReserveRule rule)
    {
        var node = unknown.TryGetValue(rule.Id, out var extra) ? (JsonObject)extra.DeepClone() : new JsonObject();
        node["id"] = rule.Id;
        node["lifetime"] = rule.Lifetime.ToString();
        node["keepPercent"] = rule.KeepPercent;
        if (rule.KeepDollars is { } dollars) node["keepDollars"] = dollars; else node.Remove("keepDollars");
        node["basis"] = rule.Basis.ToString();
        node["windows"] = (int)rule.Windows;
        node["scope"] = new JsonObject
        {
            ["kind"] = rule.Scope.Kind.ToString(),
            ["accounts"] = new JsonArray([.. rule.Scope.AccountNames.Select(n => (JsonNode)n)]),
            ["provider"] = rule.Scope.ProviderId,
            ["assistant"] = rule.Scope.AssistantId,
        };
        return node;
    }

    /// <summary>Loads a stored document; a newer version makes the settings read-only and loads nothing.</summary>
    public void Load(JsonObject? document)
    {
        lock (gate)
        {
            rules.Clear();
            unknown.Clear();
            root = null;
            ReadOnly = false;
            HoldReserves = false;
            AlwaysAskBeforeStarting = false;
            if (document is null) return;
            if (AssistantJson.Number(document["version"]) is { } number && number > FormatVersion)
            {
                ReadOnly = true;
                return;
            }
            root = (JsonObject)document.DeepClone();
            foreach (var name in new[] { "version", "holdReserves", "alwaysAskBeforeStarting", "rules" }) root.Remove(name);
            HoldReserves = Flag(document["holdReserves"]);
            AlwaysAskBeforeStarting = Flag(document["alwaysAskBeforeStarting"]);
            if (document["rules"] is not JsonArray lines) return;
            foreach (var line in lines.OfType<JsonObject>())
            {
                if (rules.Count >= MaxRules) break;
                if (Read(line) is not { } rule || rules.Any(r => r.Id == rule.Id)) continue;
                rules.Add(rule);
                var extra = new JsonObject();
                foreach (var (name, value) in line)
                    if (name is not ("id" or "lifetime" or "keepPercent" or "keepDollars" or "basis" or "windows" or "scope")) extra[name] = value?.DeepClone();
                if (extra.Count > 0) unknown[rule.Id] = extra;
            }
        }
    }

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static ReserveRule? Read(JsonObject line)
    {
        try
        {
            if (line["id"] is not JsonValue idNode || !idNode.TryGetValue<string>(out var id) || id.Length == 0 || id.Length > 64) return null;
            if (line["lifetime"] is not JsonValue life || !life.TryGetValue<string>(out var lifeText) || !Enum.TryParse<ReserveLifetime>(lifeText, out var lifetime)
                || lifetime != ReserveLifetime.Standing) return null;
            if (line["basis"] is not JsonValue basisNode || !basisNode.TryGetValue<string>(out var basisText) || !Enum.TryParse<ReserveBasis>(basisText, out var basis)) return null;
            if (AssistantJson.Number(line["windows"]) is not { } windowsNumber || windowsNumber != Math.Floor(windowsNumber) || windowsNumber is < 0 or > 15) return null;
            var windows = (int)windowsNumber;
            var percent = AssistantJson.Number(line["keepPercent"]) ?? double.NaN;
            decimal? dollars = AssistantJson.Number(line["keepDollars"]) is { } money && double.IsFinite(money) ? (decimal)money : null;
            if (dollars is null && !double.IsFinite(percent)) return null;
            if (line["scope"] is not JsonObject scopeNode || scopeNode["kind"] is not JsonValue kindNode || !kindNode.TryGetValue<string>(out var kindText)
                || !Enum.TryParse<ReserveScopeKind>(kindText, out var kind)) return null;
            var names = scopeNode["accounts"] is JsonArray array
                ? array.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var n) ? n : "").Where(n => n.Length > 0).ToArray() : [];
            var scope = new ReserveScope(kind)
            {
                AccountNames = new ValueList<string>(names),
                ProviderId = scopeNode["provider"] is JsonValue pv && pv.TryGetValue<string>(out var provider) ? provider : null,
                AssistantId = scopeNode["assistant"] is JsonValue av && av.TryGetValue<string>(out var assistant) ? assistant : null,
            };
            return new ReserveRule(id, scope, lifetime) { KeepPercent = double.IsFinite(percent) ? percent : 0, KeepDollars = dollars, Basis = basis, Windows = (ReserveWindows)windows }.Normalized();
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
