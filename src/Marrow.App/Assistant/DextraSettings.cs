// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.1 the sidebar row, 2.6 first run, 4.3 the delegate responder, 5.4 the
// brain's reading switches, 12.2 grants, 12.6 the stop chord; ED-NEW-dextra-as8-8). No Swift twin.
// Everything Settings › Assistant and the first-run card hold, under ONE settings key "dextra" as a compact JSON string
// (the IgnisSettings pattern over ISettingsStore). Defaults are the safe ones of 2.6: Automatic brain, summaries of chats on
// the same provider on, other providers off, Answer questions for me off, no first duty, Ctrl+Alt+Esc. A missing,
// unreadable or partly unknown value reads as the defaults for what it lacks; it is never rewritten until something changes.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Avalonia.Threading;

namespace Marrow.App.Assistant;

/// <summary>"Answer questions for me" (4.3): off, or on for this project only, or everywhere.</summary>
public enum DelegateScope { Off, ThisProject, Everywhere }

/// <summary>Everything Settings › Assistant holds.</summary>
public sealed record DextraPreferences
{
    /// <summary>Dextra's identity (its AssistantID); made once on first use and kept.</summary>
    public Guid? Id { get; init; }

    /// <summary>"Dextra" on or off in Settings. Off: the controller is stopped and the home only says so.</summary>
    public bool On { get; init; } = true;

    /// <summary>The first-run card was finished (2.6). Nothing outward happens before it.</summary>
    public bool FirstRunDone { get; init; }

    /// <summary>The brain's provider; null is Automatic (the strongest model on the account with the most headroom).</summary>
    public ProviderKind? BrainProvider { get; init; }

    /// <summary>The brain's account, or null for the provider's first one.</summary>
    public Guid? BrainAccount { get; init; }

    /// <summary>The brain's model id, or null for the provider's default.</summary>
    public string? BrainModel { get; init; }

    /// <summary>"Summaries of chats on the same provider" (5.4): on by default.</summary>
    public bool ReadSameProvider { get; init; } = true;

    /// <summary>"Also chats on other providers" (5.4): off by default.</summary>
    public bool ReadOtherProviders { get; init; }

    /// <summary>"Answer questions for me" (4.3). Until AS-7 passes every approval still goes to the queue.</summary>
    public DelegateScope Delegate { get; init; } = DelegateScope.Off;

    /// <summary>The project "This project only" means; null is the project the home thread works in.</summary>
    public Guid? DelegateProject { get; init; }

    /// <summary>The first duty picked on the first-run card (one of <see cref="DextraSettings.FirstDuties"/>), or null.</summary>
    public string? FirstDuty { get; init; }

    /// <summary>The stop chord's text; null is "Ctrl+Alt+Esc".</summary>
    public string? StopChord { get; init; }

    /// <summary>The grants the owner made from cards, kept across launches (revoked or spent ones are not kept).</summary>
    public ValueList<Grant> Grants { get; init; } = ValueList<Grant>.Empty;

    /// <summary>The sidebar's "Missions" group under the Assistant row is collapsed (2.1: collapsed by default, remembered).</summary>
    public bool MissionsCollapsed { get; init; } = true;

    public static DextraPreferences Default { get; } = new();
}

/// <summary>The "dextra" settings key: read, change, and the JSON it is stored as.</summary>
/// <summary>
/// The owner's Dextra choices. <see cref="Value"/> is observable, so a switch bound to one choice redraws when another view (the
/// first-run card, a duty switch, the sidebar row) changes it; the notification is raised on the UI thread.
/// </summary>
public sealed class DextraSettings : ObservableModel
{
    public const string SettingsKey = "dextra";
    public const string DefaultStopChord = "Ctrl+Alt+Esc";

    /// <summary>The three first duties the card offers (2.6), by id and words.</summary>
    public static IReadOnlyList<(string Id, string Text, string Detail)> FirstDuties { get; } =
    [
        ("digest", "Morning digest", "Every morning at 8: what finished, what needs you, what it costs."),
        ("watch-builds", "Watch failing builds", "Tells you when a chat's build or tests fail, with the first error."),
        ("tidy-chats", "Weekly chat tidy-up", "Each Friday: chats idle for a week, to archive or keep."),
    ];

    private readonly ISettingsStore settings;
    private int revision;

    /// <summary>Raised after any change was stored.</summary>
    public event Action? Changed;

    public DextraSettings(ISettingsStore settings) => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public DextraPreferences Value
    {
        get
        {
            _ = Read(revision);
            return settings.Object(SettingsKey) is string stored ? FromJson(stored) : DextraPreferences.Default;
        }
        set
        {
            if (value == Value) return;
            settings.Set(SettingsKey, ToJson(value));
            if (Dispatcher.UIThread.CheckAccess()) Touch();
            else Dispatcher.UIThread.Post(Touch);
            Changed?.Invoke();
        }
    }

