// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "What voice may answer", "Spoken status on request", 12.6 "Voice: Stop everything", 12.6b "Pause
// everything" and "Resume everything"; unit AS-18; ED-NEW-dextra-as18-4). No Swift twin.
// What a spoken request is, when it is one of the few the voice layer answers itself instead of passing to Dextra: stop, pause or
// resume everything, a status question, the digest, and the short answers to a card Dextra has just read out (yes, no, an option
// by its words or its place). A command must be the WHOLE request (a few filler words aside): "stop everything" acts, "I think you
// should stop everything" goes to Dextra, which can call the stop itself. Pure: text in, a command out.
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public enum VoiceCommandKind
{
    /// <summary>Not a command: the words go to Dextra.</summary>
    None,

    /// <summary>"Stop everything": Stop all, at once, no confirmation (12.6).</summary>
    StopEverything,

    /// <summary>"Pause everything": every task pauses at its next turn boundary (12.6b).</summary>
    PauseEverything,

    /// <summary>"Resume everything" (12.6b).</summary>
    ResumeEverything,

    /// <summary>"What's running?", "What needs me?", "Read my digest" (2.4).</summary>
    Status,

    /// <summary>"Yes", "go ahead", "allow it".</summary>
    Yes,

    /// <summary>"No", "don't", "deny".</summary>
    No,

    /// <summary>"The second one", "option three", "the last one".</summary>
    Ordinal,

    /// <summary>"Stop", "quiet", "enough", "never mind": cut the speech and say nothing more (it is not a request).</summary>
    Hush,
}

/// <summary>Which status questions a request asks (it may ask more than one: "what's running and does anything need me").</summary>
[Flags]
public enum VoiceStatusAsk
{
    None = 0,
    Running = 1,
    NeedsYou = 2,
    Digest = 4,
}

public sealed record VoiceCommand(VoiceCommandKind Kind)
{
    public VoiceStatusAsk Ask { get; init; }

    /// <summary>For Ordinal: 1 for the first, and so on; -1 for the last.</summary>
    public int Place { get; init; }

    public static VoiceCommand NotACommand { get; } = new(VoiceCommandKind.None);
}

