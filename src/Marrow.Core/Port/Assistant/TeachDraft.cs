// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 2 and 3; unit AS-19; ED-NEW-dextra-as19-4). No Swift twin.
// The skill draft a recording becomes: steps, the inputs that will vary, the decisions with their reasons, the obstacles and how
// they were met, the questions that remain, and the approval boundary (every step that asks the owner). Pure data and the rules
// that edit it; TeachDraftBuilder makes it, TeachSkillText writes it out, TeachTrial and TeachDryRun walk it.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>One step of the skill. <see cref="Guard"/> is set by code (TeachGuards) and is never taken off by a model.</summary>
public sealed record TeachStep(int Number, string Text)
{
    public TeachEventKind Kind { get; init; } = TeachEventKind.Click;

    /// <summary>The app the step happens in, for the line the owner reads.</summary>
    public string? App { get; init; }

    /// <summary>Why the step asks the owner, or null. Money, sending, deleting, installing, agreeing, a password.</summary>
    public TeachGuardFinding? Guard { get; init; }

    /// <summary>The owner asked for the trial to pause here as well.</summary>
    public bool Flagged { get; init; }

    /// <summary>What the owner said about it while he did it (his words, from the local speech engine).</summary>
    public string? Said { get; init; }

    /// <summary>The recording's events this step stands for.</summary>
    public ValueList<int> From { get; init; } = ValueList<int>.Empty;

    /// <summary>The frame to show beside it in the review: a path relative to the recording.</summary>
    public string? Frame { get; init; }

    /// <summary>The step asks the owner, by a guard or because he flagged it.</summary>
    public bool AsksYou => Guard is not null || Flagged;
}

/// <summary>An input that will differ each time: a name, the example the owner showed, and where it goes.</summary>
public sealed record TeachParameter(string Name, string Example, string Where);

/// <summary>A choice the owner made, with the reason he gave (from his narration or his answer to a question).</summary>
public sealed record TeachDecision(string Text, string Reason);

/// <summary>Something that went wrong while he taught it, and how he got past it.</summary>
public sealed record TeachObstacle(string Text, string Fix);

/// <summary>A part of the recording the draft could not settle. It goes to the decision queue as a card.</summary>
public sealed record TeachQuestion(string Id, string Text, ValueList<string> Options)
{
    /// <summary>The step the question is about, when it is about one.</summary>
    public int? Step { get; init; }

    /// <summary>What the owner answered; null while it waits.</summary>
    public string? Answer { get; init; }

    /// <summary>What the answer <see cref="EffectOn"/> does to the draft (the built-in questions); None for a question the model wrote.</summary>
    public TeachAnswerEffect Effect { get; init; }

    /// <summary>The option that has the effect.</summary>
    public string? EffectOn { get; init; }

    /// <summary>The text a NotesDecision answer writes into the decisions.</summary>
    public string? Note { get; init; }
}

public enum TeachAnswerEffect { None, MakesInput, LeavesStepOut, NotesDecision }

public sealed partial record TeachDraft(string RecordingId, string Name, string Description)
{
    public const int DescriptionChars = 300;

    public const int TextChars = 400;

    public ValueList<TeachStep> Steps { get; init; } = ValueList<TeachStep>.Empty;

    public ValueList<TeachParameter> Parameters { get; init; } = ValueList<TeachParameter>.Empty;

    public ValueList<TeachDecision> Decisions { get; init; } = ValueList<TeachDecision>.Empty;

    public ValueList<TeachObstacle> Obstacles { get; init; } = ValueList<TeachObstacle>.Empty;

    public ValueList<TeachQuestion> Questions { get; init; } = ValueList<TeachQuestion>.Empty;

    /// <summary>A sentence on how the draft was made ("Written without a model"), shown above it.</summary>
    public ValueList<string> Notes { get; init; } = ValueList<string>.Empty;

    /// <summary>A model wrote the wording of the steps, the inputs, the decisions and the obstacles (else they came straight from the recording).</summary>
    public bool ModelWritten { get; init; }

    /// <summary>The version of the file this was read from.</summary>
    public int Version { get; init; } = CurrentVersion;

    public const int CurrentVersion = 1;

    /// <summary>Fields a newer build wrote that this one does not know, as JSON text by name; they survive a save.</summary>
    public ValueMap<string, string> Unknown { get; init; } = ValueMap<string, string>.Empty;

    /// <summary>The approval boundary: every step that asks the owner.</summary>
    public IReadOnlyList<TeachStep> Boundary => [.. Steps.Where(s => s.AsksYou)];

    public IReadOnlyList<TeachStep> Guarded => [.. Steps.Where(s => s.Guard is not null)];

    public IReadOnlyList<TeachQuestion> OpenQuestions => [.. Questions.Where(q => q.Answer is null)];

