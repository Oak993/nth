// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Spoken status on request ... answered in two or three sentences. It never speaks unprompted"; 16 "done
// when" for AS-18: status answers stay within 3 sentences; ED-NEW-dextra-as18-6). No Swift twin.
// What Dextra says when the owner asks "What's running?", "What needs me?" or "Read my digest". It is built from the same snapshot the Board
// shows (the missions and the waiting cards), with no model and no network, so it is instant and costs nothing. Every answer is clamped to three
// sentences, whatever the lists hold; a title is made safe to put in a sentence (no marks of its own), so it never adds one.
using Marrow.App.Assistant.Views;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>A spoken status answer, and the card it ended on (the one a "yes" or "the second one" would answer next), if any.</summary>
public sealed record VoiceStatusAnswer(string Text, DecisionItem? Focus);

public static class VoiceStatus
{
    /// <summary>How many titles one sentence names before it says "and N more".</summary>
    public const int NamedTitles = 3;

    public static VoiceStatusAnswer Compose(VoiceStatusAsk ask, IReadOnlyList<MissionJob> jobs, IReadOnlyList<DecisionItem> waiting, Func<string?>? digest = null)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(waiting);
        var sentences = new List<string>();
        DecisionItem? focus = null;
        if (ask.HasFlag(VoiceStatusAsk.Digest))
        {
            var text = SpokenText.First(SpokenText.Clean(digest?.Invoke()), SpokenText.StatusSentences, out _);
            sentences.AddRange(SpokenText.Sentences(text.Length > 0 ? text : "There is no digest yet."));
            return new VoiceStatusAnswer(Clamp(sentences), null);
        }
        var cards = waiting.Where(c => c.State == DecisionState.Open).ToList();
        if (ask.HasFlag(VoiceStatusAsk.Running))
        {
            sentences.AddRange(RunningSentences(jobs));
        }
        if (ask.HasFlag(VoiceStatusAsk.NeedsYou))
        {
            if (cards.Count == 0)
            {
                sentences.Add("Nothing needs you.");
            }
            else
            {
                sentences.Add(cards.Count == 1 ? "One thing needs you." : $"{cards.Count} things need you.");
                focus = cards[0];
                sentences.Add(VoiceCardRules.Offer(focus));
            }
        }
        return new VoiceStatusAnswer(Clamp(sentences), focus);
    }

    private static IEnumerable<string> RunningSentences(IReadOnlyList<MissionJob> jobs)
    {
        var live = jobs.Where(j => j.Group != BoardGroup.DoneToday).ToList();
        if (live.Count == 0)
        {
            yield return "Nothing is running.";
            yield break;
        }
        var running = live.Where(j => j.Runs).ToList();
        var held = live.Count(j => j.State is MissionState.Paused or MissionState.Pausing);
        yield return running.Count switch
        {
            0 => held > 0 ? $"{Count(held, "mission")} {(held == 1 ? "is" : "are")} paused." : $"{Count(live.Count, "mission")} {(live.Count == 1 ? "is" : "are")} waiting.",
            1 => "One mission is running.",
            _ => $"{running.Count} missions are running.",
        };
        var named = (running.Count > 0 ? running : live).Select(j => SpokenText.Phrase(j.Title, 60)).Where(t => t.Length > 0).ToList();
        if (named.Count == 0) yield break;
        var shown = named.Take(NamedTitles).ToList();
        var more = named.Count - shown.Count;
        var list = shown.Count == 1 ? shown[0] : string.Join(", ", shown.Take(shown.Count - 1)) + (more > 0 ? ", " + shown[^1] : " and " + shown[^1]);
        yield return more > 0 ? $"That is {list} and {more} more." : $"That is {list}.";
    }

    private static string Count(int n, string noun) => n == 1 ? $"One {noun}" : $"{n} {noun}s";

    // Three sentences at most, whatever the parts added up to.
    private static string Clamp(List<string> sentences) =>
        string.Join(' ', sentences.Select(s => s.Trim()).Where(s => s.Length > 0).Take(SpokenText.StatusSentences));
}
