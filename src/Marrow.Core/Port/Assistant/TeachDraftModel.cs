// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 steps 2 and 3, 12.1a "annotations can only make a class stricter", 12.4; unit AS-19; ED-NEW-dextra-as19-4). No Swift twin.
// What a model may do to a draft, and what it may not. The model's answer is read as data: a size limit, strict shape, every text cut
// and read for instructions. It may reword steps, merge the steps of the recording it names, name the inputs, give the reasons and the
// obstacles and ask questions. It can never remove a mark that asks the owner, never make a step the recording has no event for, and
// never keep a recorded step that asks out of the draft: such a step comes back by itself. Then the owner's answers are applied.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Core.Port.Assistant;

public static class TeachDraftModel
{
    /// <summary>The longest answer read; a longer one is refused whole.</summary>
    public const int AnswerChars = 100_000;

    public const int ListLimit = 20;

    public const string Unreadable = "The model's answer could not be read, so the draft comes straight from the recording.";

    /// <summary>The model's JSON laid over the skeleton. A null or unreadable answer returns the skeleton with a note.</summary>
    public static TeachDraft Apply(TeachDraft skeleton, IReadOnlyList<TeachEvent> events, string? answer)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(events);
        if (Parse(answer) is not { } root) return skeleton with { Notes = skeleton.Notes.Appending(Unreadable) };
        var notes = skeleton.Notes.ToList();
        var steps = Steps(skeleton, root, notes, out var indexToFinal);
        var named = Named(skeleton, root, notes);
        var draft = skeleton with
        {
            Name = named.Name,
            Description = named.Description,
            Steps = new ValueList<TeachStep>(steps),
            ModelWritten = true,
        };
        draft = draft with
        {
            Decisions = new ValueList<TeachDecision>(Decisions(root, skeleton.Decisions, notes)),
            Obstacles = new ValueList<TeachObstacle>(Obstacles(root, notes)),
        };
        draft = Inputs(draft, root, indexToFinal, notes);
        draft = draft with
        {
            Questions = new ValueList<TeachQuestion>(Questions(root, skeleton.Questions, notes)),
            Notes = new ValueList<string>(notes),
        };
        return draft;
    }

    private static JsonObject? Parse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > AnswerChars) return null;
        var open = answer.IndexOf('{', StringComparison.Ordinal);
        var close = answer.LastIndexOf('}');
        if (open < 0 || close <= open) return null;
        try { return JsonNode.Parse(answer[open..(close + 1)], documentOptions: new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false }) as JsonObject; }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static (string Name, string Description) Named(TeachDraft skeleton, JsonObject root, List<string> notes)
    {
        var name = skeleton.Name;
        if (Text(root["name"], 120) is { Length: > 0 } given && Safe(given, "the skill's name", notes) is { } safeName) name = TeachDraft.SkillNameFrom(safeName);
        var description = skeleton.Description;
        if (Text(root["description"], TeachDraft.DescriptionChars) is { Length: > 0 } words && Safe(words, "the skill's description", notes) is { } safeWords) description = safeWords;
        return (name, description);
    }

    // ---- steps ----

    private static List<TeachStep> Steps(TeachDraft skeleton, JsonObject root, List<string> notes, out Dictionary<int, int> indexToFinal)
    {
        indexToFinal = [];
        var bySeq = skeleton.Steps.SelectMany(step => step.From.Select(seq => (seq, step))).GroupBy(p => p.seq).ToDictionary(g => g.Key, g => g.First().step);
        if (root["steps"] is not JsonArray array || array.Count == 0)
        {
            notes.Add("The model gave no steps, so the steps come straight from the recording.");
            return [.. skeleton.Steps];
        }
        var made = new List<(int Order, int Index, TeachStep Step)>();
        var covered = new HashSet<int>();
        var invented = 0;
        var index = 0;
        foreach (var node in array.Take(TeachLimits.Steps))
        {
            index++;
            if (node is not JsonObject item) { invented++; continue; }
            var from = (item["from"] as JsonArray ?? new JsonArray()).Select(Whole).Where(seq => seq is { } s && bySeq.ContainsKey(s)).Select(seq => seq!.Value).Distinct().Order().ToList();
            if (from.Count == 0) { invented++; continue; }
            var stands = from.Select(seq => bySeq[seq]).DistinctBy(s => s.Number).OrderBy(s => s.Number).ToList();
            foreach (var step in stands) covered.Add(step.Number);
            var first = stands[0];
            var text = Text(item["text"], TeachDraft.TextChars);
            if (text.Length == 0 || Safe(text, "a step's wording", notes) is not { } safe) safe = first.Text;
            // The strictest guard of the events it covers, and of the words it uses: a model can add a mark, never take one off.
            var guard = stands.Select(s => s.Guard).Append(TeachGuards.ForLabel(safe)).OfType<TeachGuardFinding>()
                .OrderByDescending(g => ActionClasses.Strictness(g.Class)).FirstOrDefault();
            var said = string.Join(' ', stands.Select(s => s.Said).Where(s => !string.IsNullOrEmpty(s)));
            made.Add((from[0], index, first with
            {
                Text = safe,
                Guard = guard,
                Flagged = stands.Any(s => s.Flagged),
                Said = said.Length == 0 ? null : TeachDraft.Clean(said, TeachLimits.NarrationChars),
                From = new ValueList<int>(stands.SelectMany(s => s.From).Distinct().Order()),
                Frame = stands.Select(s => s.Frame).FirstOrDefault(f => f is not null),
            }));
        }
        if (invented > 0) notes.Add($"{invented.ToString(CultureInfo.InvariantCulture)} step{(invented == 1 ? "" : "s")} the model wrote had no recorded step behind {(invented == 1 ? "it" : "them")} and {(invented == 1 ? "was" : "were")} left out.");
        if (made.Count == 0)
        {
            notes.Add("None of the model's steps matched a recorded step, so the steps come straight from the recording.");
            return [.. skeleton.Steps];
        }
        // A recorded step that asks the owner comes back if the model dropped or folded it away.
        foreach (var step in skeleton.Steps.Where(s => s.Guard is not null && !covered.Contains(s.Number)))
        {
            made.Add((step.From[0], 0, step));
            notes.Add($"The model left out a step that asks you, and it is back: “{TeachDraft.Clean(step.Text, 80)}”.");
        }
        var skipped = skeleton.Steps.Where(s => s.Guard is null && !covered.Contains(s.Number)).ToList();
        if (skipped.Count > 0)
            notes.Add("Left out by the model: " + string.Join("; ", skipped.Take(5).Select(s => $"“{TeachDraft.Clean(s.Text, 60)}”")) + (skipped.Count > 5 ? $" and {(skipped.Count - 5).ToString(CultureInfo.InvariantCulture)} more." : "."));
        var ordered = made.OrderBy(m => m.Order).ThenBy(m => m.Index).Select(m => m.Step).ToList();
        var final = made.OrderBy(m => m.Order).ThenBy(m => m.Index).Select((m, i) => (m.Index, Number: i + 1)).Where(x => x.Index > 0);
        foreach (var (modelIndex, number) in final) indexToFinal[modelIndex] = number;
        return [.. ordered.Take(TeachLimits.Steps).Select((s, i) => s with { Number = i + 1 })];
    }

    // ---- inputs ----

    private static TeachDraft Inputs(TeachDraft draft, JsonObject root, Dictionary<int, int> indexToFinal, List<string> notes)
    {
        if (root["inputs"] is not JsonArray array) return draft;
        foreach (var node in array.Take(TeachLimits.Parameters))
        {
            if (node is not JsonObject item) continue;
            var name = TeachDraft.InputName(Text(item["name"], 40));
            var example = Text(item["example"], 80);
            if (name is null || example.Length == 0 || Safe(example, "an input's example", notes) is not { } safe || safe != example) continue;
            var modelStep = Whole(item["step"]);
            if (modelStep is { } index && indexToFinal.TryGetValue(index, out var number) && draft.TryMakeInput(number, name, example, out var changed))
                draft = changed;
            else if (draft.Parameters.Count < TeachLimits.Parameters && draft.Parameters.All(p => p.Name != name))
                draft = draft with { Parameters = draft.Parameters.Appending(new TeachParameter(name, example, "")) };
        }
        return draft;
    }

    // ---- decisions, obstacles, questions ----

    private static List<TeachDecision> Decisions(JsonObject root, ValueList<TeachDecision> fromRecording, List<string> notes)
    {
        var list = fromRecording.ToList();
        if (root["decisions"] is not JsonArray array) return list;
        foreach (var node in array)
        {
            if (list.Count >= ListLimit || node is not JsonObject item) break;
            var text = Text(item["text"], TeachDraft.TextChars);
            var reason = Text(item["reason"], TeachDraft.TextChars);
            if (text.Length == 0 || Safe(text, "a decision", notes) is not { } safeText) continue;
            var safeReason = reason.Length == 0 ? "" : Safe(reason, "a decision's reason", notes) ?? "";
            if (list.All(d => !string.Equals(d.Text, safeText, StringComparison.OrdinalIgnoreCase))) list.Add(new TeachDecision(safeText, safeReason));
        }
        return list;
    }

    private static List<TeachObstacle> Obstacles(JsonObject root, List<string> notes)
    {
        var list = new List<TeachObstacle>();
        if (root["obstacles"] is not JsonArray array) return list;
        foreach (var node in array)
        {
            if (list.Count >= ListLimit || node is not JsonObject item) break;
            var text = Text(item["text"], TeachDraft.TextChars);
            var fix = Text(item["fix"], TeachDraft.TextChars);
            if (text.Length == 0 || Safe(text, "an obstacle", notes) is not { } safeText) continue;
            list.Add(new TeachObstacle(safeText, fix.Length == 0 ? "" : Safe(fix, "an obstacle's fix", notes) ?? ""));
        }
        return list;
    }

    private static List<TeachQuestion> Questions(JsonObject root, ValueList<TeachQuestion> fromRecording, List<string> notes)
    {
        var list = fromRecording.ToList();
        if (root["questions"] is not JsonArray array) return list;
        var n = 0;
        foreach (var node in array)
        {
            if (list.Count >= TeachLimits.Questions || node is not JsonObject item) break;
            var text = Text(item["text"], 300);
            if (text.Length == 0 || Safe(text, "a question", notes) is not { } safeText) continue;
            var options = (item["options"] as JsonArray ?? new JsonArray()).Select(o => Text(o, 120)).Where(o => o.Length > 0).Distinct(StringComparer.Ordinal).Take(4).ToList();
            if (options.Any(o => Safe(o, "a question's answer", notes) is null)) continue;
            if (options.Count < 2) options = ["Yes", "No"];
            n++;
            list.Add(new TeachQuestion("m" + n.ToString(CultureInfo.InvariantCulture), safeText, new ValueList<string>(options)));
        }
        return list;
    }

    // ---- the owner's answers ----

    /// <summary>
    /// The owner answered a question. An answer that is one of the options can change the draft (an input is made, a step is left out, a
    /// wait is noted); a model's question, or an answer the owner typed himself, is kept as a decision with his words as the reason.
    /// </summary>
    public static TeachDraft Answer(TeachDraft draft, string questionId, string answer)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var question = draft.Questions.FirstOrDefault(q => q.Id == questionId);
        var text = TeachDraft.Clean(answer, 300);
        if (question is null || question.Answer is not null || text.Length == 0) return draft;
        var result = draft with
        {
            Questions = new ValueList<TeachQuestion>(draft.Questions.Select(q => q.Id == questionId ? q with { Answer = text } : q)),
        };
        var chosen = question.Options.Contains(text);
        if (chosen && question.Effect != TeachAnswerEffect.None)
            return string.Equals(text, question.EffectOn, StringComparison.Ordinal) ? Effect(result, question) : result;
        return result with { Decisions = result.Decisions.Count >= ListLimit ? result.Decisions : result.Decisions.Appending(new TeachDecision(question.Text, "You answered: " + text)) };
    }

    private static TeachDraft Effect(TeachDraft draft, TeachQuestion question)
    {
        switch (question.Effect)
        {
            case TeachAnswerEffect.MakesInput when question.Step is { } number && draft.Steps.FirstOrDefault(s => s.Number == number) is { } step:
            {
                var example = Between(step.Text, '“', '”');
                var field = InputNameFrom(step.Text) ?? "input_" + number.ToString(CultureInfo.InvariantCulture);
                var name = field;
                for (var i = 2; draft.Parameters.Any(p => p.Name == name) && i < 20; i++) name = field + "_" + i.ToString(CultureInfo.InvariantCulture);
                return example is not null && draft.TryMakeInput(number, name, example, out var changed) ? changed : draft;
            }
            case TeachAnswerEffect.LeavesStepOut when question.Step is { } removed:
            {
                var gone = new List<int> { removed };
                // The undo that followed the step goes with it.
                if (draft.Steps.FirstOrDefault(s => s.Number == removed + 1) is { Kind: TeachEventKind.Shortcut } undo
                    && undo.Text.Contains("Ctrl+Z", StringComparison.OrdinalIgnoreCase))
                    gone.Add(removed + 1);
                var smaller = (draft with { Steps = new ValueList<TeachStep>(draft.Steps.Where(s => !gone.Contains(s.Number))) }).Renumbered();
                // Questions that point at later steps follow them to their new numbers; one that pointed at a step that went has none.
                return smaller with
                {
                    Questions = new ValueList<TeachQuestion>(smaller.Questions.Select(q =>
                        q.Step is not { } at ? q : gone.Contains(at) ? q with { Step = null } : q with { Step = at - gone.Count(g => g < at) })),
                };
            }
            case TeachAnswerEffect.NotesDecision when question.Note is { Length: > 0 } note:
                return draft with { Decisions = draft.Decisions.Count >= ListLimit ? draft.Decisions : draft.Decisions.Appending(new TeachDecision(note, "You said so.")) };
            default:
                return draft;
        }
    }

    // The second quoted text of a typed step ("Type “x” into “Subject” (edit)"), made into an input name; null when there is none.
    private static string? InputNameFrom(string stepText)
    {
        var first = stepText.IndexOf('”', StringComparison.Ordinal);
        if (first < 0) return null;
        var rest = stepText[(first + 1)..];
        return TeachDraft.InputName(Between(rest, '“', '”'));
    }

    private static string? Between(string text, char open, char close)
    {
        var start = text.IndexOf(open, StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf(close, start + 1);
        return start >= 0 && end > start + 1 ? text[(start + 1)..end] : null;
    }

    // ---- reading ----

    private static string Text(JsonNode? node, int limit) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? TeachDraft.Clean(text, limit) : "";

    private static int? Whole(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var number)) return number;
        return value.TryGetValue<double>(out var real) && real == Math.Floor(real) && Math.Abs(real) < int.MaxValue ? (int)real : null;
    }

    // The text when it is fit to keep: no instruction in it and nothing the privacy patterns would change. Null when it is not.
    private static string? Safe(string text, string what, List<string> notes)
    {
        if (TeachScan.Find(text) is { } found)
        {
            var note = $"Found instructions in {what} (“{found.Quote}”); ignored.";
            if (!notes.Contains(note)) notes.Add(note);
            return null;
        }
        return FeedShield.Mask(text) == text ? text : null;
    }
}
