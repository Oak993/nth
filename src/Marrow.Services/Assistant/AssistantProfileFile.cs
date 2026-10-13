// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1, 15.3; unit AS-16; ED-NEW-dextra-as16-9). No Swift twin.
// An assistant's profile as assistants/<id>/profile.json holds it. The file carries a version; a file with a newer version is read for
// nothing and never written over; fields this build does not know are kept and written back; a value of the wrong type is skipped, never
// repaired; the id inside must be the folder's, so a planted file cannot speak for another assistant. Text is read as one clean line and cut
// to the card's limits, because anything with write access to the folder may have edited it.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>A profile read from its file, with the fields this build does not know (written back unchanged).</summary>
public sealed record ProfileDocument(AssistantProfile Profile, JsonObject Extra);

/// <summary>What reading a profile file found: the profile, or why there is none.</summary>
public sealed record ProfileRead(ProfileDocument? Document, string? Problem, bool Newer);

public static class AssistantProfileFile
{
    public const int FormatVersion = 1;

    private static readonly string[] Known =
    [
        "version", "id", "name", "parent", "aliases", "shape", "area", "style", "voice", "homeThread", "template", "created", "retired",
        "sharesBrowserOf", "classRules", "askFirst", "connections", "skills", "duties", "wants", "budget", "model",
    ];

    /// <summary>The document to store for a profile; <paramref name="extra"/> is what the file held that this build does not know.</summary>
    public static JsonObject ToJson(AssistantProfile profile, JsonObject? extra = null)
    {
        var document = extra is null ? new JsonObject() : (JsonObject)extra.DeepClone();
        foreach (var name in Known) document.Remove(name);
        document["version"] = FormatVersion;
        document["id"] = profile.Id;
        document["name"] = profile.Name;
        if (profile.ParentId is { } parent) document["parent"] = parent;
        document["aliases"] = Strings(profile.Aliases);
        if (profile.Shape is { } shape) document["shape"] = AsturShapes.Id(shape);
        document["area"] = profile.Area;
        document["style"] = profile.Style;
        if (profile.Voice is { } voice) document["voice"] = voice;
        if (profile.HomeThread is { } home) document["homeThread"] = home.ToString("D");
        if (profile.Template is { } template) document["template"] = template;
        if (profile.Created is { } created) document["created"] = created.ToString("O", CultureInfo.InvariantCulture);
        if (profile.Retired) document["retired"] = true;
        if (profile.SharesBrowserOf is { } shares) document["sharesBrowserOf"] = shares;
        document["classRules"] = new JsonObject(profile.ClassRules.OrderBy(r => (int)r.Key).Select(r => KeyValuePair.Create(r.Key.ToString(), (JsonNode?)r.Value.ToString())));
        document["askFirst"] = new JsonArray([.. profile.AskFirst.Select(Write)]);
        document["connections"] = Strings(profile.Connections);
        document["skills"] = Strings(profile.Skills);
        document["duties"] = new JsonArray([.. profile.Duties.Select(d => (JsonNode)new JsonObject { ["text"] = d.Text, ["keep"] = d.Keep })]);
        document["wants"] = new JsonArray([.. profile.Wants.Select(w => (JsonNode)new JsonObject { ["kind"] = w.Kind.ToString(), ["text"] = w.Text, ["needsLogin"] = w.NeedsLogin })]);
        if (profile.Budget is { } budget)
        {
            var node = new JsonObject { ["dailyPercent"] = budget.DailyPercent };
            if (budget.DailyDollars is { } dollars) node["dailyDollars"] = dollars;
            document["budget"] = node;
        }
        if (profile.Model is { } model)
        {
            var node = new JsonObject { ["provider"] = model.ProviderId };
            if (model.Model is { } name) node["model"] = name;
            if (model.Account is { } account) node["account"] = account.ToString("D");
            document["model"] = node;
        }
        return document;
    }

