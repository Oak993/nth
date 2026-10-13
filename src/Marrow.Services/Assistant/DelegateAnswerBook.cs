// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "logged with its reasoning, listed in the digest ... saved as a past answer, so the delegate learns from it",
// 15.3; unit AS-22; ED-NEW-dextra-as22-8). No Swift twin.
// What the delegate answered for the owner, and what he corrected: the list the Decisions tab shows with its Override buttons, the digest's
// "Answered for you" line, and the past answers the delegate is given before it decides the next one (his corrections first). Kept in
// delegate-answers.json: a version, fields this build does not know written back (the file's and each record's), a newer file left as it is,
// a line that is not a record skipped, never repaired. Every text is masked for secrets and cut before it is kept, the newest 300 records
// stay and nothing older than 90 days, so the file cannot grow without bound. Plain data and arithmetic; the responder decides.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What the delegate answered.</summary>
public enum DelegateKind { Question, Approval }

/// <summary>One answer given for the owner, and his correction if he made one.</summary>
public sealed record DelegateRecord(Guid Id, DateTimeOffset At, Guid Chat, string ChatTitle, DelegateKind Kind, string Question, string Answer, string By, string Reasoning)
{
    /// <summary>The project the chat belongs to, when known (the past answers a project's delegate learns from are its own).</summary>
    public Guid? Project { get; init; }

    /// <summary>How sure the model said it was (0 to 1); 1 for an approval the engine allowed by its own rule.</summary>
    public double Confidence { get; init; }

    /// <summary>What the owner said it should have been (Override), or null.</summary>
    public string? Correction { get; init; }

    public DateTimeOffset? CorrectedAt { get; init; }

    /// <summary>Whether the correction reached the asking chat (it is saved either way).</summary>
    public bool CorrectionSent { get; init; }

    public bool Corrected => Correction is not null;
}

public sealed class DelegateAnswerBook
{
    public const int FormatVersion = 1;

    /// <summary>The newest records kept.</summary>
    public const int MaxRecords = 300;

    /// <summary>Records older than this are let go.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);

    public const int TitleChars = 120;
    public const int QuestionChars = 600;
    public const int AnswerChars = 300;
    public const int ReasoningChars = 600;
    public const int CorrectionChars = DelegateWords.CorrectionChars;

    private sealed class Entry(DelegateRecord record, JsonObject? unknown)
    {
        public DelegateRecord Record { get; set; } = record;
        public JsonObject? Unknown { get; } = unknown;
    }

    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly List<Entry> entries = [];
    private JsonObject? root;

    public DelegateAnswerBook(TimeProvider? time = null) => clock = time ?? TimeProvider.System;

    /// <summary>The file's version was newer than this build knows: nothing was read, and nothing is written over it.</summary>
    public bool ReadOnly { get; private set; }

    /// <summary>A record was added or corrected (the store's writer listens).</summary>
    public event Action? Changed;

    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    /// <summary>
    /// Keeps an answer. Its texts are masked for secrets and cut; the id and time are the caller's. Returns the record as kept. The
    /// oldest records go when there are more than <see cref="MaxRecords"/> or they are older than <see cref="MaxAge"/>.
    /// </summary>
    public DelegateRecord Add(DelegateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var kept = record with
        {
            ChatTitle = Kept(record.ChatTitle, TitleChars),
            Question = Kept(record.Question, QuestionChars),
            Answer = Kept(record.Answer, AnswerChars),
            By = Kept(record.By, TitleChars),
            Reasoning = Kept(record.Reasoning, ReasoningChars),
            Confidence = double.IsFinite(record.Confidence) ? Math.Clamp(record.Confidence, 0, 1) : 0,
            Correction = record.Correction is null ? null : Kept(record.Correction, CorrectionChars),
        };
        lock (gate)
        {
            entries.RemoveAll(e => e.Record.Id == kept.Id);
            entries.Add(new Entry(kept, null));
            Prune();
        }
        Changed?.Invoke();
        return kept;
    }

    public DelegateRecord? Find(Guid id)
    {
        lock (gate) return entries.FirstOrDefault(e => e.Record.Id == id)?.Record;
    }

    /// <summary>The newest records first.</summary>
    public IReadOnlyList<DelegateRecord> Recent(int count)
    {
        lock (gate) return [.. entries.Select(e => e.Record).OrderByDescending(r => r.At).Take(Math.Max(0, count))];
    }

    /// <summary>The records since <paramref name="time"/>, oldest first (the digest tells them).</summary>
    public IReadOnlyList<DelegateRecord> Since(DateTimeOffset time)
    {
        lock (gate) return [.. entries.Select(e => e.Record).Where(r => r.At > time).OrderBy(r => r.At)];
    }

    /// <summary>
    /// What a delegate learns from: the project's records (those with no project too), the ones the owner corrected first, then the
    /// newest. <paramref name="project"/> null reads every record.
    /// </summary>
    public IReadOnlyList<DelegateRecord> Past(Guid? project, int count)
    {
        lock (gate)
        {
            return [.. entries.Select(e => e.Record)
                .Where(r => project is null || r.Project is null || r.Project == project)
                .OrderByDescending(r => r.Corrected).ThenByDescending(r => r.At).Take(Math.Max(0, count))];
        }
    }

    /// <summary>
    /// Saves the owner's correction of an answer (Override). Null when there is no such record or it was corrected already (one
    /// correction each). The text is masked for secrets and cut like every text here.
    /// </summary>
    public DelegateRecord? Correct(Guid id, string correction, DateTimeOffset at, bool sent)
    {
        ArgumentNullException.ThrowIfNull(correction);
        DelegateRecord corrected;
        lock (gate)
        {
            var entry = entries.FirstOrDefault(e => e.Record.Id == id);
            if (entry is null || entry.Record.Corrected) return null;
            corrected = entry.Record with { Correction = Kept(correction, CorrectionChars), CorrectedAt = at, CorrectionSent = sent };
            entry.Record = corrected;
        }
        Changed?.Invoke();
        return corrected;
    }

