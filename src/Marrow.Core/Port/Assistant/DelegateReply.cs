// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3; unit AS-22; ED-NEW-dextra-as22-3). No Swift twin.
// What the delegate's model said, read defensively. The model is asked for one JSON object (the answer to each question, how sure it is,
// what the answer rests on, a flag for the topics the owner always confirms, a quote that proves a recorded decision, and a reason). Its
// text may wrap that object in prose or a code fence, so the first object that holds an "answers" member is taken. Anything that does not
// read cleanly gives null (the delegate then leaves the question to the owner): a duplicate member, a nested answer, a number that is
// not a number. Pure.
using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>What the model says the answer rests on.</summary>
public enum DelegateBasis
{
    /// <summary>A plain technical fact or inference the project's code or the chat settles.</summary>
    Technical,
    /// <summary>A decision the owner already recorded (a decisions note, a brief, a past answer): it quotes where.</summary>
    Decided,
    /// <summary>An open design or taste pick.</summary>
    Taste,
    /// <summary>It does not know.</summary>
    Unsure,
}

/// <summary>The model's reading of one ask.</summary>
/// <param name="Answers">Each question's id and the labels or words it picked.</param>
/// <param name="Confidence">0 to 1 (a number in 1 to 100 is read as a percent).</param>
/// <param name="Basis">What the answer rests on.</param>
/// <param name="Flag">A topic the model says the question touches (money, credentials, ...), or null.</param>
/// <param name="Evidence">A quote from the project's decisions, past answers or the owner's words that settles it, or "".</param>
/// <param name="Reason">One or two sentences of why, kept in the log and shown on Override.</param>
public sealed record DelegateReply(ValueMap<string, ValueList<string>> Answers, double Confidence, DelegateBasis Basis, DelegateReason? Flag, string Evidence, string Reason)
{
    /// <summary>The longest reply text read.</summary>
    public const int MaxChars = 20_000;

    /// <summary>How many objects of a text are tried before giving up.</summary>
    public const int MaxTries = 8;

    /// <summary>The longest evidence quote or reason kept.</summary>
    public const int NoteChars = 600;

    private static readonly JsonDocumentOptions Strict = new() { AllowTrailingCommas = true, MaxDepth = 8, AllowDuplicateProperties = false };

    /// <summary>The reply in a model's text, or null when it holds no readable one.</summary>
    public static DelegateReply? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxChars) return null;
        var tries = 0;
        for (var start = text.IndexOf('{', StringComparison.Ordinal); start >= 0 && tries < MaxTries; start = text.IndexOf('{', start + 1))
        {
            tries++;
            var end = Closing(text, start);
            if (end < 0) continue;
            if (Read(text.AsSpan(start, end - start + 1).ToString()) is { } reply) return reply;
        }
        return null;
    }

    // The index of the brace that closes the one at <start>, with strings and escapes honoured; -1 when it never closes.
    private static int Closing(string text, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static DelegateReply? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, Strict);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object) return null;
            var map = new Dictionary<string, ValueList<string>>(StringComparer.Ordinal);
            foreach (var property in answers.EnumerateObject())
            {
                var values = new List<string>();
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        values.Add(property.Value.GetString() ?? "");
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.String) return null;
                            values.Add(item.GetString() ?? "");
                        }
                        break;
                    default:
                        return null;
                }
                map[property.Name] = new ValueList<string>(values);
            }
            return new DelegateReply(new ValueMap<string, ValueList<string>>(map), ConfidenceOf(root), BasisOf(Text(root, "basis")), FlagOf(Text(root, "flag")),
                Clip(Text(root, "evidence")), Clip(Text(root, "reason")));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Clip(string text) => text.Length <= NoteChars ? text.Trim() : text[..NoteChars].Trim();

    // 0 to 1; a number from 1 to 100 is a percent; anything else (missing, negative, not finite, over 100) is 0, which never passes the threshold.
    private static double ConfidenceOf(JsonElement root)
    {
        if (!root.TryGetProperty("confidence", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)) return 0;
        if (!double.IsFinite(number) || number < 0 || number > 100) return 0;
        return number > 1 ? number / 100 : number;
    }

    private static DelegateBasis BasisOf(string word) => word.Trim().ToLowerInvariant() switch
    {
        "decided" => DelegateBasis.Decided,
        "technical" => DelegateBasis.Technical,
        "taste" => DelegateBasis.Taste,
        _ => DelegateBasis.Unsure,
    };

    // "none" or nothing is no flag; a word nth knows is that topic; any other word is read as "not sure" (fail closed).
    private static DelegateReason? FlagOf(string word) => word.Trim().ToLowerInvariant() switch
    {
        "" or "none" or "null" or "no" or "n/a" => null,
        "money" or "payment" or "purchase" => DelegateReason.Money,
        "credentials" or "credential" or "secret" or "password" or "login" => DelegateReason.Credentials,
        "outward" or "sending" or "send" or "publish" => DelegateReason.Outward,
        "destructive" or "deleting" or "delete" => DelegateReason.Destructive,
        "install" or "installing" => DelegateReason.Install,
        "commitment" or "promise" or "booking" => DelegateReason.Commitment,
        "taste" or "design" => DelegateReason.Taste,
        _ => DelegateReason.LowConfidence,
    };
}
