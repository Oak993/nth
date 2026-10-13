// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.3; unit AS-11; ED-NEW-dextra-as11-6). No Swift twin.
// The digest file's codec: a digest, a suggestion and the suggestion ledger as JSON. Enums are written by name and read back strictly: a value this
// build does not know makes that digest or suggestion unreadable (it is dropped, not guessed at), every text is bounded on the way in, and a count
// is bounded too, because anything with write access to the folder may have planted the file. Times are round-trip strings.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

internal static class DigestJson
{
    public const int TextLimit = 4_000;
    public const int SectionsLimit = 16;
    public const int ItemsLimit = 200;
    public const int SuggestionsLimit = 8;
    public const int TaintLimit = 50;

    private const string Round = "O";

    // ---- writing ----

    public static JsonObject ToJson(Digest digest)
    {
        var node = new JsonObject
        {
            ["id"] = digest.Id,
            ["kind"] = digest.Kind.ToString(),
            ["at"] = Time(digest.At),
            ["since"] = Time(digest.Since),
            ["header"] = digest.Header,
            ["sections"] = new JsonArray([.. digest.Sections.Select(section => ToJson(section))]),
            ["suggestions"] = new JsonArray([.. digest.Suggestions.Select(suggestion => (JsonNode)ToJson(suggestion))]),
            ["laneTitle"] = digest.LaneTitle,
            ["spoken"] = digest.Spoken,
            ["taint"] = new JsonArray([.. digest.Taint.Sources.Select(s => (JsonNode)new JsonObject { ["origin"] = s.Origin.ToString(), ["detail"] = s.Detail })]),
        };
        if (digest.Seal is { } seal) node["seal"] = new JsonObject { ["day"] = seal.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["hmac"] = seal.Hmac };
        return node;
    }

    private static JsonNode ToJson(DigestSection section) => new JsonObject
    {
        ["part"] = section.Part.ToString(),
        ["label"] = section.Label,
        ["lead"] = section.Lead,
        ["inline"] = section.Inline,
        ["items"] = new JsonArray([.. section.Items.Select(item => (JsonNode)new JsonObject
        {
            ["text"] = item.Text,
            ["note"] = item.Note,
            ["link"] = new JsonObject { ["kind"] = item.Link.Kind.ToString(), ["id"] = item.Link.Id },
        })]),
    };

    public static JsonObject ToJson(Suggestion suggestion)
    {
        var node = new JsonObject
        {
            ["key"] = suggestion.Key,
            ["kind"] = suggestion.Kind.ToString(),
            ["line"] = suggestion.Line,
            ["cost"] = suggestion.Cost,
            ["plan"] = ToJson(suggestion.Plan),
            ["offeredAt"] = Time(suggestion.OfferedAt),
            ["state"] = suggestion.State.ToString(),
        };
        if (suggestion.LocalOnly) node["localOnly"] = true;
        if (suggestion.Settled is { } settled) node["settled"] = Time(settled);
        return node;
    }

    private static JsonNode ToJson(SuggestionPlan plan)
    {
        var node = new JsonObject { ["do"] = plan.Do.ToString(), ["title"] = plan.Title };
        if (plan.Brief.Length > 0) node["brief"] = plan.Brief;
        if (plan.ProjectId is { } project) node["projectId"] = project.ToString("D");
        if (plan.DutyId is { } duty) node["dutyId"] = duty;
        if (plan.EveryMinutes is { } every) node["everyMinutes"] = every;
        if (plan.Day is { } day) node["day"] = day.ToString();
        if (plan.At is { } at) node["at"] = at.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (plan.Instructions is { } instructions) node["instructions"] = instructions;
        return node;
    }

    public static JsonObject ToJson(SuggestionLedger ledger) => new()
    {
        ["day"] = ledger.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["today"] = new JsonArray([.. ledger.Today.Select(suggestion => (JsonNode)ToJson(suggestion))]),
        ["dismissals"] = new JsonObject(ledger.Dismissals.OrderBy(d => d.Key).Select(d => KeyValuePair.Create(d.Key.ToString(), (JsonNode?)d.Value))),
        ["off"] = new JsonArray([.. ledger.Off.Select(k => (JsonNode)k.ToString())]),
        ["settled"] = new JsonArray([.. ledger.Settled.Select(k => (JsonNode)k)]),
    };

    // ---- reading ----