public static partial class VoiceCommands
{
    /// <summary>A command longer than this many words (after the fillers) is a message to Dextra.</summary>
    public const int MostWords = 12;

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "hey", "ok", "okay", "dextra", "please", "now", "just", "right", "immediately", "quick", "quickly", "thanks", "thank", "you", "kindly", "then", "so",
    };

    private static readonly HashSet<string> Everything = new(StringComparer.Ordinal)
    {
        "everything", "all", "of", "it", "the", "missions", "mission", "tasks", "task", "work", "agents", "that", "running", "jobs",
    };

    private static readonly HashSet<string> YesWords = new(StringComparer.Ordinal)
    {
        "yes", "yeah", "yep", "yup", "ok", "okay", "sure", "go", "ahead", "do", "it", "allow", "approve", "fine", "sounds", "good", "for", "that", "is", "once", "agreed", "correct", "right", "confirm", "accept",
    };

    private static readonly HashSet<string> NoWords = new(StringComparer.Ordinal)
    {
        "no", "nope", "nah", "dont", "do", "not", "deny", "decline", "reject", "never", "mind", "allow", "it", "that", "cancel", "now", "refuse",
    };

    private static readonly HashSet<string> SpareWords = new(StringComparer.Ordinal)
    {
        "the", "one", "option", "number", "choice", "go", "with", "pick", "take", "choose", "i", "will", "want", "let", "us", "use", "answer",
    };

    private static readonly HashSet<string> Hushes = new(StringComparer.Ordinal)
    {
        "stop", "stop talking", "stop speaking", "quiet", "be quiet", "shh", "enough", "that is enough", "never mind", "nevermind", "cancel", "forget it", "be silent", "silence", "hush",
    };

    private static readonly string[] Running =
    [
        "what is running", "what is going on", "what are you working on", "what are you doing", "what is happening", "what is the status",
        "what is in progress", "what is working", "status", "give me a status", "any updates", "how are the missions", "what are the missions",
        "what is on", "whats running",
    ];

    private static readonly string[] Needs =
    [
        "what needs me", "what needs my attention", "what is waiting for me", "what is waiting on me", "does anything need me", "anything need me",
        "do you need me", "do you need anything from me", "what do you need from me", "what needs a decision", "what is waiting", "am i needed",
        "what needs my decision", "what needs an answer", "what needs my answer",
    ];

    private static readonly string[] Digest =
    [
        "read my digest", "read the digest", "read me my digest", "read me the digest", "what is in my digest", "my digest", "the digest", "digest",
    ];

    private static readonly (string Word, int Place)[] Places =
    [
        ("first", 1), ("one", 1), ("1", 1), ("second", 2), ("two", 2), ("2", 2), ("third", 3), ("three", 3), ("3", 3), ("fourth", 4), ("four", 4), ("4", 4),
        ("last", -1),
    ];

    [GeneratedRegex(@"[^\p{L}\p{N}' ]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotWords();

    /// <summary>The words of a request: lower case, punctuation gone, "what's" as "what is", apostrophes gone ("don't" as "dont").</summary>
    public static IReadOnlyList<string> Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var flat = text.ToLowerInvariant().Replace('’', '\'').Replace("what's", "what is", StringComparison.Ordinal)
            .Replace("that's", "that is", StringComparison.Ordinal).Replace("what're", "what are", StringComparison.Ordinal);
        flat = NotWords().Replace(flat, " ").Replace("'", "", StringComparison.Ordinal);
        return flat.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The command <paramref name="text"/> is, or <see cref="VoiceCommand.NotACommand"/>.</summary>
    public static VoiceCommand Parse(string? text)
    {
        var all = Normalise(text);
        if (all.Count == 0 || all.Count > MostWords + 4) return VoiceCommand.NotACommand;
        var words = all.Where(w => !Fillers.Contains(w)).ToList();
        // "ok" and "okay" are fillers around a command and a yes on their own; "thank you" alone is nothing to act on.
        if (words.Count == 0 && all.Any(w => w is "ok" or "okay") && !all.Any(w => w is "dextra")) return new VoiceCommand(VoiceCommandKind.Yes);
        if (words.Count == 0 || words.Count > MostWords) return VoiceCommand.NotACommand;

        if (IsAll(words, "stop", "halt", "kill") || words.SequenceEqual(["emergency", "stop"])) return new VoiceCommand(VoiceCommandKind.StopEverything);
        if (IsAll(words, "pause", "hold")) return new VoiceCommand(VoiceCommandKind.PauseEverything);
        if (IsAll(words, "resume", "continue", "unpause")) return new VoiceCommand(VoiceCommandKind.ResumeEverything);
        if (Hushes.Contains(string.Join(' ', words))) return new VoiceCommand(VoiceCommandKind.Hush);

        var flat = string.Join(' ', all);
        var ask = VoiceStatusAsk.None;
        if (Digest.Any(p => Has(flat, p))) ask |= VoiceStatusAsk.Digest;
        if (Needs.Any(p => Has(flat, p))) ask |= VoiceStatusAsk.NeedsYou;
        if (Running.Any(p => Has(flat, p))) ask |= VoiceStatusAsk.Running;
        if (ask != VoiceStatusAsk.None) return new VoiceCommand(VoiceCommandKind.Status) { Ask = ask };

        if (words.All(w => NoWords.Contains(w)) && words.Any(w => w is "no" or "nope" or "nah" or "dont" or "not" or "deny" or "decline" or "reject" or "never" or "cancel" or "refuse"))
            return new VoiceCommand(VoiceCommandKind.No);
        if (words.All(w => YesWords.Contains(w)) && words.Any(w => w is "yes" or "yeah" or "yep" or "yup" or "ok" or "okay" or "sure" or "ahead" or "allow" or "approve" or "fine" or "good" or "agreed" or "correct" or "confirm" or "accept" or "do"))
            return new VoiceCommand(VoiceCommandKind.Yes);
        if (PlaceOf(words) is { } place) return new VoiceCommand(VoiceCommandKind.Ordinal) { Place = place };
        return VoiceCommand.NotACommand;
    }

    // True when the words are one of the verbs and nothing but "everything"-words: "stop all", "stop all of it", "stop all the missions".
    private static bool IsAll(List<string> words, params string[] verbs)
    {
        var at = words.FindIndex(w => verbs.Contains(w));
        if (at < 0) return false;
        var rest = words.Where((_, i) => i != at).ToList();
        return rest.Count > 0 && rest.All(Everything.Contains) && rest.Any(w => w is "everything" or "all");
    }

    private static bool Has(string flat, string phrase) =>
        flat == phrase || flat.StartsWith(phrase + " ", StringComparison.Ordinal) || flat.EndsWith(" " + phrase, StringComparison.Ordinal)
        || flat.Contains(" " + phrase + " ", StringComparison.Ordinal);

    // "the second one", "second", "option two", "number 3", "the last one": the place, when the words are only that.
    private static int? PlaceOf(List<string> words)
    {
        var named = words.Where(w => w != "one" && Places.Any(p => p.Word == w)).Distinct().ToList();
        if (named.Count == 1 && words.All(w => SpareWords.Contains(w) || w == named[0])) return Places.First(p => p.Word == named[0]).Place;
        if (named.Count == 0 && words.Contains("one") && words.All(SpareWords.Contains) && (words.Count == 1 || words.Any(w => w is "option" or "number" or "choice"))) return 1;
        return null;
    }

    /// <summary>The words of <paramref name="text"/> joined by single spaces, for comparing what was said with a card's own options.</summary>
    public static string Flat(string? text) => string.Join(' ', Normalise(text));
}