    /// <summary>Null when the draft can be saved as a skill; else the first thing wrong, in the owner's words.</summary>
    public string? Problem()
    {
        if (!Marrow.Core.Port.Skills.Catalogue.SkillName.IsValid(Name))
            return "Name the skill with lowercase letters, digits and single hyphens, up to 64 characters.";
        if (string.IsNullOrWhiteSpace(Description) || Description.Length > DescriptionChars || Description.Any(char.IsControl))
            return $"Describe the skill in one line of at most {DescriptionChars.ToString(CultureInfo.InvariantCulture)} characters.";
        if (Steps.Count == 0) return "The skill has no steps.";
        if (Steps.Count > TeachLimits.Steps) return $"A skill holds at most {TeachLimits.Steps.ToString(CultureInfo.InvariantCulture)} steps.";
        for (var i = 0; i < Steps.Count; i++)
            if (Steps[i].Number != i + 1 || string.IsNullOrWhiteSpace(Steps[i].Text)) return $"Step {(i + 1).ToString(CultureInfo.InvariantCulture)} is empty or out of order.";
        return null;
    }

    /// <summary>The draft with the steps numbered from 1 in order.</summary>
    public TeachDraft Renumbered() => this with { Steps = new ValueList<TeachStep>(Steps.Select((s, i) => s with { Number = i + 1 })) };

    /// <summary>A step flagged or not (the owner's choice); a guarded step asks either way.</summary>
    public TeachDraft Flag(int number, bool flagged) => this with
    {
        Steps = new ValueList<TeachStep>(Steps.Select(s => s.Number == number ? s with { Flagged = flagged } : s)),
    };

    /// <summary>The wording of one step (the owner's edit). Its guard stays: only code decides that.</summary>
    public TeachDraft Reword(int number, string text) => this with
    {
        Steps = new ValueList<TeachStep>(Steps.Select(s => s.Number == number ? s with { Text = Clean(text, TextChars) } : s)),
    };

    /// <summary>A step taken out. The steps are numbered again. A guarded step can be taken out only by the owner, never by a model.</summary>
    public TeachDraft Remove(int number) => (this with { Steps = new ValueList<TeachStep>(Steps.Where(s => s.Number != number)) }).Renumbered();

    /// <summary>
    /// Turns the text typed in a step into an input called <paramref name="name"/>: the example is kept, the step says
    /// {name} where the text was. False when the step typed nothing, or the name is taken, or the text was a secret.
    /// </summary>
    public bool TryMakeInput(int step, string name, string example, out TeachDraft result)
    {
        result = this;
        var key = InputName(name);
        if (key is null || Parameters.Count >= TeachLimits.Parameters || Parameters.Any(p => p.Name == key)) return false;
        var target = Steps.FirstOrDefault(s => s.Number == step);
        if (target is null || target.Kind != TeachEventKind.Type || target.Guard?.Class == ActionClass.HardDeny || string.IsNullOrEmpty(example)) return false;
        var marker = "{" + key + "}";
        var swapped = target.Text.Contains("“" + example + "”", StringComparison.Ordinal)
            ? target.Text.Replace("“" + example + "”", "“" + marker + "”", StringComparison.Ordinal)
            : target.Text + " (" + marker + ")";
        result = this with
        {
            Parameters = Parameters.Appending(new TeachParameter(key, Clean(example, 80), $"step {step.ToString(CultureInfo.InvariantCulture)}")),
            Steps = new ValueList<TeachStep>(Steps.Select(s => s.Number == step ? s with { Text = swapped } : s)),
        };
        return true;
    }

    /// <summary>The name of an input: lowercase letters, digits and underscores, up to 32, starting with a letter; else null.</summary>
    public static string? InputName(string? name)
    {
        var folded = (name ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return InputShape().IsMatch(folded) ? folded : null;
    }

    /// <summary>A skill name from words: lowercase, digits and single hyphens, cut to 64; "taught-skill" when nothing is left.</summary>
    public static string SkillNameFrom(string? words)
    {
        var builder = new StringBuilder();
        foreach (var c in (words ?? "").Normalize(NormalizationForm.FormKD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var name = builder.ToString().Trim('-');
        if (name.Length > Marrow.Core.Port.Skills.Catalogue.SkillName.MaxLength) name = name[..Marrow.Core.Port.Skills.Catalogue.SkillName.MaxLength].Trim('-');
        return name.Length == 0 ? "taught-skill" : name;
    }

    /// <summary>One line of text: control characters and runs of space folded, cut to <paramref name="limit"/> (never inside a surrogate pair).</summary>
    public static string Clean(string? text, int limit)
    {
        var builder = new StringBuilder();
        var space = false;
        foreach (var c in text ?? "")
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        var line = builder.ToString();
        if (line.Length <= limit) return line;
        var cut = limit;
        if (char.IsHighSurrogate(line[cut - 1])) cut--;
        return line[..cut].TrimEnd() + "…";
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex InputShape();
}