    public static Digest? ReadDigest(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        try
        {
            if (Text(o["id"], 64) is not { Length: > 0 } id || !Named<DigestKind>(o["kind"], out var kind)) return null;
            if (Moment(o["at"]) is not { } at || Text(o["header"], 200) is not { } header || o["sections"] is not JsonArray sectionNodes) return null;
            var sections = new List<DigestSection>();
            foreach (var sectionNode in sectionNodes.Take(SectionsLimit))
            {
                if (ReadSection(sectionNode) is not { } section) return null;
                sections.Add(section);
            }
            var suggestions = (o["suggestions"] as JsonArray)?.Take(SuggestionsLimit).Select(ReadSuggestion).OfType<Suggestion>().ToList() ?? [];
            var sources = new List<TaintSource>();
            foreach (var source in (o["taint"] as JsonArray)?.Take(TaintLimit) ?? [])
            {
                if (source is JsonObject s && Named<ContentOrigin>(s["origin"], out var origin) && Text(s["detail"], 500) is { } detail) sources.Add(new TaintSource(origin, detail));
            }
            DigestSeal? seal = null;
            if (o["seal"] is JsonObject sealNode)
            {
                if (Text(sealNode["day"], 10) is not { } dayText || !DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    || Text(sealNode["hmac"], 128) is not { Length: > 0 } hmac) return null;
                seal = new DigestSeal(day, hmac);
            }
            return new Digest(id, kind, at, header, sections)
            {
                Since = Moment(o["since"]) ?? at,
                Suggestions = suggestions,
                LaneTitle = Text(o["laneTitle"], 200) ?? "",
                Seal = seal,
                Spoken = Text(o["spoken"], TextLimit) ?? "",
                Taint = sources.Count == 0 ? Taint.Clean : new Taint(new ValueList<TaintSource>(sources)),
            };
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static DigestSection? ReadSection(JsonNode? node)
    {
        if (node is not JsonObject o || !Named<DigestPart>(o["part"], out var part) || Text(o["label"], 200) is not { } label || Text(o["lead"], 200) is not { } lead) return null;
        var inline = o["inline"] is JsonValue flag && flag.TryGetValue<bool>(out var yes) && yes;
        var items = new List<DigestItem>();
        foreach (var itemNode in (o["items"] as JsonArray)?.Take(ItemsLimit) ?? [])
        {
            if (itemNode is not JsonObject item || Text(item["text"], TextLimit) is not { } text || Text(item["note"], TextLimit) is not { } note || item["link"] is not JsonObject link
                || !Named<DigestLinkKind>(link["kind"], out var kind) || Text(link["id"], 200) is not { } id) return null;
            items.Add(new DigestItem(text, new DigestLink(kind, id)) { Note = note });
        }
        return new DigestSection(part, label, lead, items, inline);
    }

    public static Suggestion? ReadSuggestion(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        if (Text(o["key"], 400) is not { Length: > 0 } key || !Named<SuggestionKind>(o["kind"], out var kind) || Text(o["line"], TextLimit) is not { } line
            || Text(o["cost"], 400) is not { } cost || ReadPlan(o["plan"]) is not { } plan || Moment(o["offeredAt"]) is not { } offered
            || !Named<SuggestionState>(o["state"], out var state)) return null;
        return new Suggestion(key, kind, line, cost, plan, offered)
        {
            State = state,
            LocalOnly = o["localOnly"] is JsonValue local && local.TryGetValue<bool>(out var only) && only,
            Settled = Moment(o["settled"]),
        };
    }

    private static SuggestionPlan? ReadPlan(JsonNode? node)
    {
        if (node is not JsonObject o || !Named<SuggestionDo>(o["do"], out var what) || Text(o["title"], 400) is not { } title) return null;
        Guid? project = o["projectId"] is JsonValue p && p.TryGetValue<string>(out var projectText) && Guid.TryParse(projectText, out var guid) ? guid : null;
        DayOfWeek? day = Named<DayOfWeek>(o["day"], out var weekday) ? weekday : null;
        TimeOnly? at = o["at"] is JsonValue t && t.TryGetValue<string>(out var atText) && TimeOnly.TryParseExact(atText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
        int? every = AssistantJson.Number(o["everyMinutes"]) is { } minutes and >= 1 and <= 525_600 ? (int)minutes : null;
        return new SuggestionPlan(what, title)
        {
            Brief = Text(o["brief"], TextLimit) ?? "",
            ProjectId = project,
            DutyId = Text(o["dutyId"], 64),
            EveryMinutes = every,
            Day = day,
            At = at,
            Instructions = Text(o["instructions"], TextLimit),
        };
    }

    public static SuggestionLedger ReadLedger(JsonNode? node)
    {
        if (node is not JsonObject o || Text(o["day"], 10) is not { } dayText
            || !DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return SuggestionLedger.Empty;
        var today = (o["today"] as JsonArray)?.Take(SuggestionLedger.MaxPerDay).Select(ReadSuggestion).OfType<Suggestion>().ToList() ?? [];
        var dismissals = new Dictionary<SuggestionKind, int>();
        if (o["dismissals"] is JsonObject counted)
        {
            foreach (var (name, value) in counted)
                if (Enum.TryParse<SuggestionKind>(name, out var kind) && Enum.IsDefined(kind) && AssistantJson.Number(value) is { } count and >= 0 and <= 10_000) dismissals[kind] = (int)count;
        }
        var off = (o["off"] as JsonArray)?.Select(n => Named<SuggestionKind>(n, out var kind) ? (SuggestionKind?)kind : null).OfType<SuggestionKind>().Distinct().ToList() ?? [];
        var settled = (o["settled"] as JsonArray)?.Select(n => Text(n, 400)).OfType<string>().Where(s => s.Length > 0).TakeLast(SuggestionLedger.SettledKept).ToList() ?? [];
        return new SuggestionLedger
        {
            Day = day,
            Today = new ValueList<Suggestion>(today),
            Dismissals = new ValueMap<SuggestionKind, int>(dismissals),
            Off = new ValueList<SuggestionKind>(off),
            Settled = new ValueList<string>(settled),
        };
    }

    // ---- helpers ----

    public static string Time(DateTimeOffset at) => at.ToString(Round, CultureInfo.InvariantCulture);

    public static DateTimeOffset? Moment(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    /// <summary>The string a node holds, or null when it is not a string or is longer than the limit.</summary>
    public static string? Text(JsonNode? node, int limit) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && text.Length <= limit ? text : null;

    private static bool Named<T>(JsonNode? node, out T value) where T : struct, Enum
    {
        value = default;
        return Text(node, 64) is { } name && System.Enum.TryParse(name, out value) && System.Enum.IsDefined(value);
    }
}
