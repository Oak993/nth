// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-contract; PLAN unit U0). Settings › Ignis and each chat's own mode, kept under
// ONE settings key "ignis" as a compact JSON string (the CapsuleStyleSetting pattern over ISettingsStore). Defaults
// (design Settings › Ignis): Warm + tidy up, Automatic on, Include Hydra heads on, Smart helpers on, Show (i)
// explanations on, Show keep-warm pings off, Keep word for word "Last 12 turns", Update automatically on, first-time
// notice not seen yet. A missing, unreadable or partly unknown value reads as the defaults for what it lacks; it is
// never rewritten until something changes.
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Port.Ignis;

/// <summary>A chat's own mode and Auto, set from the composer popover (Settings only sets the default for new chats).</summary>
public sealed record IgnisChatPreference(IgnisMode Mode, bool Auto);

/// <summary>Everything Settings › Ignis holds, plus each chat's own choice.</summary>
public sealed record IgnisPreferences
{
    /// <summary>"Ignis · Default for new chats".</summary>
    public IgnisMode DefaultMode { get; init; } = IgnisMode.WarmAndTidy;
    /// <summary>"Automatic": Ignis decides when to keep warm and tidy up (default for new chats; each chat has its own Auto).</summary>
    public bool Auto { get; init; } = true;
    /// <summary>"Include Hydra heads".</summary>
    public bool IncludeHeads { get; init; } = true;
    /// <summary>"Smart helpers": look-up helpers and Hydra scouts on the cheaper helper model (Claude only).</summary>
    public bool SmartHelpers { get; init; } = true;
    /// <summary>"Show (i) explanations".</summary>
    public bool ShowExplanations { get; init; } = true;
    /// <summary>"Show keep-warm pings": false hides the "ignis:ping:" timeline items.</summary>
    public bool ShowPings { get; init; }
    /// <summary>"Keep word for word": recent turns a rebuild never shortens (one of <see cref="IgnisSettings.KeepTurnsChoices"/>).</summary>
    public int KeepTurns { get; init; } = IgnisSettings.DefaultKeepTurns;
    /// <summary>"Update automatically" (model points, checked with Marrow's own updates).</summary>
    public bool AutoUpdatePoints { get; init; } = true;
    /// <summary>The first-time "Ignis hides its keep-warm pings" notice was dismissed.</summary>
    public bool NoticeSeen { get; init; }
    /// <summary>Chats whose mode or Auto differs from the defaults, by thread id.</summary>
    public ValueMap<Guid, IgnisChatPreference> Chats { get; init; } = ValueMap<Guid, IgnisChatPreference>.Empty;

    public static IgnisPreferences Default { get; } = new();
}

/// <summary>The "ignis" settings key: read, change, and the JSON it is stored as.</summary>
public sealed class IgnisSettings
{
    public const string SettingsKey = "ignis";
    public const int DefaultKeepTurns = 12;

    /// <summary>The "Keep word for word" choices ("Last {n} turns"). ENGINEERING DECISION: the design shows only 12.</summary>
    public static IReadOnlyList<int> KeepTurnsChoices { get; } = [6, 12, 24];

    private readonly ISettingsStore settings;

    /// <summary>Raised after any change was stored.</summary>
    public event Action? Changed;

    public IgnisSettings(ISettingsStore settings) => this.settings = settings;

    public IgnisPreferences Value
    {
        get => settings.Object(SettingsKey) is string stored ? FromJson(stored) : IgnisPreferences.Default;
        set
        {
            if (value == Value) return;
            settings.Set(SettingsKey, ToJson(value));
            Changed?.Invoke();
        }
    }

    public void Update(Func<IgnisPreferences, IgnisPreferences> change) => Value = change(Value);

    /// <summary>A chat's mode and Auto: its own when set, else the defaults.</summary>
    public IgnisChatPreference ForChat(Guid thread)
    {
        var value = Value;
        return value.Chats.TryGetValue(thread, out var own) ? own : new IgnisChatPreference(value.DefaultMode, value.Auto);
    }

    /// <summary>Stores a chat's own mode and Auto (removed again when it equals the defaults).</summary>
    public void SetChat(Guid thread, IgnisChatPreference preference) => Update(value => value with
    {
        Chats = value.Chats.Mutating(map =>
        {
            if (preference == new IgnisChatPreference(value.DefaultMode, value.Auto)) map.Remove(thread);
            else map[thread] = preference;
        }),
    });

