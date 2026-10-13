// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 steps 2 and 3, 12.4; unit AS-19; ED-NEW-dextra-as19-4). No Swift twin.
// The draft a recording makes with no model at all: one step per thing the owner did (a click on a text field and the typing into
// it are one step), the guards decided by code, his spoken lines attached to the steps they belong to, the reasons he gave read
// as decisions, and the questions the recording itself raises. It is always valid and always complete, so a model that is slow, off
// or refused costs the owner the nice wording and nothing else (TeachDraftModel adds the wording on top). Text that came from the
// screen (a control's name, a window's title) is read for instructions first; what looks like one is taken out and said so.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public static partial class TeachDraftBuilder
{
    /// <summary>A click on a field and the typing into it count as one step when they are this close in time.</summary>
    public static readonly TimeSpan ClickThenType = TimeSpan.FromSeconds(3);

    /// <summary>Typed bursts into one field this close together are one step.</summary>
    public static readonly TimeSpan SameTyping = TimeSpan.FromSeconds(5);

    /// <summary>A gap this long between two events raises "did you wait for something?".</summary>
    public static readonly TimeSpan LongWait = TimeSpan.FromSeconds(30);

    /// <summary>A bare "Yes" or "OK" takes the guard of the step before it only when it follows within this time.</summary>
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(30);

    /// <summary>The most typed texts the built-in "always the same?" question is raised for.</summary>
    public const int TypedQuestions = 3;

    public const string TakenOut = "[text taken out]";

    /// <summary>The draft straight from the recording.</summary>
    public static TeachDraft Skeleton(TeachRecordingInfo info, IReadOnlyList<TeachEvent> events, IReadOnlyList<TeachNarrationLine> narration)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(narration);
        var notes = new List<string>();
        var steps = new List<TeachStep>();
        TeachGuardFinding? previous = null;
        TeachEvent? last = null;
        foreach (var e in events.OrderBy(e => e.Seq))
        {
            // A bare "Yes" takes the guard of the step just before it, but only a moment later: not a "Yes" five minutes on.
            var inherit = last is not null && e.At - last.At <= ConfirmWindow ? previous : null;
            var guard = TeachGuards.ForEvent(e, inherit);
            var step = StepOf(e, guard, notes);
            // A click on a field and the typing into it are one step; typing in bursts into one field is one step.
            if (steps.Count > 0 && last is not null && Joins(last, e))
            {
                var before = steps[^1];
                steps[^1] = last.Kind == TeachEventKind.Click
                    ? step with { Number = before.Number, Guard = step.Guard ?? before.Guard, From = before.From.Appending(e.Seq), Frame = before.Frame ?? step.Frame }
                    : MergeTyping(before, e, guard, events, notes);
            }
            else
            {
                if (steps.Count >= TeachLimits.Steps)
                {
                    if (!notes.Contains(TooLong)) notes.Add(TooLong);
                    last = e;
                    continue;
                }
                steps.Add(step with { Number = steps.Count + 1, From = new ValueList<int>([e.Seq]) });
            }
            previous = steps[^1].Guard;
            last = e;
        }

        var withSaid = AttachNarration(steps, events, narration, out var decisions);
        var name = NameOf(info, withSaid, narration);
        var draft = new TeachDraft(info.Id, name, DescriptionOf(info, withSaid))
        {
            Steps = new ValueList<TeachStep>(withSaid),
            Decisions = new ValueList<TeachDecision>(decisions),
            Notes = new ValueList<string>(notes),
        };
        return draft with { Questions = new ValueList<TeachQuestion>(QuestionsOf(draft, events)) };
    }

    private const string TooLong = "The recording has more than 200 steps. Only the first 200 are in the draft.";

    private static bool Joins(TeachEvent last, TeachEvent next)
    {
        if (last.Element is null || next.Element is null || next.Kind != TeachEventKind.Type) return false;
        if (last.Kind == TeachEventKind.Click)
            return SameField(last.Element, next.Element) && next.At - last.At <= ClickThenType && last.Verb is "click" or "";
        return last.Kind == TeachEventKind.Type && SameField(last.Element, next.Element) && next.At - last.At <= SameTyping;
    }

    private static bool SameField(TeachElement a, TeachElement b) =>
        string.Equals(a.App, b.App, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && string.Equals(a.ControlType, b.ControlType, StringComparison.Ordinal);

    // The typed bursts of one field joined. A burst's text is what the field held when typing stopped, so the last one is the field's
    // value; a secret in any burst makes the whole step a secret, and a step with no readable text keeps only the length.
    private static TeachStep MergeTyping(TeachStep before, TeachEvent next, TeachGuardFinding? guardNext, IReadOnlyList<TeachEvent> events, List<string> notes)
    {
        var parts = before.From.Select(seq => events.FirstOrDefault(e => e.Seq == seq)).OfType<TeachEvent>().Where(e => e.Kind == TeachEventKind.Type).Append(next).ToList();
        var secret = parts.Any(p => p.Secret || p.Element?.IsPassword == true);
        var joined = new TeachEvent(next.Seq, next.At, TeachEventKind.Type)
        {
            Verb = "type", Element = next.Element, Text = secret ? null : parts[^1].Text, Length = parts.Sum(p => p.Length), Secret = secret,
            Frame = parts[0].Frame ?? next.Frame,
        };
        var guard = TeachGuards.ForEvent(joined) ?? before.Guard ?? guardNext;
        return StepOf(joined, guard, notes) with { Number = before.Number, From = before.From.Appending(next.Seq), Frame = before.Frame ?? next.Frame };
    }

    private static TeachStep StepOf(TeachEvent e, TeachGuardFinding? guard, List<string> notes)
    {
        var app = Pretty(e.Element?.App);
        var text = e.Kind switch
        {
            TeachEventKind.Private => "Do this step yourself, in a window nth does not record.",
            TeachEventKind.Type => TypeText(e, notes),
            TeachEventKind.Shortcut => $"Press {SafeText(e.Text, notes, 60)}" + (app.Length > 0 ? " in " + app : ""),
            TeachEventKind.Browser => BrowserText(e, notes),
            _ => ClickText(e, notes),
        };
        return new TeachStep(0, TeachDraft.Clean(text, TeachDraft.TextChars))
        {
            Kind = e.Kind,
            App = app.Length > 0 ? app : null,
            Guard = guard,
            Frame = e.Frame,
        };
    }

    private static string ClickText(TeachEvent e, List<string> notes)
    {
        var verb = e.Verb switch { "right-click" => "Right-click", "double-click" => "Double-click", _ => "Click" };
        var element = e.Element;
        var app = Pretty(element?.App);
        var where = app.Length > 0 ? " in " + app : "";
        if (element is null) return verb + where;
        var name = SafeText(element.Name, notes, 80);
        var type = ControlWord(element.ControlType);
        if (name.Length > 0) return $"{verb} “{name}”" + (type.Length > 0 ? $" ({type})" : "") + where;
        if (type.Length > 0) return $"{verb} the {type}" + where;
        var window = SafeText(element.Window, notes, 60);
        return window.Length > 0 ? $"{verb} in the window “{window}”" + where : verb + where;
    }

    private static string TypeText(TeachEvent e, List<string> notes)
    {
        var field = e.Element is { } element ? Field(element, notes) : "the field";
        if (e.Secret || e.Element?.IsPassword == true)
            return "Type the password or secret yourself" + (field.Length > 0 ? " into " + field : "") + ". nth never types one.";
        if (string.IsNullOrEmpty(e.Text))
            return $"Type text ({e.Length.ToString(CultureInfo.InvariantCulture)} characters) into {field}";
        var shown = e.Text.Length > 120 ? e.Text[..120] + "…" : e.Text;
        return $"Type “{shown}” into {field}";
    }

    private static string Field(TeachElement element, List<string> notes)
    {
        var name = SafeText(element.Name, notes, 60);
        var type = ControlWord(element.ControlType);
        if (name.Length > 0) return $"“{name}”" + (type.Length > 0 ? $" ({type})" : "");
        return type.Length > 0 ? "the " + type : "the field";
    }

    private static string BrowserText(TeachEvent e, List<string> notes)
    {
        var url = SafeText(e.Url, notes, 120);
        switch (e.Verb)
        {
            case "open": return "Open " + (url.Length > 0 ? url : "the page");
            case "submit": return "Submit the form" + (url.Length > 0 ? " on " + url : "");
            case "type":
                return string.IsNullOrEmpty(e.Text) ? $"Type text ({e.Length.ToString(CultureInfo.InvariantCulture)} characters) on the page" : $"Type “{TeachDraft.Clean(e.Text, 120)}” on the page";
            default:
                var name = SafeText(e.Element?.Name ?? e.Text, notes, 80);
                return (e.Verb is { Length: > 0 } verb ? char.ToUpperInvariant(verb[0]) + verb[1..] : "Click") + (name.Length > 0 ? $" “{name}”" : " on the page");
        }
    }

    // Screen text is data: a control's name that reads like an instruction to the agent that will carry the skill out is taken out.
    private static string SafeText(string? text, List<string> notes, int limit)
    {
        var line = TeachDraft.Clean(text, limit);
        if (line.Length == 0) return "";
        if (TeachScan.Find(line) is { } found)
        {
            var note = $"Found instructions in text on the screen (“{found.Quote}”); ignored.";
            if (!notes.Contains(note)) notes.Add(note);
            return TakenOut;
        }
        return FeedShield.Mask(line);
    }

    private static string ControlWord(string? type)
    {
        var folded = TeachDraft.Clean(type, 40).ToLowerInvariant();
        return folded.Length == 0 ? "" : folded;
    }

    /// <summary>"notepad" as "Notepad": the process name made readable, never longer than 40 characters.</summary>
    public static string Pretty(string? app)
    {
        var name = Path.GetFileNameWithoutExtension((app ?? "").Trim());
        name = TeachDraft.Clean(name, 40);
        return name.Length == 0 ? "" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    // ---- narration ----

    private static List<TeachStep> AttachNarration(List<TeachStep> steps, IReadOnlyList<TeachEvent> events, IReadOnlyList<TeachNarrationLine> narration,
        out List<TeachDecision> decisions)
    {
        decisions = [];
        if (steps.Count == 0 || narration.Count == 0) return steps;
        var at = events.ToDictionary(e => e.Seq, e => e.At);
        var said = new Dictionary<int, StringBuilder>();
        foreach (var line in narration.OrderBy(n => n.Start))
        {
            var text = Spoken(line.Text);
            if (text.Length == 0) continue;
            var step = steps.OrderBy(s => Math.Abs((at[s.From[0]] - line.Start).TotalSeconds)).First();
            if (!said.TryGetValue(step.Number, out var builder)) said[step.Number] = builder = new StringBuilder();
            if (builder.Length > 0) builder.Append(' ');
            if (builder.Length + text.Length <= TeachLimits.NarrationChars) builder.Append(text);
            if (IsReason(text) && decisions.Count < 20)
                decisions.Add(new TeachDecision(text, $"You said so at {TeachWords.Clock(line.Start)}, at step {step.Number.ToString(CultureInfo.InvariantCulture)}."));
        }
        return [.. steps.Select(s => said.TryGetValue(s.Number, out var builder) ? s with { Said = builder.ToString() } : s)];
    }

    // A spoken line that the privacy patterns would change (a password read aloud) is dropped whole, not masked.
    private static string Spoken(string? text)
    {
        var line = TeachDraft.Clean(text, TeachLimits.NarrationChars);
        if (line.Length == 0) return "";
        return FeedShield.Mask(line) != line ? "" : line;
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:because|so that|since|if|when|unless|always|never|only|instead|whenever|every time|otherwise|क्योंकि|अगर|यदि|हमेशा|कभी नहीं|सिर्फ|केवल|वरना|کیونکہ|اگر|ہمیشہ|کبھی نہیں|صرف|ورنہ)(?![\p{L}\p{N}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex ReasonWords();

    private static bool IsReason(string text)
    {
        try { return ReasonWords().IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    // ---- name and description ----

    private static string NameOf(TeachRecordingInfo info, IReadOnlyList<TeachStep> steps, IReadOnlyList<TeachNarrationLine> narration)
    {
        var first = narration.OrderBy(n => n.Start).Select(n => Spoken(n.Text)).FirstOrDefault(t => t.Length > 0);
        if (first is not null)
        {
            var words = string.Join(' ', first.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5));
            var fromWords = TeachDraft.SkillNameFrom(words);
            if (fromWords != "taught-skill") return fromWords;
        }
        var app = steps.Select(s => s.App).FirstOrDefault(a => !string.IsNullOrEmpty(a));
        var stamp = info.Started.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return TeachDraft.SkillNameFrom((app is null ? "taught" : app) + "-steps-" + stamp);
    }

    private static string DescriptionOf(TeachRecordingInfo info, IReadOnlyList<TeachStep> steps)
    {
        var apps = steps.Select(s => s.App).Where(a => !string.IsNullOrEmpty(a)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        var where = apps.Count == 0 ? "" : " in " + string.Join(", ", apps);
        var count = steps.Count.ToString(CultureInfo.InvariantCulture);
        return TeachDraft.Clean($"Taught by watching the owner work{where} on {info.Started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {count} step{(steps.Count == 1 ? "" : "s")}.", TeachDraft.DescriptionChars);
    }

    // ---- questions ----

    private static List<TeachQuestion> QuestionsOf(TeachDraft draft, IReadOnlyList<TeachEvent> events)
    {
        var questions = new List<TeachQuestion>();
        var at = events.ToDictionary(e => e.Seq, e => e.At);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var typed = 0;
        foreach (var step in draft.Steps)
        {
            if (questions.Count >= TeachLimits.Questions) break;
            if (step.Kind == TeachEventKind.Type && step.Guard?.Class != ActionClass.HardDeny && typed < TypedQuestions
                && step.Text.Contains('“', StringComparison.Ordinal))
            {
                var key = TypedExample(step);
                if (key is not null && seen.Add(key))
                {
                    typed++;
                    questions.Add(new TeachQuestion($"typed-{step.Number.ToString(CultureInfo.InvariantCulture)}", $"At {TeachWords.Clock(at[step.From[0]])} you typed “{TeachDraft.Clean(key, 60)}”. Is it always the same?",
                        new ValueList<string>(["It is always the same", "It changes: make it an input"]))
                    {
                        Step = step.Number, Effect = TeachAnswerEffect.MakesInput, EffectOn = "It changes: make it an input",
                    });
                }
            }
        }
        for (var i = 0; i < draft.Steps.Count && questions.Count < TeachLimits.Questions; i++)
        {
            var step = draft.Steps[i];
            if (step.Kind == TeachEventKind.Shortcut && string.Equals(StepChord(step, events), "Ctrl+Z", StringComparison.OrdinalIgnoreCase) && i > 0)
            {
                var undone = draft.Steps[i - 1];
                questions.Add(new TeachQuestion($"undo-{step.Number.ToString(CultureInfo.InvariantCulture)}",
                    $"At {TeachWords.Clock(at[step.From[0]])} you pressed Ctrl+Z after step {undone.Number.ToString(CultureInfo.InvariantCulture)} ({TeachDraft.Clean(undone.Text, 80)}). Leave that step out?",
                    new ValueList<string>(["Leave it out", "Keep it"]))
                {
                    Step = undone.Number, Effect = TeachAnswerEffect.LeavesStepOut, EffectOn = "Leave it out",
                });
            }
        }
        var ordered = events.OrderBy(e => e.Seq).ToList();
        for (var i = 1; i < ordered.Count && questions.Count < TeachLimits.Questions; i++)
        {
            var gap = ordered[i].At - ordered[i - 1].At;
            if (gap < LongWait) continue;
            var before = draft.Steps.FirstOrDefault(s => s.From.Contains(ordered[i].Seq));
            questions.Add(new TeachQuestion($"wait-{ordered[i].Seq.ToString(CultureInfo.InvariantCulture)}",
                $"At {TeachWords.Clock(ordered[i - 1].At)} nothing happened for {((int)gap.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds. Does the skill have to wait for something to finish?",
                new ValueList<string>(["Yes, wait for it", "No, that was just me"]))
            {
                Step = before?.Number, Effect = TeachAnswerEffect.NotesDecision, EffectOn = "Yes, wait for it",
                Note = $"Wait before step {(before?.Number ?? 1).ToString(CultureInfo.InvariantCulture)}: something takes time to finish.",
            });
        }
        return questions;
    }

    private static string? TypedExample(TeachStep step)
    {
        var open = step.Text.IndexOf('“', StringComparison.Ordinal);
        var close = open < 0 ? -1 : step.Text.IndexOf('”', open + 1);
        return open >= 0 && close > open + 1 ? step.Text[(open + 1)..close] : null;
    }

    private static string? StepChord(TeachStep step, IReadOnlyList<TeachEvent> events) =>
        events.FirstOrDefault(e => e.Seq == step.From[0] && e.Kind == TeachEventKind.Shortcut)?.Text;
}
