using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 5.1, 5.4, 4.3 "listed in the digest"; the canvas's feed line "Answered for you in “Pitch deck v3”: keep the existing chart style";
// ED-NEW-dextra-as22-8): an answer given for the owner is one line of the feed, under the privacy rules: the chat's title leads, the answer follows only where the
// filter lets its words through, a secret is masked, and a chat the owner hid shows nothing at all.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-8")]
public sealed class DelegateFeedTapTests
{
    private const string Title = "Pitch deck v3";

    private static ThreadRuntimeTurnRig Rig(bool hidden = false, ProviderKind provider = ProviderKind.Claude) =>
        new(provider, thread => thread with { Title = Title, HasCustomTitle = true, HiddenFromAssistant = hidden },
            settings: app =>
            {
                app.Settings.ChimeWhenFinished = false;
                app.Settings.NotifyWhenFinished = false;
            });

    private static async Task<IReadOnlyList<FeedEvent>> Flushed(ThreadRuntimeTurnRig rig, FeedCollector feed, int expected)
    {
        await rig.App.AssistantEvents.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return await feed.UntilAsync(lines => lines.Count >= expected);
    }

    [Fact]
    public async Task An_answered_question_is_one_info_line_with_the_chat_and_the_answer()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);
        rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapDelegateAnswered(rig.InitialThread.Id, "keep the existing chart style", approval: false));
        var line = Assert.Single(await Flushed(rig, feed, 1));
        Assert.Equal((FeedEventKind.DecisionAnswered, FeedSeverity.Info, rig.InitialThread.Id), (line.Kind, line.Severity, line.ThreadId));
        Assert.Equal($"{Title}: a question was answered for you. keep the existing chart style", line.Summary);
    }

    [Fact]
    public async Task An_allowed_approval_says_so_in_its_own_words()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);
        rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapDelegateAnswered(rig.InitialThread.Id, "allowed git status", approval: true));
        var line = Assert.Single(await Flushed(rig, feed, 1));
        Assert.Equal($"{Title}: an approval was answered for you. allowed git status", line.Summary);
    }

    [Fact]
    public async Task A_secret_in_the_answer_is_masked_and_a_brain_that_may_not_read_the_chat_gets_the_line_without_its_words()
    {
        using var rig = Rig();
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);
        rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapDelegateAnswered(rig.InitialThread.Id, "use sk-SENTINELabcdefghijklmnop123456", approval: false));
        var masked = Assert.Single(await Flushed(rig, feed, 1));
        Assert.DoesNotContain("SENTINEL", masked.Summary, StringComparison.Ordinal);

        using var other = Rig();
        other.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Codex);               // another provider, and the owner has not allowed reading across
        using var otherFeed = new FeedCollector(other.App.AssistantEvents);
        other.H.Main.OnMain(() => other.App.AssistantTaps.TapDelegateAnswered(other.InitialThread.Id, "keep the existing chart style", approval: false));
        var bare = Assert.Single(await Flushed(other, otherFeed, 1));
        Assert.Equal($"{Title}: a question was answered for you", bare.Summary);
    }

    [Fact]
    public async Task A_chat_the_owner_hid_shows_nothing()
    {
        using var rig = Rig(hidden: true);
        rig.App.AssistantEvents.Brain = new FeedBrain(ProviderKind.Claude);
        using var feed = new FeedCollector(rig.App.AssistantEvents);
        rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapDelegateAnswered(rig.InitialThread.Id, "keep the existing chart style", approval: false));
        await rig.App.AssistantEvents.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(feed.Lines);
    }
}