    /// <summary>Forgets a deleted chat's own choice.</summary>
    public void ForgetChat(Guid thread)
    {
        if (Value.Chats.ContainsKey(thread)) Update(value => value with { Chats = value.Chats.Mutating(map => map.Remove(thread)) });
    }

    // ---- JSON ----

    private static readonly (IgnisMode Mode, string Raw)[] ModeNames =
        [(IgnisMode.Off, "off"), (IgnisMode.KeepWarm, "keepWarm"), (IgnisMode.WarmAndTidy, "warmAndTidy")];

    public static string RawValue(IgnisMode mode) => ModeNames.First(pair => pair.Mode == mode).Raw;

    public static IgnisMode? ModeFrom(string? raw)
    {
        foreach (var (mode, name) in ModeNames) if (string.Equals(name, raw, StringComparison.Ordinal)) return mode;
        return null;
    }

    /// <summary>Compact JSON, fixed member order, chats by ordinal thread id.</summary>
    public static string ToJson(IgnisPreferences value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("defaultMode", RawValue(value.DefaultMode));
            writer.WriteBoolean("auto", value.Auto);
            writer.WriteBoolean("heads", value.IncludeHeads);
            writer.WriteBoolean("helpers", value.SmartHelpers);
            writer.WriteBoolean("explanations", value.ShowExplanations);
            writer.WriteBoolean("showPings", value.ShowPings);
            writer.WriteNumber("keepTurns", value.KeepTurns);
            writer.WriteBoolean("autoUpdatePoints", value.AutoUpdatePoints);
            writer.WriteBoolean("noticeSeen", value.NoticeSeen);
            writer.WriteStartObject("chats");
            foreach (var (thread, chat) in value.Chats.Select(pair => (Id: pair.Key.ToString("D"), pair.Value)).OrderBy(pair => pair.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject(thread);
                writer.WriteString("mode", RawValue(chat.Mode));
                writer.WriteBoolean("auto", chat.Auto);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads what it can; anything missing, of the wrong type or unknown keeps its default.</summary>
    public static IgnisPreferences FromJson(string json)
    {
        var result = IgnisPreferences.Default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;
            if (Text(root, "defaultMode") is { } raw && ModeFrom(raw) is { } mode) result = result with { DefaultMode = mode };
            if (Bool(root, "auto") is { } auto) result = result with { Auto = auto };
            if (Bool(root, "heads") is { } heads) result = result with { IncludeHeads = heads };
            if (Bool(root, "helpers") is { } helpers) result = result with { SmartHelpers = helpers };
            if (Bool(root, "explanations") is { } explanations) result = result with { ShowExplanations = explanations };
            if (Bool(root, "showPings") is { } showPings) result = result with { ShowPings = showPings };
            if (root.TryGetProperty("keepTurns", out var keep) && keep.ValueKind == JsonValueKind.Number && keep.TryGetInt32(out var turns) && turns is >= 1 and <= 1000)
                result = result with { KeepTurns = turns };
            if (Bool(root, "autoUpdatePoints") is { } update) result = result with { AutoUpdatePoints = update };
            if (Bool(root, "noticeSeen") is { } seen) result = result with { NoticeSeen = seen };
            if (root.TryGetProperty("chats", out var chats) && chats.ValueKind == JsonValueKind.Object)
            {
                var map = new List<KeyValuePair<Guid, IgnisChatPreference>>();
                foreach (var chat in chats.EnumerateObject())
                {
                    if (!Guid.TryParseExact(chat.Name, "D", out var thread) || chat.Value.ValueKind != JsonValueKind.Object) continue;
                    if (ModeFrom(Text(chat.Value, "mode")) is not { } chatMode || Bool(chat.Value, "auto") is not { } chatAuto) continue;
                    map.Add(new(thread, new IgnisChatPreference(chatMode, chatAuto)));
                }
                result = result with { Chats = new ValueMap<Guid, IgnisChatPreference>(map) };
            }
        }
        catch (JsonException)
        {
            return IgnisPreferences.Default;
        }
        return result;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