    public void Update(Func<DextraPreferences, DextraPreferences> change) => Value = change(Value);

    private void Touch() => Set(ref revision, revision + 1, property: nameof(Value));

    /// <summary>Dextra's identity: the stored one, or a new one stored now.</summary>
    public Guid EnsureId()
    {
        if (Value.Id is { } id) return id;
        var made = Guid.NewGuid();
        Update(value => value with { Id = made });
        return made;
    }

    /// <summary>The chord the stop key registers.</summary>
    public string Chord => string.IsNullOrWhiteSpace(Value.StopChord) ? DefaultStopChord : Value.StopChord!;

    /// <summary>The brain's side of the privacy rules (5.4) for the bus, from the owner's two switches.</summary>
    public FeedBrain Brain(ProviderKind? provider) => provider is null
        ? FeedBrain.Unknown with { SameProviderSummaries = Value.ReadSameProvider, AlsoOtherProviders = Value.ReadOtherProviders }
        : new FeedBrain(provider, RunsLocally: provider == ProviderKind.Local, Value.ReadSameProvider, Value.ReadOtherProviders);

    /// <summary>Keeps the grants worth keeping at <paramref name="now"/>: not revoked, not spent, not past their end.</summary>
    public void KeepGrants(IEnumerable<Grant> grants, DateTimeOffset now)
    {
        var kept = new ValueList<Grant>(grants.Where(g => Keeps(g, now)).OrderBy(g => g.Created).ThenBy(g => g.Id));
        if (!kept.Equals(Value.Grants)) Update(value => value with { Grants = kept });
    }

    /// <summary>Whether a grant is worth keeping across launches.</summary>
    public static bool Keeps(Grant grant, DateTimeOffset now) => grant.RevokedAt is null && grant.Scope.Kind switch
    {
        GrantScopeKind.Once => grant.UsedBy.Count == 0,
        GrantScopeKind.Until => grant.Scope.Until is { } end && now < end,
        _ => true,
    };

    // ---- JSON ----

    private static readonly (DelegateScope Scope, string Raw)[] DelegateNames =
        [(DelegateScope.Off, "off"), (DelegateScope.ThisProject, "project"), (DelegateScope.Everywhere, "everywhere")];

    /// <summary>Compact JSON, fixed member order; defaults are left out.</summary>
    public static string ToJson(DextraPreferences value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (value.Id is { } id) writer.WriteString("id", id.ToString("D"));
            writer.WriteBoolean("on", value.On);
            writer.WriteBoolean("firstRunDone", value.FirstRunDone);
            if (value.BrainProvider is { } provider) writer.WriteString("brainProvider", provider.ToString());
            if (value.BrainAccount is { } account) writer.WriteString("brainAccount", account.ToString("D"));
            if (value.BrainModel is { } model) writer.WriteString("brainModel", model);
            writer.WriteBoolean("readSameProvider", value.ReadSameProvider);
            writer.WriteBoolean("readOtherProviders", value.ReadOtherProviders);
            writer.WriteString("delegate", DelegateNames.First(pair => pair.Scope == value.Delegate).Raw);
            if (value.DelegateProject is { } project) writer.WriteString("delegateProject", project.ToString("D"));
            if (value.FirstDuty is { } duty) writer.WriteString("firstDuty", duty);
            if (value.StopChord is { } chord) writer.WriteString("stopChord", chord);
            writer.WriteBoolean("missionsCollapsed", value.MissionsCollapsed);
            writer.WriteStartArray("grants");
            foreach (var grant in value.Grants) WriteGrant(writer, grant);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads what it can; anything missing, of the wrong type or unknown keeps its default. A damaged grant is dropped.</summary>
    public static DextraPreferences FromJson(string json)
    {
        var result = DextraPreferences.Default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;
            if (GuidOf(root, "id") is { } id) result = result with { Id = id };
            if (Bool(root, "on") is { } on) result = result with { On = on };
            if (Bool(root, "firstRunDone") is { } done) result = result with { FirstRunDone = done };
            if (Text(root, "brainProvider") is { } raw && Enum.TryParse<ProviderKind>(raw, ignoreCase: false, out var provider) && Enum.IsDefined(provider))
                result = result with { BrainProvider = provider };
            if (GuidOf(root, "brainAccount") is { } account) result = result with { BrainAccount = account };
            if (Text(root, "brainModel") is { Length: > 0 } model) result = result with { BrainModel = model };
            if (Bool(root, "readSameProvider") is { } same) result = result with { ReadSameProvider = same };
            if (Bool(root, "readOtherProviders") is { } other) result = result with { ReadOtherProviders = other };
            if (Text(root, "delegate") is { } scope && DelegateNames.FirstOrDefault(pair => pair.Raw == scope) is { Raw: not null } named)
                result = result with { Delegate = named.Scope };
            if (GuidOf(root, "delegateProject") is { } project) result = result with { DelegateProject = project };
            if (Text(root, "firstDuty") is { } duty && FirstDuties.Any(d => d.Id == duty)) result = result with { FirstDuty = duty };
            if (Text(root, "stopChord") is { Length: > 0 } chord) result = result with { StopChord = chord };
            if (Bool(root, "missionsCollapsed") is { } collapsed) result = result with { MissionsCollapsed = collapsed };
            if (root.TryGetProperty("grants", out var grants) && grants.ValueKind == JsonValueKind.Array)
                result = result with { Grants = new ValueList<Grant>(grants.EnumerateArray().Select(ReadGrant).OfType<Grant>()) };
        }
        catch (JsonException)
        {
            return DextraPreferences.Default;
        }
        return result;
    }

