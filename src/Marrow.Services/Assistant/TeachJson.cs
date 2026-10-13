// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.3; unit AS-19; ED-NEW-dextra-as19-1). No Swift twin.
// How a recording is written and read back: events, narration and frame lines as compact JSON lines; the manifest and the draft as
// versioned documents read strictly (a repeated name, a wrong type or a missing field is refused, never guessed at). A field this
// build does not know survives a load and a save, on the manifest and on the draft; a file written by a newer build (a higher version)
// is read for display and never written back. Event lines are append-only and are never rewritten, so a newer build's extra fields on
// them stay where they are.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public static class TeachJson
{
    private static readonly HashSet<string> KnownInfo = new(StringComparer.Ordinal)
    {
        "version", "id", "assistant", "started", "state", "elapsedMs", "events", "frames", "frameBytes", "narration", "source", "changed", "ended",
        "keep", "skillName", "skillId",
    };

    private static readonly HashSet<string> KnownDraft = new(StringComparer.Ordinal)
    {
        "version", "recording", "name", "description", "steps", "inputs", "decisions", "obstacles", "questions", "notes", "modelWritten",
    };

    // ---- lines ----

    public static string EventLine(TeachEvent e)
    {
        var line = new JsonObject
        {
            ["seq"] = e.Seq,
            ["at"] = Seconds(e.At),
            ["kind"] = e.Kind.ToString(),
            ["verb"] = e.Verb,
        };
        if (e.Element is { } element) line["el"] = ElementNode(element);
        if (e.Text is not null) line["text"] = e.Text;
        if (e.Length != 0) line["len"] = e.Length;
        if (e.Secret) line["secret"] = true;
        if (e.Url is not null) line["url"] = e.Url;
        if (e.Frame is not null) line["frame"] = e.Frame;
        return line.ToJsonString();
    }

    public static TeachEvent? ReadEvent(string? line)
    {
        if (AssistantJson.ParseObject(line) is not { } o) return null;
        try
        {
            if (Whole(o["seq"]) is not { } seq || seq < 0 || Number(o["at"]) is not { } at || at < 0 || at > 24 * 3600 * 365) return null;
            if (!Enum.TryParse<TeachEventKind>(Text(o, "kind"), out var kind) || !Enum.IsDefined(kind)) return null;
            return new TeachEvent((int)seq, TimeSpan.FromSeconds(at), kind)
            {
                Verb = Text(o, "verb") ?? "",
                Element = o["el"] is JsonObject el ? ReadElement(el) : null,
                Text = Text(o, "text"),
                Length = (int)Math.Clamp(Whole(o["len"]) ?? 0, 0, int.MaxValue),
                Secret = o["secret"] is JsonValue s && s.TryGetValue<bool>(out var secret) && secret,
                Url = Text(o, "url"),
                Frame = Text(o, "frame") is { } frame && AssistantStore.IsFrameName(frame) ? frame : null,
            };
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException) { return null; }
    }

    public static string NarrationLine(TeachNarrationLine line) =>
        new JsonObject { ["start"] = Seconds(line.Start), ["end"] = Seconds(line.End), ["text"] = line.Text }.ToJsonString();

    public static TeachNarrationLine? ReadNarration(string? line)
    {
        if (AssistantJson.ParseObject(line) is not { } o) return null;
        try
        {
            if (Number(o["start"]) is not { } start || Number(o["end"]) is not { } end || start < 0 || end < start || Text(o, "text") is not { Length: > 0 } text) return null;
            return new TeachNarrationLine(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException) { return null; }
    }

    public static string FrameLine(TimeSpan at, string file) => new JsonObject { ["at"] = Seconds(at), ["file"] = file }.ToJsonString();

    // ---- the manifest ----

    public static JsonObject InfoDocument(TeachRecordingInfo info)
    {
        var o = new JsonObject
        {
            ["version"] = Math.Max(info.Version, TeachRecordingInfo.CurrentVersion),
            ["id"] = info.Id,
            ["assistant"] = info.AssistantId,
            ["started"] = Time(info.Started),
            ["state"] = info.State.ToString(),
            ["elapsedMs"] = (long)info.Elapsed.TotalMilliseconds,
            ["events"] = info.Events,
            ["frames"] = info.Frames,
            ["frameBytes"] = info.FrameBytes,
            ["narration"] = info.Narration,
            ["source"] = info.Source.ToString(),
            ["changed"] = Time(info.Changed),
        };
        if (info.Ended is { } ended) o["ended"] = ended.ToString();
        if (info.Keep) o["keep"] = true;
        if (info.SkillName is not null) o["skillName"] = info.SkillName;
        if (info.SkillId is not null) o["skillId"] = info.SkillId;
        foreach (var (name, json) in info.Unknown)
            if (!KnownInfo.Contains(name) && AssistantJson.Parse(json) is { } value) o[name] = value;
        return o;
    }

    /// <summary>The manifest, or null when the document does not fit (it is then left as it is and listed as a problem).</summary>
    public static TeachRecordingInfo? ReadInfo(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        try
        {
            if (Text(o, "id") is not { } id || !AssistantStore.IsId(id) || Text(o, "assistant") is not { } assistant || !AssistantStore.IsId(assistant)) return null;
            if (ParseTime(Text(o, "started")) is not { } started || !Enum.TryParse<TeachRecordingState>(Text(o, "state"), out var state) || !Enum.IsDefined(state)) return null;
            var version = o["version"] is { } v && Whole(v) is { } whole ? (int)Math.Clamp(whole, 0, int.MaxValue) : 0;
            TeachStopReason? ended = null;
            if (Text(o, "ended") is { } reason)
            {
                if (!Enum.TryParse<TeachStopReason>(reason, out var parsed) || !Enum.IsDefined(parsed)) return null;
                ended = parsed;
            }
            var source = Enum.TryParse<TeachSource>(Text(o, "source"), out var given) && Enum.IsDefined(given) ? given : TeachSource.Direct;
            return new TeachRecordingInfo(id, assistant, started, state)
            {
                Version = version == 0 ? TeachRecordingInfo.CurrentVersion : version,
                Elapsed = TimeSpan.FromMilliseconds(Math.Clamp(Whole(o["elapsedMs"]) ?? 0, 0, (long)TimeSpan.FromDays(2).TotalMilliseconds)),
                Events = (int)Math.Clamp(Whole(o["events"]) ?? 0, 0, int.MaxValue),
                Frames = (int)Math.Clamp(Whole(o["frames"]) ?? 0, 0, int.MaxValue),
                FrameBytes = Math.Max(0, Whole(o["frameBytes"]) ?? 0),
                Narration = (int)Math.Clamp(Whole(o["narration"]) ?? 0, 0, int.MaxValue),
                Source = source,
                Changed = ParseTime(Text(o, "changed")) ?? started,
                Ended = ended,
                Keep = o["keep"] is JsonValue k && k.TryGetValue<bool>(out var keep) && keep,
                SkillName = Text(o, "skillName"),
                SkillId = Text(o, "skillId"),
                Unknown = Unknown(o, KnownInfo),
            };
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException) { return null; }
    }

    // ---- the draft ----

    public static JsonObject DraftDocument(TeachDraft draft)
    {
        var o = new JsonObject
        {
            ["version"] = Math.Max(draft.Version, TeachDraft.CurrentVersion),
            ["recording"] = draft.RecordingId,
            ["name"] = draft.Name,
            ["description"] = draft.Description,
            ["modelWritten"] = draft.ModelWritten,
            ["steps"] = new JsonArray([.. draft.Steps.Select(StepNode)]),
            ["inputs"] = new JsonArray([.. draft.Parameters.Select(p => (JsonNode)new JsonObject { ["name"] = p.Name, ["example"] = p.Example, ["where"] = p.Where })]),
            ["decisions"] = new JsonArray([.. draft.Decisions.Select(d => (JsonNode)new JsonObject { ["text"] = d.Text, ["reason"] = d.Reason })]),
            ["obstacles"] = new JsonArray([.. draft.Obstacles.Select(b => (JsonNode)new JsonObject { ["text"] = b.Text, ["fix"] = b.Fix })]),
            ["questions"] = new JsonArray([.. draft.Questions.Select(QuestionNode)]),
            ["notes"] = new JsonArray([.. draft.Notes.Select(n => (JsonNode)JsonValue.Create(n)!)]),
        };
        foreach (var (name, json) in draft.Unknown)
            if (!KnownDraft.Contains(name) && AssistantJson.Parse(json) is { } value) o[name] = value;
        return o;
    }

    public static TeachDraft? ReadDraft(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        try
        {
            if (Text(o, "recording") is not { } recording || !AssistantStore.IsId(recording) || Text(o, "name") is not { } name || Text(o, "description") is not { } description) return null;
            var version = o["version"] is { } v && Whole(v) is { } whole ? (int)Math.Clamp(whole, 0, int.MaxValue) : 0;
            var steps = new List<TeachStep>();
            foreach (var item in o["steps"] as JsonArray ?? new JsonArray())
            {
                if (item is not JsonObject so || ReadStep(so) is not { } step) return null;
                steps.Add(step);
            }
            var questions = new List<TeachQuestion>();
            foreach (var item in o["questions"] as JsonArray ?? new JsonArray())
            {
                if (item is not JsonObject qo || ReadQuestion(qo) is not { } question) return null;
                questions.Add(question);
            }
            return new TeachDraft(recording, name, description)
            {
                Version = version == 0 ? TeachDraft.CurrentVersion : version,
                ModelWritten = o["modelWritten"] is JsonValue m && m.TryGetValue<bool>(out var written) && written,
                Steps = new ValueList<TeachStep>(steps),
                Parameters = new ValueList<TeachParameter>(Objects(o["inputs"]).Select(p => new TeachParameter(Text(p, "name") ?? "", Text(p, "example") ?? "", Text(p, "where") ?? ""))),
                Decisions = new ValueList<TeachDecision>(Objects(o["decisions"]).Select(d => new TeachDecision(Text(d, "text") ?? "", Text(d, "reason") ?? ""))),
                Obstacles = new ValueList<TeachObstacle>(Objects(o["obstacles"]).Select(b => new TeachObstacle(Text(b, "text") ?? "", Text(b, "fix") ?? ""))),
                Questions = new ValueList<TeachQuestion>(questions),
                Notes = new ValueList<string>((o["notes"] as JsonArray ?? new JsonArray()).Select(n => n is JsonValue s && s.TryGetValue<string>(out var text) ? text : "").Where(t => t.Length > 0)),
                Unknown = Unknown(o, KnownDraft),
            };
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException) { return null; }
    }

    private static JsonNode StepNode(TeachStep step)
    {
        var o = new JsonObject { ["n"] = step.Number, ["text"] = step.Text, ["kind"] = step.Kind.ToString() };
        if (step.App is not null) o["app"] = step.App;
        if (step.Guard is { } guard) o["guard"] = new JsonObject { ["class"] = guard.Class.ToString(), ["reason"] = guard.Reason };
        if (step.Flagged) o["flagged"] = true;
        if (step.Said is not null) o["said"] = step.Said;
        o["from"] = new JsonArray([.. step.From.Select(seq => (JsonNode)JsonValue.Create(seq)!)]);
        if (step.Frame is not null) o["frame"] = step.Frame;
        return o;
    }

    private static TeachStep? ReadStep(JsonObject o)
    {
        if (Whole(o["n"]) is not { } number || number < 1 || number > TeachLimits.Steps || Text(o, "text") is not { } text) return null;
        if (!Enum.TryParse<TeachEventKind>(Text(o, "kind"), out var kind) || !Enum.IsDefined(kind)) return null;
        TeachGuardFinding? guard = null;
        if (o["guard"] is JsonObject g)
        {
            if (!Enum.TryParse<ActionClass>(Text(g, "class"), out var actionClass) || !Enum.IsDefined(actionClass) || Text(g, "reason") is not { } reason) return null;
            guard = new TeachGuardFinding(actionClass, reason);
        }
        else if (o["guard"] is not null) return null;
        var from = new List<int>();
        foreach (var seq in o["from"] as JsonArray ?? new JsonArray())
        {
            if (Whole(seq) is not { } value || value < 0 || value > int.MaxValue) return null;
            from.Add((int)value);
        }
        return new TeachStep((int)number, text)
        {
            Kind = kind,
            App = Text(o, "app"),
            Guard = guard,
            Flagged = o["flagged"] is JsonValue f && f.TryGetValue<bool>(out var flagged) && flagged,
            Said = Text(o, "said"),
            From = new ValueList<int>(from),
            Frame = Text(o, "frame") is { } frame && AssistantStore.IsFrameName(frame) ? frame : null,
        };
    }

    private static JsonNode QuestionNode(TeachQuestion q)
    {
        var o = new JsonObject
        {
            ["id"] = q.Id,
            ["text"] = q.Text,
            ["options"] = new JsonArray([.. q.Options.Select(option => (JsonNode)JsonValue.Create(option)!)]),
            ["effect"] = q.Effect.ToString(),
        };
        if (q.Step is { } step) o["step"] = step;
        if (q.Answer is not null) o["answer"] = q.Answer;
        if (q.EffectOn is not null) o["effectOn"] = q.EffectOn;
        if (q.Note is not null) o["note"] = q.Note;
        return o;
    }

    private static TeachQuestion? ReadQuestion(JsonObject o)
    {
        if (Text(o, "id") is not { Length: > 0 } id || Text(o, "text") is not { } text) return null;
        if (!Enum.TryParse<TeachAnswerEffect>(Text(o, "effect"), out var effect) || !Enum.IsDefined(effect)) return null;
        var options = (o["options"] as JsonArray ?? new JsonArray()).Select(n => n is JsonValue s && s.TryGetValue<string>(out var option) ? option : "").Where(t => t.Length > 0);
        return new TeachQuestion(id, text, new ValueList<string>(options))
        {
            Step = Whole(o["step"]) is { } step && step is >= 1 and <= TeachLimits.Steps ? (int)step : null,
            Answer = Text(o, "answer"),
            Effect = effect,
            EffectOn = Text(o, "effectOn"),
            Note = Text(o, "note"),
        };
    }

    // ---- pieces ----

    private static JsonObject ElementNode(TeachElement e)
    {
        var o = new JsonObject { ["app"] = e.App, ["window"] = e.Window, ["type"] = e.ControlType, ["name"] = e.Name };
        if (e.IsPassword) o["pw"] = true;
        return o;
    }

    private static TeachElement ReadElement(JsonObject o) =>
        new(Text(o, "app") ?? "", Text(o, "window") ?? "", Text(o, "type") ?? "", Text(o, "name") ?? "")
        {
            IsPassword = o["pw"] is JsonValue p && p.TryGetValue<bool>(out var password) && password,
        };

    private static IEnumerable<JsonObject> Objects(JsonNode? node) => (node as JsonArray ?? new JsonArray()).OfType<JsonObject>();

    private static double Seconds(TimeSpan time) => Math.Round(time.TotalSeconds, 3);

    private static string Time(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(string? text) =>
        DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;

    private static string? Text(JsonObject o, string name) =>
        o[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double? Number(JsonNode? node) => AssistantJson.Number(node);

    private static long? Whole(JsonNode? node) =>
        AssistantJson.Number(node) is { } number && number == Math.Floor(number) && Math.Abs(number) < 1e12 ? (long)number : null;

    private static ValueMap<string, string> Unknown(JsonObject o, HashSet<string> known) =>
        new(o.Where(p => !known.Contains(p.Key) && p.Value is not null).Select(p => new KeyValuePair<string, string>(p.Key, p.Value!.ToJsonString())));
}