    private static string Kept(string text, int chars)
    {
        var line = FeedShield.Mask(text.Length <= FeedShield.MaxScanChars ? text : text[..FeedShield.MaxScanChars]).Trim();
        return line.Length <= chars ? line : line[..chars].TrimEnd() + "…";
    }

    // Under the lock.
    private void Prune()
    {
        var oldest = clock.GetUtcNow() - MaxAge;
        entries.RemoveAll(e => e.Record.At < oldest);
        if (entries.Count <= MaxRecords) return;
        foreach (var old in entries.OrderBy(e => e.Record.At).Take(entries.Count - MaxRecords).ToList()) entries.Remove(old);
    }

    // ---- the stored document ----

    /// <summary>The document to store, or null when the file read was newer than this build knows (it is left as it was).</summary>
    public JsonObject? ToJson()
    {
        lock (gate)
        {
            if (ReadOnly) return null;
            Prune();
            var document = root is null ? new JsonObject() : (JsonObject)root.DeepClone();
            document["version"] = FormatVersion;
            document["answers"] = new JsonArray([.. entries.OrderBy(e => e.Record.At).Select(e => (JsonNode)Write(e))]);
            return document;
        }
    }

    private static JsonObject Write(Entry entry)
    {
        var r = entry.Record;
        var node = entry.Unknown is null ? new JsonObject() : (JsonObject)entry.Unknown.DeepClone();
        node["id"] = r.Id.ToString("D");
        node["at"] = r.At.ToString("O", CultureInfo.InvariantCulture);
        node["chat"] = r.Chat.ToString("D");
        node["title"] = r.ChatTitle;
        node["kind"] = r.Kind.ToString();
        node["question"] = r.Question;
        node["answer"] = r.Answer;
        node["by"] = r.By;
        node["reasoning"] = r.Reasoning;
        node["confidence"] = r.Confidence;
        if (r.Project is { } project) node["project"] = project.ToString("D"); else node.Remove("project");
        if (r.Correction is { } correction)
        {
            node["correction"] = correction;
            node["correctedAt"] = (r.CorrectedAt ?? r.At).ToString("O", CultureInfo.InvariantCulture);
            node["correctionSent"] = r.CorrectionSent;
        }
        else
        {
            node.Remove("correction");
            node.Remove("correctedAt");
            node.Remove("correctionSent");
        }
        return node;
    }

    private static readonly string[] KnownKeys = ["id", "at", "chat", "title", "kind", "question", "answer", "by", "reasoning", "confidence", "project", "correction", "correctedAt", "correctionSent"];

    /// <summary>Loads a stored document. A newer version makes the book read-only and loads nothing; a line that is not a record is skipped.</summary>
    public void Load(JsonObject? document)
    {
        lock (gate)
        {
            entries.Clear();
            root = null;
            ReadOnly = false;
            if (document is null) return;
            if (AssistantJson.Number(document["version"]) is { } version && version > FormatVersion)
            {
                ReadOnly = true;
                return;
            }
            root = (JsonObject)document.DeepClone();
            root.Remove("version");
            root.Remove("answers");
            if (document["answers"] is not JsonArray lines) return;
            foreach (var line in lines.OfType<JsonObject>())
            {
                if (entries.Count >= MaxRecords) break;
                if (Read(line) is not { } entry || entries.Any(e => e.Record.Id == entry.Record.Id)) continue;
                entries.Add(entry);
            }
            Prune();
        }
    }

    private static Entry? Read(JsonObject line)
    {
        if (Guid_(line["id"]) is not { } id || Guid_(line["chat"]) is not { } chat) return null;
        if (Text(line["at"]) is not { } atText || !DateTimeOffset.TryParseExact(atText, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return null;
        if (!Enum.TryParse<DelegateKind>(Text(line["kind"]), ignoreCase: false, out var kind) || !Enum.IsDefined(kind)) return null;
        if (Text(line["question"]) is not { } question || Text(line["answer"]) is not { } answer) return null;
        var record = new DelegateRecord(id, at, chat, Cut(Text(line["title"]) ?? "", TitleChars), kind, Cut(question, QuestionChars), Cut(answer, AnswerChars),
            Cut(Text(line["by"]) ?? "", TitleChars), Cut(Text(line["reasoning"]) ?? "", ReasoningChars))
        {
            Project = Guid_(line["project"]),
            Confidence = AssistantJson.Number(line["confidence"]) is { } c && double.IsFinite(c) ? Math.Clamp(c, 0, 1) : 0,
        };
        if (Text(line["correction"]) is { } correction)
        {
            record = record with
            {
                Correction = Cut(correction, CorrectionChars),
                CorrectedAt = Text(line["correctedAt"]) is { } when && DateTimeOffset.TryParseExact(when, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var corrected) ? corrected : at,
                CorrectionSent = line["correctionSent"] is JsonValue sent && sent.TryGetValue<bool>(out var flag) && flag,
            };
        }
        var unknown = new JsonObject();
        foreach (var pair in line)
            if (!KnownKeys.Contains(pair.Key, StringComparer.Ordinal)) unknown[pair.Key] = pair.Value?.DeepClone();
        return new Entry(record, unknown.Count == 0 ? null : unknown);
    }

    // What a file holds is cut again on the way in, so a hand-edited file cannot carry a long text into the prompt.
    private static string Cut(string text, int chars) => text.Length <= chars ? text : text[..chars].TrimEnd() + "…";

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static Guid? Guid_(JsonNode? node) => Text(node) is { } text && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty ? id : null;
}