    private static void WriteGrant(Utf8JsonWriter writer, Grant grant)
    {
        writer.WriteStartObject();
        writer.WriteString("id", grant.Id.ToString("D"));
        writer.WriteString("assistant", grant.AssistantId);
        writer.WriteString("class", grant.Class.ToString());
        writer.WriteString("targetKind", grant.Target.Kind.ToString());
        Strings(writer, "target", grant.Target.Values);
        writer.WriteString("scope", grant.Scope.Kind.ToString());
        if (grant.Scope.MissionId is { } mission) writer.WriteString("mission", mission.ToString("D"));
        if (grant.Scope.Until is { } until) writer.WriteString("until", until.ToString("O", CultureInfo.InvariantCulture));
        if (grant.Fields.Count > 0) Strings(writer, "fields", grant.Fields);
        if (grant.Excluded.Count > 0) Strings(writer, "excluded", grant.Excluded);
        if (grant.Conditions.Count > 0) Strings(writer, "conditions", grant.Conditions);
        if (grant.FromDecision is { } decision) writer.WriteString("fromDecision", decision.ToString("D"));
        writer.WriteString("created", grant.Created.ToString("O", CultureInfo.InvariantCulture));
        if (grant.RevokedAt is { } revoked) writer.WriteString("revokedAt", revoked.ToString("O", CultureInfo.InvariantCulture));
        if (grant.UsedBy.Count > 0) Strings(writer, "usedBy", grant.UsedBy);
        writer.WriteEndObject();
    }

    private static Grant? ReadGrant(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (GuidOf(element, "id") is not { } id || Text(element, "assistant") is not { Length: > 0 } assistant) return null;
        if (!Enum.TryParse<ActionClass>(Text(element, "class"), ignoreCase: false, out var actionClass) || !Enum.IsDefined(actionClass)) return null;
        if (!Enum.TryParse<GrantTargetKind>(Text(element, "targetKind"), ignoreCase: false, out var kind) || !Enum.IsDefined(kind)) return null;
        if (!Enum.TryParse<GrantScopeKind>(Text(element, "scope"), ignoreCase: false, out var scopeKind) || !Enum.IsDefined(scopeKind)) return null;
        var mission = GuidOf(element, "mission");
        var until = Time(element, "until");
        if (scopeKind == GrantScopeKind.Mission && mission is null) return null;
        if (scopeKind == GrantScopeKind.Until && until is null) return null;
        var grant = new Grant(id, assistant, actionClass, new GrantTarget(kind, StringsOf(element, "target")), new GrantScope(scopeKind, mission, until))
        {
            Fields = StringsOf(element, "fields"),
            Excluded = StringsOf(element, "excluded"),
            Conditions = StringsOf(element, "conditions"),
            FromDecision = GuidOf(element, "fromDecision"),
            Created = Time(element, "created") ?? DateTimeOffset.MinValue,
            RevokedAt = Time(element, "revokedAt"),
            UsedBy = StringsOf(element, "usedBy"),
        };
        return GrantRules.Invalid(grant) is null ? grant : null;
    }

    private static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static ValueList<string> StringsOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? new ValueList<string>(array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!))
            : ValueList<string>.Empty;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static Guid? GuidOf(JsonElement element, string name) =>
        Text(element, name) is { } raw && Guid.TryParseExact(raw, "D", out var id) && id != Guid.Empty ? id : null;

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        Text(element, name) is { } raw && DateTimeOffset.TryParseExact(raw, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
}