    /// <summary>
    /// The profile a file holds. <paramref name="folderId"/> is the assistant folder it was read from. A file of a newer version gives no
    /// profile and <c>Newer</c>; a file that is not a profile of this folder gives a problem line for the team's list.
    /// </summary>
    public static ProfileRead Read(string folderId, JsonObject? document)
    {
        if (document is null) return new ProfileRead(null, $"The profile of {folderId} could not be read.", false);
        if (AssistantJson.Number(document["version"]) is { } version && version > FormatVersion)
            return new ProfileRead(null, $"The profile of {folderId} was saved by a newer nth; it is left as it is.", true);
        var id = Text(document["id"]);
        if (id is null || id != folderId || !AssistantStore.IsId(id)) return new ProfileRead(null, $"The profile in {folderId} names another assistant, so it is ignored.", false);
        var name = Line(document["name"], AsturTeamRules.NameMax);
        if (name.Length == 0) return new ProfileRead(null, $"The profile of {folderId} has no name.", false);
        var parent = Text(document["parent"]);
        if (parent is not null && (!AssistantStore.IsId(parent) || parent == id)) return new ProfileRead(null, $"The profile of {name} names a parent nth cannot use.", false);

        var profile = new AssistantProfile(id, name)
        {
            ParentId = parent,
            Aliases = Lines(document["aliases"], AsturTeamRules.AliasesMax, AsturTeamRules.NameMax),
            Shape = AsturShapes.Parse(Text(document["shape"])),
            Area = Line(document["area"], AsturTeamRules.AreaMax),
            Style = Line(document["style"], AsturTeamRules.StyleMax),
            Voice = Line(document["voice"], AsturTeamRules.NameMax) is { Length: > 0 } voice ? voice : null,
            HomeThread = Text(document["homeThread"]) is { } home && Guid.TryParse(home, out var thread) ? thread : null,
            Template = Text(document["template"]) is { Length: <= 40 } template ? template : null,
            Created = Text(document["created"]) is { } when && DateTimeOffset.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created) ? created : null,
            Retired = Flag(document["retired"]),
            SharesBrowserOf = Text(document["sharesBrowserOf"]) is { } lender && AssistantStore.IsId(lender) && lender != id ? lender : null,
            ClassRules = ClassRules(document["classRules"]),
            AskFirst = AskFirst(document["askFirst"]),
            Connections = Lines(document["connections"], 64, 100),
            Skills = Lines(document["skills"], 64, 100),
            Duties = Duties(document["duties"]),
            Wants = Wants(document["wants"]),
            Budget = Budget(document["budget"]),
            Model = Model(document["model"]),
        };
        if (profile.IsRoot) profile = profile with { Shape = null, Budget = null };
        var extra = new JsonObject();
        foreach (var (field, value) in document) if (Array.IndexOf(Known, field) < 0) extra[field] = value?.DeepClone();
        return new ProfileRead(new ProfileDocument(profile, extra), null, false);
    }

    // ---- reading ----

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 && text.Length <= 200 ? text : null;

    private static string Line(JsonNode? node, int max) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? AsturDrafter.Clip(MemoryRules.Clean(text), max) : "";

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static ValueList<string> Lines(JsonNode? node, int count, int max) =>
        node is JsonArray array
            ? new ValueList<string>(array.Select(item => Line(item, max)).Where(line => line.Length > 0).Take(count))
            : ValueList<string>.Empty;

    private static ValueMap<ActionClass, ClassDefault> ClassRules(JsonNode? node)
    {
        if (node is not JsonObject rules) return ValueMap<ActionClass, ClassDefault>.Empty;
        var map = new Dictionary<ActionClass, ClassDefault>();
        foreach (var (name, value) in rules)
            if (Enum.TryParse<ActionClass>(name, out var actionClass) && Enum.IsDefined(actionClass) && Text(value) is { } rule
                && Enum.TryParse<ClassDefault>(rule, out var choice) && Enum.IsDefined(choice))
                map[actionClass] = choice;
        return new ValueMap<ActionClass, ClassDefault>(map);
    }

    private static JsonNode Write(AskFirstRule rule)
    {
        var node = new JsonObject();
        if (rule.Class is { } actionClass) node["class"] = actionClass.ToString();
        if (rule.Target is { } target) node["target"] = new JsonObject { ["kind"] = target.Kind.ToString(), ["values"] = Strings(target.Values) };
        if (rule.Field is { } field) node["field"] = field;
        return node;
    }

    private static ValueList<AskFirstRule> AskFirst(JsonNode? node)
    {
        if (node is not JsonArray array) return ValueList<AskFirstRule>.Empty;
        var rules = new List<AskFirstRule>();
        foreach (var item in array.OfType<JsonObject>().Take(64))
        {
            ActionClass? actionClass = Text(item["class"]) is { } c && Enum.TryParse<ActionClass>(c, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
            GrantTarget? target = null;
            if (item["target"] is JsonObject t && Text(t["kind"]) is { } kind && Enum.TryParse<GrantTargetKind>(kind, out var targetKind) && Enum.IsDefined(targetKind))
            {
                var values = Lines(t["values"], 16, 500);
                if (values.Count > 0) target = new GrantTarget(targetKind, values);
            }
            var field = Line(item["field"], 100) is { Length: > 0 } f ? f : null;
            var rule = new AskFirstRule(actionClass, target, field);
            if (rule.Class is not null || rule.Target is not null || rule.Field is not null) rules.Add(rule);
        }
        return new ValueList<AskFirstRule>(rules);
    }

    private static ValueList<DutyDraft> Duties(JsonNode? node) =>
        node is JsonArray array
            ? new ValueList<DutyDraft>(array.OfType<JsonObject>().Select(d => new DutyDraft(Line(d["text"], AsturTeamRules.LineMax), !(d["keep"] is JsonValue keep && keep.TryGetValue<bool>(out var k) && !k)))
                .Where(d => d.Text.Length > 0).Take(AsturTeamRules.DutiesMax))
            : ValueList<DutyDraft>.Empty;

    private static ValueList<AsturWant> Wants(JsonNode? node) =>
        node is JsonArray array
            ? new ValueList<AsturWant>(array.OfType<JsonObject>().Select(w => new AsturWant(
                    Text(w["kind"]) is { } kind && Enum.TryParse<WantKind>(kind, out var parsed) && Enum.IsDefined(parsed) ? parsed : WantKind.Connector,
                    Line(w["text"], AsturTeamRules.LineMax), Flag(w["needsLogin"])))
                .Where(w => w.Text.Length > 0).Take(AsturTeamRules.WantsMax))
            : ValueList<AsturWant>.Empty;

    private static AsturBudget? Budget(JsonNode? node)
    {
        if (node is not JsonObject budget || AssistantJson.Number(budget["dailyPercent"]) is not { } percent || !double.IsFinite(percent)) return null;
        decimal? dollars = AssistantJson.Number(budget["dailyDollars"]) is { } d && double.IsFinite(d) && d > 0 && d <= AsturTeamRules.DollarsMax ? (decimal)d : null;
        return new AsturBudget(Math.Clamp(percent, 1, 100), dollars);
    }

    private static AsturModel? Model(JsonNode? node) =>
        node is JsonObject model && Text(model["provider"]) is { } provider
            ? new AsturModel(provider, Text(model["model"]), Text(model["account"]) is { } account && Guid.TryParse(account, out var id) ? id : null)
            : null;

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode)JsonValue.Create(v)!)]);
}
