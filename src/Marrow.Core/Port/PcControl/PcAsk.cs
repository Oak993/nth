// PORT-ONLY: Tactus, pill v2 (OD-0145, ED-NEW-pcctl-12; DESIGN.md 10.4, 10.9). The wire of pc_ask, the model's short
// question the user can answer from the pill: the question (up to 600 characters), up to four choices of up to 40
// characters, whether a typed answer is allowed (default yes), and the window it is about. The text is plain and is shown
// as the model's words: control and direction-override characters are removed so it cannot fake nth's own text, and a
// question that asks for a secret goes only to the chat. The answer comes back as ok with answer {"choice": ...} or
// {"text": ...}; an unanswered question ends after 30 minutes, or when the user writes in that chat first, as
// declined_by_user "expired" or "moved_on".
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Core.Port.PcControl;

/// <summary>Why an ask ended without an answer (the reason of declined_by_user).</summary>
public static class PcDeclineReason
{
    /// <summary>Nobody answered within <see cref="PcAskRequest.Lifetime"/>.</summary>
    public const string Expired = "expired";

    /// <summary>The user wrote in that chat before answering.</summary>
    public const string MovedOn = "moved_on";

    /// <summary>The user dismissed the question.</summary>
    public const string Dismissed = "dismissed";
}

/// <summary>A pc_ask call, read and checked.</summary>
public sealed record PcAskRequest(string Question, IReadOnlyList<string> Choices, bool AllowText, string? Window)
{
    public const int MaxQuestion = 600;
    public const int MaxChoices = 4;
    public const int MaxChoice = 40;

    /// <summary>An unanswered question ends after this long.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>The question or a choice asks for a secret: it is shown only in the chat, never with a composer in the pill.</summary>
    public bool AsksForSecret => PcSecretWords.AsksForSecret(Question) || Choices.Any(PcSecretWords.AsksForSecret);

    /// <summary>Reads pc_ask's arguments; a wrong or missing one is refused (bad_args), never guessed.</summary>
    public static PcAskRequest Parse(JsonObject args)
    {
        foreach (var (name, _) in args)
        {
            if (name is not ("question" or "choices" or "allow_text" or "window")) throw new PcArgsException("pc_ask takes no " + name);
        }
        var question = Plain(PcArgs.Str(args, "question") ?? throw new PcArgsException("question is required"), multiline: true);
        if (question.Length == 0) throw new PcArgsException("question must not be empty");
        if (question.Length > MaxQuestion) throw new PcArgsException("question must be at most " + MaxQuestion + " characters");
        var choices = new List<string>();
        if (args["choices"] is { } node)
        {
            if (node is not JsonArray array) throw new PcArgsException("choices must be a list of text");
            if (array.Count > MaxChoices) throw new PcArgsException("choices takes at most " + MaxChoices);
            foreach (var item in array)
            {
                if (item is not JsonValue value || !value.TryGetValue<string>(out var text)) throw new PcArgsException("choices must be a list of text");
                var choice = Plain(text, multiline: false);
                if (choice.Length == 0) throw new PcArgsException("a choice must not be empty");
                if (choice.Length > MaxChoice) throw new PcArgsException("a choice must be at most " + MaxChoice + " characters");
                if (choices.Contains(choice, StringComparer.OrdinalIgnoreCase)) throw new PcArgsException("choices must differ");
                choices.Add(choice);
            }
        }
        var allowText = PcArgs.Bool(args, "allow_text", true);
        if (choices.Count == 0 && !allowText) throw new PcArgsException("give choices, or allow a typed answer");
        var window = PcArgs.Str(args, "window");
        return new PcAskRequest(question, choices, allowText, window);
    }

    /// <summary>
    /// Plain text: no control characters (a question keeps its line breaks), no direction overrides or invisible
    /// formatting, spaces collapsed, trimmed.
    /// </summary>
    public static string Plain(string text, bool multiline)
    {
        var clean = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            if (c == '\n' && multiline)
            {
                while (clean.Length > 0 && clean[^1] == ' ') clean.Length--;
                clean.Append('\n');
                space = false;
                continue;
            }
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = true;
                continue;
            }
            if (c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '​' or '⁠' or '﻿' or '‎' or '‏') continue;
            if (space && clean.Length > 0 && clean[^1] != '\n') clean.Append(' ');
            space = false;
            clean.Append(c);
        }
        var result = clean.ToString().Trim();
        while (result.Contains("\n\n\n", StringComparison.Ordinal)) result = result.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        return result;
    }
}

/// <summary>The user's answer to a pc_ask: one of the choices, or typed text.</summary>
public sealed record PcAnswer(string? Choice, string? Text)
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static PcAnswer Chose(string choice) => new(choice, null);

    public static PcAnswer Typed(string text) => new(null, text);

    /// <summary>The body line of the ok result: answer: {"choice":"..."} or answer: {"text":"..."}.</summary>
    public string Render()
    {
        var answer = Choice is not null ? new JsonObject { ["choice"] = Choice } : new JsonObject { ["text"] = Text ?? "" };
        return "answer: " + answer.ToJsonString(Options);
    }
}

/// <summary>The first result after a Resume (DESIGN.md 10.1): how long after the Resume the step went on, that the window was read again, and how many of its elements changed while it was paused.</summary>
public sealed record PcResumed(long AfterMs, bool Reread, int Changed)
{
    public string Render() => "resumed: " + new JsonObject { ["after_ms"] = AfterMs, ["reread"] = Reread, ["changed"] = Changed }.ToJsonString();
}

/// <summary>What an app in pc_list_apps is doing for Tactus.</summary>
public enum PcAppState
{
    Idle,
    Working,
    Paused,
}

public static class PcPauseText
{
    public static string Wire(PcAppState state) => state switch
    {
        PcAppState.Working => "working",
        PcAppState.Paused => "paused",
        _ => "idle",
    };

    /// <summary>The reason of stale_obs for a point read before a pause: "paused 4 min; observe again".</summary>
    public static string StaleAfterPause(TimeSpan paused) => "paused " + Span(paused) + "; observe again";

    /// <summary>A pause's length in the pill's and the model's words: "12 s", "4 min", "2 h 5 min".</summary>
    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return Math.Max(1, (int)span.TotalSeconds) + " s";
        if (span < TimeSpan.FromHours(1)) return (int)span.TotalMinutes + " min";
        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        return minutes == 0 ? hours + " h" : hours + " h " + minutes + " min";
    }
}
