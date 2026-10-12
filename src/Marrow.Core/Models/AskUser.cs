using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// PORT-ONLY (web surfaces W25, DESIGN.md 7.3 `ask_user`, 14 W25; ED-NEW-web-w25-1). The data of an ask_user card: nth's own
// blocking question to the person, asked by a model through the `ask_user` tool (any provider) and kept in the thread as
// one timeline item. A card holds one to four questions (single choice, several choices, text, number), its state
// (waiting, answered, skipped, stopped) and the answers. No Swift counterpart: it is separate from QuestionRequest, the
// provider's own question (Claude's AskUserQuestion, Codex's user input), which keeps its badge and sheet.
//
// The card is plain data and round-trips through the thread's storage JSON (TimelineContentCoding "ask"): a thread that
// quit while a question waited reopens with the question still waiting, and answering it then resumes the turn.

/// <summary>What kind of answer a question takes. The wire names are single, multiple, text and number.</summary>
public enum AskKind
{
    Single,
    Multiple,
    Text,
    Number,
}

/// <summary>Where a card stands. A card leaves <see cref="Waiting"/> only by the person's action or a Stop.</summary>
public enum AskState
{
    /// <summary>Waiting for the person. After a restart it is still waiting; an answer then starts a new turn with the answer.</summary>
    Waiting,
    /// <summary>Answered; <see cref="AskCard.Answers"/> holds the answers.</summary>
    Answered,
    /// <summary>The person chose not to answer.</summary>
    Skipped,
    /// <summary>The turn was stopped (Stop) before the person answered.</summary>
    Stopped,
}

/// <summary>One choice of a choice question: its label (also the answer) and an optional line under it.</summary>
public sealed record AskOption(string Label, string? Detail = null);

/// <summary>One question of a card.</summary>
public sealed record AskQuestion
{
    /// <summary>Unique within the card; the key of its answer. Letters, digits, hyphens and underscores.</summary>
    public required string Id { get; init; }

    public required string Prompt { get; init; }

    /// <summary>A short label above the prompt ("Database"); none when null.</summary>
    public string? Header { get; init; }

    public AskKind Kind { get; init; } = AskKind.Text;

    /// <summary>The choices of a single or multiple choice question (two to twelve); empty for text and number.</summary>
    public ValueList<AskOption> Options { get; init; } = ValueList<AskOption>.Empty;

    /// <summary>A choice question also takes the person's own words as its answer.</summary>
    public bool AllowsOther { get; init; }

    /// <summary>False lets the person leave the question without an answer.</summary>
    public bool Required { get; init; } = true;

    public string? Placeholder { get; init; }

    /// <summary>A text question takes several lines.</summary>
    public bool Multiline { get; init; }

    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>The step of the number field's buttons and arrow keys; 1 when null.</summary>
    public double? Step { get; init; }

    public bool WholeNumber { get; init; }

    /// <summary>A unit shown beside the number ("GB").</summary>
    public string? Unit { get; init; }

    /// <summary>The answer the card starts with: an option's label, a number or text.</summary>
    public string? Default { get; init; }

    public bool IsChoice => Kind is AskKind.Single or AskKind.Multiple;
}

/// <summary>An ask_user card: the questions, where it stands and the answers.</summary>
public sealed record AskCard
{
    /// <summary>The card's id; waits and answers are keyed by it.</summary>
    public required string Id { get; init; }

    public required ValueList<AskQuestion> Questions { get; init; }

    public AskState State { get; init; } = AskState.Waiting;

    /// <summary>The answers by question id: a choice question's labels (and the person's own words), the text, the number in its invariant form.</summary>
    public ValueMap<string, ValueList<string>> Answers { get; init; } = ValueMap<string, ValueList<string>>.Empty;

    public DateTimeOffset AskedAt { get; init; } = SwiftDate.Now;

    /// <summary>When the card left <see cref="AskState.Waiting"/>.</summary>
    public DateTimeOffset? ClosedAt { get; init; }

    public bool IsWaiting => State == AskState.Waiting;

    /// <summary>The same card, closed in <paramref name="state"/> (answers only for <see cref="AskState.Answered"/>).</summary>
    public AskCard Closed(AskState state, ValueMap<string, ValueList<string>>? answers = null) => this with
    {
        State = state,
        Answers = state == AskState.Answered ? answers ?? Answers : Answers,
        ClosedAt = ClosedAt ?? SwiftDate.Now,
    };
}

internal static class AskKinds
{
    public static string Wire(AskKind kind) => kind switch
    {
        AskKind.Single => "single",
        AskKind.Multiple => "multiple",
        AskKind.Text => "text",
        AskKind.Number => "number",
    };

    public static AskKind? FromWire(string? text) => text switch
    {
        "single" => AskKind.Single,
        "multiple" => AskKind.Multiple,
        "text" => AskKind.Text,
        "number" => AskKind.Number,
        _ => null,
    };

    public static string Wire(AskState state) => state switch
    {
        AskState.Waiting => "waiting",
        AskState.Answered => "answered",
        AskState.Skipped => "skipped",
        AskState.Stopped => "stopped",
    };

    public static AskState? StateFromWire(string? text) => text switch
    {
        "waiting" => AskState.Waiting,
        "answered" => AskState.Answered,
        "skipped" => AskState.Skipped,
        "stopped" => AskState.Stopped,
        _ => null,
    };
}

