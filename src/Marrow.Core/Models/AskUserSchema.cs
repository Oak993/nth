using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port;

namespace Marrow.Core.Models;

// PORT-ONLY (web surfaces W25, DESIGN.md 7.3 `ask_user`; ED-NEW-web-w25-1). How an ask_user call becomes a card, how the
// person's input becomes answers, and the words the model reads back. Arguments are read leniently (a small model's
// questions sent as a JSON string, a single question without the list, "radio" or "checkbox" for a kind, an option as a
// bare string); a call that cannot be read gives errors written for the model, which can then call again. Our own words;
// nothing here is copied from another product. No Swift counterpart.

/// <summary>The outcome of reading a call's arguments: the card, or errors the model can act on.</summary>
public sealed record AskParse(AskCard? Card, ValueList<string> Errors)
{
    public bool Ok => Card is not null;
}

/// <summary>The outcome of checking the person's input: the answers to keep, and a problem to show for each question that cannot be taken yet.</summary>
public sealed record AskCheck(ValueMap<string, ValueList<string>> Answers, ValueMap<string, string> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

public static class AskUserSchema
{
    public const int MaxQuestions = 4;
    public const int MinOptions = 2;
    public const int MaxOptions = 12;
    public const int MaxPrompt = 500;
    public const int MaxHeader = 24;
    public const int MaxLabel = 120;
    public const int MaxDetail = 300;
    public const int MaxPlaceholder = 120;
    public const int MaxText = 4000;
    public const int MaxOtherText = 400;

    private static readonly string[] PromptKeys = ["question", "prompt", "text", "message", "title"];
    private static readonly string[] KindKeys = ["type", "kind", "answer_type", "answerType", "input"];
    private static readonly string[] OptionKeys = ["options", "choices", "answers", "items"];
    private static readonly string[] LabelKeys = ["label", "text", "title", "value", "name", "option"];
    private static readonly string[] DetailKeys = ["description", "detail", "hint", "subtitle"];

    /// <summary>Reads the arguments of one ask_user call into a waiting card with the id <paramref name="cardId"/>.</summary>
    public static AskParse Parse(JsonNode? arguments, string cardId, DateTimeOffset? now = null)
    {
        var errors = new List<string>();
        var root = AsObject(arguments);
        if (root is null)
        {
            return Failed("The arguments must be an object with a questions list, like {\"questions\":[{\"question\":\"Which database?\",\"type\":\"single\",\"options\":[\"PostgreSQL\",\"SQLite\"]}]}.");
        }

        IReadOnlyList<JsonNode?> items;
        var list = root["questions"] ?? root["Questions"];
        if (list is JsonValue { } text && text.TryGetValue<string>(out var json)) list = ReadJson(json);
        if (list is JsonArray array) items = [.. array];
        else if (list is JsonObject single) items = [single];
        else if (PromptKeys.Any(key => root.ContainsKey(key))) items = [root]; // one question without the list
        else return Failed("questions is required: a list of one to " + MaxQuestions.ToString(CultureInfo.InvariantCulture) + " questions.");
        if (items.Count == 0) return Failed("questions is empty: ask at least one question.");
        if (items.Count > MaxQuestions)
            errors.Add("At most " + MaxQuestions.ToString(CultureInfo.InvariantCulture) + " questions at a time (you sent " + items.Count.ToString(CultureInfo.InvariantCulture) + "); ask the rest after the person has answered.");

        var questions = new List<AskQuestion>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Math.Min(items.Count, MaxQuestions); index++)
        {
            var number = index + 1;
            if (AsObject(items[index]) is not { } node)
            {
                errors.Add("Question " + number.ToString(CultureInfo.InvariantCulture) + " must be an object.");
                continue;
            }
            if (Question(node, number, ids, errors) is { } question) questions.Add(question);
        }
        if (errors.Count != 0) return new AskParse(null, new ValueList<string>(errors));
        return new AskParse(new AskCard { Id = cardId, Questions = new ValueList<AskQuestion>(questions), AskedAt = now ?? SwiftDate.Now }, ValueList<string>.Empty);

        static AskParse Failed(string message) => new(null, new ValueList<string>([message]));
    }

    private static AskQuestion? Question(JsonObject node, int number, HashSet<string> ids, List<string> errors)
    {
        var label = "Question " + number.ToString(CultureInfo.InvariantCulture);
        var prompt = Clip(Text(node, PromptKeys), MaxPrompt);
        if (prompt is null)
        {
            errors.Add(label + " has no question text (use the \"question\" field).");
            return null;
        }
        label += " (\"" + Clip(prompt, 40) + "\")";

        var options = Options(node, errors, label);
        var kind = KindOf(node, options.Count != 0, errors, label);
        if (kind is null) return null;
        var isChoice = kind is AskKind.Single or AskKind.Multiple;
        if (isChoice && options.Count < MinOptions)
        {
            errors.Add(label + ": a " + (kind == AskKind.Single ? "single" : "multiple") + " choice needs " + MinOptions.ToString(CultureInfo.InvariantCulture)
                + " to " + MaxOptions.ToString(CultureInfo.InvariantCulture) + " options" + (options.Count == 1 ? " (it has one)" : "") + "; for an open answer use type \"text\".");
            return null;
        }
        if (options.Count > MaxOptions)
        {
            errors.Add(label + ": at most " + MaxOptions.ToString(CultureInfo.InvariantCulture) + " options (it has " + options.Count.ToString(CultureInfo.InvariantCulture) + "); offer the likeliest and let the person write another.");
            return null;
        }

        var id = Id(Text(node, ["id"]), ids, number);
        var required = Flag(node, "required") ?? !(Flag(node, "optional") ?? false);
        var question = new AskQuestion
        {
            Id = id,
            Prompt = prompt,
            Header = Clip(Text(node, ["header", "label", "heading"]), MaxHeader),
            Kind = kind.Value,
            Required = required,
            Options = isChoice ? new ValueList<AskOption>(options) : ValueList<AskOption>.Empty,
            AllowsOther = isChoice && (Flag(node, "allow_other", "allowOther", "allowsOther", "other", "allow_custom", "allowCustom", "allow_custom_answer") ?? true),
            Placeholder = Clip(Text(node, ["placeholder", "hint"]), MaxPlaceholder),
        };
        switch (kind)
        {
            case AskKind.Text:
                question = question with { Multiline = Flag(node, "multiline", "long", "paragraph", "multi_line") ?? false, Default = Clip(Text(node, ["default", "initial"]), MaxText) };
                break;
            case AskKind.Number:
                return Number(node, question, errors, label);
            case AskKind.Single:
                question = question with { Default = options.FirstOrDefault(option => string.Equals(option.Label, Text(node, ["default", "initial"]), StringComparison.OrdinalIgnoreCase))?.Label };
                break;
        }
        return question;
    }

    private static AskQuestion? Number(JsonObject node, AskQuestion question, List<string> errors, string label)
    {
        var min = Digit(node, "min", "minimum");
        var max = Digit(node, "max", "maximum");
        if (min is { } low && max is { } high && low > high)
        {
            errors.Add(label + ": min is more than max.");
            return null;
        }
        var step = Digit(node, "step", "increment");
        var whole = (Flag(node, "integer", "whole", "wholeNumber", "whole_number", "int") ?? false) || IsIntegerKind(Text(node, KindKeys));
        var initial = Digit(node, "default", "initial", "value");
        if (initial is { } start && (min is { } floor && start < floor || max is { } ceiling && start > ceiling || whole && !IsWhole(start))) initial = null;
        return question with
        {
            Min = min,
            Max = max,
            Step = step is > 0 ? step : null,
            WholeNumber = whole,
            Unit = Clip(Text(node, ["unit", "units", "suffix"]), 16),
            Default = initial is { } value ? Canonical(value) : null,
        };
    }

    private static AskKind? KindOf(JsonObject node, bool hasOptions, List<string> errors, string label)
    {
        var multi = Flag(node, "multiple", "multi", "multi_select", "multiSelect", "multiselect", "allow_multiple", "allowMultiple") ?? false;
        if (Text(node, KindKeys) is not { } raw)
            return hasOptions ? (multi ? AskKind.Multiple : AskKind.Single) : AskKind.Text;
        var word = raw.Trim().ToLowerInvariant().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        switch (word)
        {
            case "single" or "singlechoice" or "choice" or "radio" or "select" or "one" or "pickone" or "dropdown" or "option" or "options" or "enum" or "boolean" or "bool" or "yesno":
                return multi ? AskKind.Multiple : AskKind.Single;
            case "multiple" or "multi" or "multiplechoice" or "multiselect" or "checkbox" or "checkboxes" or "many" or "pickmany" or "list" or "array":
                return AskKind.Multiple;
            case "text" or "string" or "free" or "freetext" or "open" or "input" or "textarea" or "paragraph" or "longtext" or "shorttext":
                return AskKind.Text;
            case "number" or "numeric" or "integer" or "int" or "float" or "double" or "decimal" or "range" or "slider":
                return AskKind.Number;
            default:
                errors.Add(label + ": type \"" + Clip(raw, 30) + "\" is not one of single, multiple, text, number.");
                return null;
        }
    }

    private static bool IsIntegerKind(string? kind) => kind?.Trim().ToLowerInvariant() is "integer" or "int";

    private static List<AskOption> Options(JsonObject node, List<string> errors, string label)
    {
        var result = new List<AskOption>();
        JsonNode? source = null;
        foreach (var key in OptionKeys)
            if (node[key] is { } found) { source = found; break; }
        if (source is JsonValue { } asText && asText.TryGetValue<string>(out var json) && json.TrimStart().StartsWith('[')) source = ReadJson(json);
        if (source is not JsonArray array) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array)
        {
            string? name; string? detail = null;
            if (item is JsonObject entry)
            {
                name = Text(entry, LabelKeys);
                detail = Clip(Text(entry, DetailKeys), MaxDetail);
            }
            else name = AsString(item);
            name = Clip(name, MaxLabel);
            if (name is null) continue;
            if (!seen.Add(name))
            {
                errors.Add(label + ": the option \"" + Clip(name, 40) + "\" appears twice.");
                continue;
            }
            result.Add(new AskOption(name, detail));
        }
        return result;
    }

    private static string Id(string? wanted, HashSet<string> taken, int number)
    {
        var id = new string((wanted ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
        if (id.Length == 0) id = "q" + number.ToString(CultureInfo.InvariantCulture);
        var candidate = id;
        for (var suffix = 2; !taken.Add(candidate); suffix++) candidate = id + "-" + suffix.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    // ---- reading JSON leniently ----

    private static JsonNode? ReadJson(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static JsonObject? AsObject(JsonNode? node) => node switch
    {
        JsonObject obj => obj,
        JsonValue value when value.TryGetValue<string>(out var text) && text.TrimStart().StartsWith('{') => ReadJson(text) as JsonObject,
        _ => null,
    };

    private static string? AsString(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<string>(out var text)) return text;
        if (value.TryGetValue<bool>(out var flag)) return flag ? "Yes" : "No";
        if (value.TryGetValue<double>(out var number)) return Canonical(number);
        return null;
    }

    private static string? Text(JsonObject node, IEnumerable<string> keys)
    {
        foreach (var key in keys)
            if (node[key] is JsonValue { } value && value.TryGetValue<string>(out var text) && text.Trim().Length != 0) return text.Trim();
        return null;
    }

    private static bool? Flag(JsonObject node, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (node[key] is not JsonValue value) continue;
            if (value.TryGetValue<bool>(out var flag)) return flag;
            if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)) return parsed;
        }
        return null;
    }

    private static double? Digit(JsonObject node, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (node[key] is not JsonValue value) continue;
            if (value.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
            if (value.TryGetValue<string>(out var text) && TryNumber(text, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return null;
    }

    private static string? Clip(string? text, int limit)
    {
        if (text is null) return null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length <= limit) return trimmed;
        var cut = limit - 1;
        if (char.IsHighSurrogate(trimmed[cut - 1])) cut--;
        return trimmed[..cut].TrimEnd() + "…";
    }

    // ---- numbers ----

    /// <summary>A number in the form an answer keeps and the model reads: invariant, no exponent for ordinary values, no trailing zeros.</summary>
    public static string Canonical(double value)
    {
        if (IsWhole(value) && Math.Abs(value) < 1e15) return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static bool IsWhole(double value) => Math.Abs(value - Math.Round(value)) < 1e-9;

    /// <summary>
    /// Reads what a person typed: in their own culture first, then in the invariant form (so "3.5" works everywhere). It never guesses
    /// at a grouped number: "1,000" and "1.000" are read only where the culture says what the mark means, and a space inside a number is refused.
    /// </summary>
    public static bool TryNumber(string? text, CultureInfo culture, out double value)
    {
        value = 0;
        if (text is null) return false;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        const NumberStyles style = NumberStyles.Float;
        if (double.TryParse(trimmed, style, culture, out value) && double.IsFinite(value)) return true;
        if (!LooksGrouped(trimmed) && double.TryParse(trimmed, style, CultureInfo.InvariantCulture, out value) && double.IsFinite(value)) return true;
        value = 0;
        return false;
    }

    // "1.000" is a thousand where a comma means the decimal point, and one where a point does: never decided for the person.
    private static bool LooksGrouped(string text)
    {
        var index = text.Length != 0 && text[0] is '+' or '-' ? 1 : 0;
        var digits = 0;
        var groups = 0;
        for (; index < text.Length; index++)
        {
            if (char.IsAsciiDigit(text[index])) { digits++; continue; }
            if (text[index] is not ('.' or ',') || groups == 0 && digits is 0 or > 3 || groups > 0 && digits != 3) return false;
            groups++;
            digits = 0;
        }
        return groups > 0 && digits == 3;
    }

    // ---- the person's input ----

    /// <summary>
    /// Checks the person's input against the card's questions and returns the answers to keep (numbers in their invariant form) and
    /// a problem for each question that cannot be taken yet. <paramref name="given"/> maps a question's id to what the person chose
    /// or typed: one value for single, text and number, any number for multiple.
    /// </summary>
    public static AskCheck Check(AskCard card, IReadOnlyDictionary<string, IReadOnlyList<string>> given, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var answers = new List<KeyValuePair<string, ValueList<string>>>();
        var problems = new List<KeyValuePair<string, string>>();
        foreach (var question in card.Questions)
        {
            var raw = given.TryGetValue(question.Id, out var values) ? values : [];
            var entries = raw.Select(value => value?.Trim() ?? "").Where(value => value.Length != 0).ToList();
            if (entries.Count == 0)
            {
                if (question.Required) problems.Add(KeyValuePair.Create(question.Id, Missing(question)));
                continue;
            }
            switch (question.Kind)
            {
                case AskKind.Single:
                case AskKind.Multiple:
                {
                    if (question.Kind == AskKind.Single && entries.Count > 1) entries = [entries[0]];
                    var kept = new List<string>();
                    string? problem = null;
                    foreach (var entry in entries)
                    {
                        var match = question.Options.FirstOrDefault(option => string.Equals(option.Label, entry, StringComparison.OrdinalIgnoreCase));
                        if (match is not null) { if (!kept.Contains(match.Label, StringComparer.Ordinal)) kept.Add(match.Label); }
                        else if (!question.AllowsOther) problem = AskUserWords.NotAnOption;
                        else if (entry.Length > MaxOtherText) problem = AskUserWords.TooLong(MaxOtherText);
                        else if (!kept.Contains(entry, StringComparer.Ordinal)) kept.Add(entry);
                    }
                    if (problem is not null) problems.Add(KeyValuePair.Create(question.Id, problem));
                    else answers.Add(KeyValuePair.Create(question.Id, new ValueList<string>(kept)));
                    break;
                }
                case AskKind.Text:
                {
                    var text = string.Join("\n", entries);
                    if (text.Length > MaxText) problems.Add(KeyValuePair.Create(question.Id, AskUserWords.TooLong(MaxText)));
                    else answers.Add(KeyValuePair.Create(question.Id, new ValueList<string>([text])));
                    break;
                }
                case AskKind.Number:
                {
                    if (!TryNumber(entries[0], culture, out var number)) problems.Add(KeyValuePair.Create(question.Id, AskUserWords.NotANumber));
                    else if (question.WholeNumber && !IsWhole(number)) problems.Add(KeyValuePair.Create(question.Id, AskUserWords.NotWhole));
                    else if (question.Min is { } min && number < min) problems.Add(KeyValuePair.Create(question.Id, AskUserWords.AtLeast(min, culture)));
                    else if (question.Max is { } max && number > max) problems.Add(KeyValuePair.Create(question.Id, AskUserWords.AtMost(max, culture)));
                    else answers.Add(KeyValuePair.Create(question.Id, new ValueList<string>([Canonical(number)])));
                    break;
                }
            }
        }
        return new AskCheck(new ValueMap<string, ValueList<string>>(answers, StringComparer.Ordinal), new ValueMap<string, string>(problems, StringComparer.Ordinal));
    }

    private static string Missing(AskQuestion question) => question.Kind switch
    {
        AskKind.Single => AskUserWords.PickOne,
        AskKind.Multiple => AskUserWords.PickAtLeastOne,
        AskKind.Number => AskUserWords.NotANumber,
        _ => AskUserWords.WriteAnswer,
    };
}

/// <summary>The words an ask_user card says to the person (problems, the answer in one line) and to the model (what the call returns).</summary>
public static class AskUserWords
{
    /// <summary>The state a waiting turn shows (DESIGN.md 7.3).</summary>
    public const string Waiting = "Waiting for your answer";

    public const string PickOne = "Pick one.";
    public const string PickAtLeastOne = "Pick at least one.";
    public const string WriteAnswer = "Write an answer.";
    public const string NotAnOption = "Pick one of the choices.";
    public const string NotANumber = "Enter a number.";
    public const string NotWhole = "Enter a whole number.";

    public static string TooLong(int limit) => "Keep it under " + limit.ToString(CultureInfo.InvariantCulture) + " characters.";

    public static string AtLeast(double min, CultureInfo culture) => "At least " + Shown(min, culture) + ".";

    public static string AtMost(double max, CultureInfo culture) => "At most " + Shown(max, culture) + ".";

    /// <summary>A number as the person reads it: their culture's separators, no needless decimals.</summary>
    public static string Shown(double number, CultureInfo? culture = null) =>
        number.ToString(AskUserSchema.IsWhole(number) ? "0" : "0.##########", culture ?? CultureInfo.CurrentCulture);

    /// <summary>The answer to a question as one line: the labels joined, the number with its unit, or "No answer".</summary>
    public static string Line(AskQuestion question, ValueList<string>? values, CultureInfo? culture = null)
    {
        if (values is null || values.Count == 0) return "No answer";
        if (question.Kind == AskKind.Number && AskUserSchema.TryNumber(values[0], CultureInfo.InvariantCulture, out var number))
        {
            var shown = Shown(number, culture);
            return question.Unit is { } unit ? shown + " " + unit : shown;
        }
        return string.Join(", ", values);
    }

    /// <summary>What the model reads when the person answered. Our own words.</summary>
    public static string ToolResult(AskCard card)
    {
        var text = new StringBuilder("The user answered");
        text.Append(card.Questions.Count == 1 ? " your question.\n" : " your questions.\n");
        foreach (var (question, index) in card.Questions.Select((question, index) => (question, index)))
        {
            text.Append('\n').Append("Question ").Append(index + 1).Append(": ").Append(question.Prompt).Append('\n');
            text.Append("Answer ").Append(index + 1).Append(": ");
            text.Append(card.Answers.TryGetValue(question.Id, out var values) && values.Count != 0 ? Plain(question, values) : "(no answer; they left it empty)").Append('\n');
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>What the model reads when the person chose not to answer.</summary>
    public static string SkippedResult =>
        "The user chose not to answer. Carry on with your best judgement, and say what you assumed; ask again only if you truly cannot go on without it.";

    /// <summary>What the model reads when the turn was stopped while the question waited.</summary>
    public static string StoppedResult => "The user stopped the turn before answering. Do not continue the task.";

    /// <summary>What the model reads when the question can no longer be answered here (the session ended).</summary>
    public static string DetachedResult => "The question could not be delivered; the session ended. The user can still answer it later.";

    /// <summary>
    /// The message that starts a new turn when the person answers a question whose call is gone (the app was restarted, or the provider ended
    /// the call): self-contained, so the model needs nothing from the lost call.
    /// </summary>
    public static string ResumeMessage(AskCard card)
    {
        var text = new StringBuilder();
        if (card.Questions.Count == 1)
        {
            var question = card.Questions[0];
            text.Append("My answer to your question \"").Append(question.Prompt).Append("\": ");
            text.Append(card.Answers.TryGetValue(question.Id, out var values) && values.Count != 0 ? Plain(question, values) : "(none)");
            return text.Append("\n\nPlease carry on from there.").ToString();
        }
        text.Append("My answers to your questions:\n");
        foreach (var question in card.Questions)
        {
            text.Append("- ").Append(question.Prompt).Append(' ');
            text.Append(card.Answers.TryGetValue(question.Id, out var values) && values.Count != 0 ? Plain(question, values) : "(none)").Append('\n');
        }
        return text.Append("\nPlease carry on from there.").ToString();
    }

    /// <summary>A card as text for a handoff or a transcript.</summary>
    public static string Describe(AskCard card)
    {
        var text = new StringBuilder();
        text.Append("Question to the user (").Append(AskKinds.Wire(card.State)).Append("):\n");
        foreach (var question in card.Questions)
        {
            text.Append(question.Prompt).Append('\n');
            if (card.State == AskState.Answered)
                text.Append("Answer: ").Append(card.Answers.TryGetValue(question.Id, out var values) && values.Count != 0 ? Plain(question, values) : "(no answer)").Append('\n');
        }
        return text.ToString();
    }

    private static string Plain(AskQuestion question, ValueList<string> values)
    {
        if (question.Kind == AskKind.Number && question.Unit is { } unit) return values[0] + " " + unit;
        return question.Kind == AskKind.Multiple ? string.Join(", ", values) : string.Join("\n", values);
    }
}