/// <summary>The storage coding of <see cref="AskCard"/>: every member optional on read and a malformed one dropped, so one bad field never costs a thread.</summary>
internal static class AskCardCoding
{
    private static string? OptionalString(JsonElement element, string key) =>
        SwiftDecoding.Value<string?>(element, key, field => SwiftDecoding.ReadString(field), () => null);

    private static double? OptionalNumber(JsonElement element, string key) =>
        SwiftDecoding.Value<double?>(element, key, field => SwiftDecoding.ReadDouble(field), () => null);

    private static bool Flag(JsonElement element, string key, bool fallback) =>
        SwiftDecoding.Value(element, key, SwiftDecoding.ReadBool, () => fallback);

    private static AskOption DecodeOption(JsonElement element) =>
        new(SwiftDecoding.Decode(element, "label", SwiftDecoding.ReadString), OptionalString(element, "detail"));

    private static AskQuestion DecodeQuestion(JsonElement element)
    {
        var kind = AskKinds.FromWire(OptionalString(element, "kind")) ?? AskKind.Text;
        return new AskQuestion
        {
            Id = SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadString),
            Prompt = SwiftDecoding.Decode(element, "prompt", SwiftDecoding.ReadString),
            Header = OptionalString(element, "header"),
            Kind = kind,
            Options = SwiftDecoding.LenientList(element, "options", DecodeOption, () => ValueList<AskOption>.Empty),
            AllowsOther = Flag(element, "allowsOther", false),
            Required = Flag(element, "required", true),
            Placeholder = OptionalString(element, "placeholder"),
            Multiline = Flag(element, "multiline", false),
            Min = OptionalNumber(element, "min"),
            Max = OptionalNumber(element, "max"),
            Step = OptionalNumber(element, "step"),
            WholeNumber = Flag(element, "whole", false),
            Unit = OptionalString(element, "unit"),
            Default = OptionalString(element, "default"),
        };
    }

    internal static AskCard Decode(JsonElement element)
    {
        var answers = new List<KeyValuePair<string, ValueList<string>>>();
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("answers", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in map.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;
                var values = new List<string>();
                foreach (var item in property.Value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) values.Add(item.GetString()!);
                answers.Add(KeyValuePair.Create(property.Name, new ValueList<string>(values)));
            }
        }
        return new AskCard
        {
            Id = SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadString),
            Questions = SwiftDecoding.LenientList(element, "questions", DecodeQuestion, () => ValueList<AskQuestion>.Empty),
            State = AskKinds.StateFromWire(OptionalString(element, "state")) ?? AskState.Waiting,
            Answers = new ValueMap<string, ValueList<string>>(answers, StringComparer.Ordinal),
            AskedAt = SwiftDecoding.Value(element, "askedAt", SwiftDecoding.ReadStorageDate, () => SwiftDate.Now),
            ClosedAt = SwiftDecoding.Value<DateTimeOffset?>(element, "closedAt", field => SwiftDecoding.ReadStorageDate(field), () => null),
        };
    }

    internal static void WriteMembers(Utf8JsonWriter writer, AskCard card)
    {
        writer.WriteStartObject();
        writer.WriteString("id", card.Id);
        writer.WriteString("state", AskKinds.Wire(card.State));
        writer.WriteString("askedAt", FoundationISO8601.Format(card.AskedAt));
        if (card.ClosedAt is { } closed) writer.WriteString("closedAt", FoundationISO8601.Format(closed));
        writer.WritePropertyName("questions");
        writer.WriteStartArray();
        foreach (var question in card.Questions) WriteQuestion(writer, question);
        writer.WriteEndArray();
        writer.WritePropertyName("answers");
        writer.WriteStartObject();
        foreach (var (key, values) in card.Answers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            writer.WriteStartArray();
            foreach (var value in values) writer.WriteStringValue(value);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteQuestion(Utf8JsonWriter writer, AskQuestion question)
    {
        writer.WriteStartObject();
        writer.WriteString("id", question.Id);
        writer.WriteString("prompt", question.Prompt);
        if (question.Header is { } header) writer.WriteString("header", header);
        writer.WriteString("kind", AskKinds.Wire(question.Kind));
        if (question.Options.Count != 0)
        {
            writer.WritePropertyName("options");
            writer.WriteStartArray();
            foreach (var option in question.Options)
            {
                writer.WriteStartObject();
                writer.WriteString("label", option.Label);
                if (option.Detail is { } detail) writer.WriteString("detail", detail);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        if (question.AllowsOther) writer.WriteBoolean("allowsOther", true);
        if (!question.Required) writer.WriteBoolean("required", false);
        if (question.Placeholder is { } placeholder) writer.WriteString("placeholder", placeholder);
        if (question.Multiline) writer.WriteBoolean("multiline", true);
        if (question.Min is { } min) writer.WriteNumber("min", min);
        if (question.Max is { } max) writer.WriteNumber("max", max);
        if (question.Step is { } step) writer.WriteNumber("step", step);
        if (question.WholeNumber) writer.WriteBoolean("whole", true);
        if (question.Unit is { } unit) writer.WriteString("unit", unit);
        if (question.Default is { } initial) writer.WriteString("default", initial);
        writer.WriteEndObject();
    }
}
