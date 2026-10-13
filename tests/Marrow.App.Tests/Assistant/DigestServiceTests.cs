using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using static Marrow.App.Tests.Fixtures.DigestRigs;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.1, 6; ED-NEW-dextra-as11-1, -3, -9): the digest as the app composes it, over a real controller, a real scheduler and a real store.
// It reads nth's own state (the decision queue, the mission runner's records, the duties' histories), never a chat: what needs the owner and what finished (with
// the reviewer's verdict), what runs, what the standing duties found and only that, and the seal of the last day's activity log. It tells what happened since the last
// morning digest. It sends no toast and raises no card of its own.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-9")]
public sealed class DigestServiceTests
{
    [Fact]
    public async Task What_needs_the_owner_counts_the_open_cards_and_a_blocked_task_each_linked_to_its_source()
    {
        using var rig = Quiet();
        var card = Card(rig, "Which chart style?");
        var blocked = Blocked(rig, "Shop login fix");
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        var needs = digest.Sections[0];
        Assert.Equal(DigestPart.NeedsYou, needs.Part);
        Assert.Equal("2.", needs.Lead);
        Assert.Equal(["Which chart style?", "“Shop login fix” is blocked"], needs.Items.Select(i => i.Text));
        Assert.Equal([new DigestLink(DigestLinkKind.Decision, card.Id.ToString("D")), new DigestLink(DigestLinkKind.Mission, blocked.Id.ToString("D"))], needs.Items.Select(i => i.Link));
    }

    [Fact]
    public async Task What_finished_since_the_last_morning_digest_comes_with_the_reviewers_verdict_and_a_duty_run_is_told_in_its_own_section()
    {
        using var rig = Quiet();
        var done = Finished(rig, "Dependency audit");
        rig.Save(DutyHostRig.Daily("d1", 7, "Compare the views."));
        rig.Clock.Advance(TimeSpan.FromHours(2));
        rig.Complete(rig.MissionOf(rig.Host.Scheduler.Find("d1")!), "Views are up 8%.");
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        var finished = digest.Sections.Single(s => s.Part == DigestPart.Finished);
        Assert.Equal("Finished: “Dependency audit” (reviewed: passed)", finished.Text);
        Assert.Equal(new DigestLink(DigestLinkKind.Mission, done.Id.ToString("D")), finished.Items[0].Link);
        // The duty's run is not an owner mission: it is in the duties section, with what it reported.
        Assert.Equal("Duties: Duty d1: Views are up 8%.", digest.Sections.Single(s => s.Part == DigestPart.Duties).Text);
    }

    [Fact]
    public async Task What_runs_is_every_live_task_by_the_boards_own_word()
    {
        using var rig = Quiet();
        var running = Started(rig, "Channel A's next video");
        MissionRig.Until(() => rig.Now(running).Mission.State is MissionState.Working or MissionState.Planning, "the mission to run");
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        var section = digest.Sections.Single(s => s.Part == DigestPart.Running);
        Assert.StartsWith("Running: “Channel A's next video” (", section.Text, StringComparison.Ordinal);
        Assert.Contains(MissionStates.Label(rig.Now(running).Mission.State).ToLowerInvariant(), section.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_duty_that_is_silent_unless_it_changed_and_changed_nothing_is_absent_and_a_loud_one_is_present()
    {
        using var rig = Quiet();
        var quiet = rig.Save(DutyHostRig.Daily("quiet", 7));
        var loud = rig.Save(DutyHostRig.Daily("loud", 7));
        rig.Clock.Advance(TimeSpan.FromHours(2));
        rig.Complete(rig.MissionOf(quiet), "Looked at the page.\nRESULT: nothing_changed");
        rig.Complete(rig.MissionOf(loud), "The price fell to $640.\nMore words below.");
        MissionRig.Until(() => rig.Host.Scheduler.Find("quiet")!.History[0].Outcome == DutyRunOutcome.NothingChanged, "the quiet run to end");
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        Assert.Equal("Duties: Duty loud: The price fell to $640.", digest.Sections.Single(s => s.Part == DigestPart.Duties).Text);
        Assert.DoesNotContain("Duty quiet", digest.Sections.Single(s => s.Part == DigestPart.Duties).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_that_failed_is_unusual_and_a_duty_with_a_missing_project_says_it_could_not_start()
    {
        using var rig = Quiet();
        var failing = rig.Save(DutyHostRig.Daily("failing", 7));
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var lead = rig.LeadOf(rig.MissionOf(failing));
        rig.Controller.OnTurnFinished(lead, TurnStatus.Failed, false);
        MissionRig.Until(() => rig.Host.Scheduler.Find("failing")!.History[0].Outcome == DutyRunOutcome.Failed, "the run to fail");
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        var unusual = digest.Sections.Single(s => s.Part == DigestPart.Unusual);
        Assert.Equal(["“Duty failing” failed at 07:00."], unusual.Items.Select(i => i.Text));
    }

    [Fact]
    public async Task A_digest_tells_what_happened_since_the_last_morning_digest_and_one_the_owner_asked_for_does_not_move_that()
    {
        using var rig = Quiet();
        Finished(rig, "Before the digest");
        var first = (await rig.Host.Digests!.Compose(DigestKind.Scheduled))!;
        Assert.Equal(rig.Clock.GetUtcNow() - DigestService.FirstLookback, first.Since);
        Assert.Contains("Before the digest", first.Sections.Single(s => s.Part == DigestPart.Finished).Text, StringComparison.Ordinal);
        rig.Clock.Advance(TimeSpan.FromHours(3));
        Finished(rig, "After the digest");
        var asked = (await rig.Host.Digests.Compose(DigestKind.Asked))!;
        Assert.Equal(first.At, asked.Since);
        var told = asked.Sections.Single(s => s.Part == DigestPart.Finished).Text;
        Assert.DoesNotContain("Before the digest", told, StringComparison.Ordinal);
        Assert.Contains("After the digest", told, StringComparison.Ordinal);
        rig.Clock.Advance(TimeSpan.FromHours(1));
        var next = (await rig.Host.Digests.Compose(DigestKind.Scheduled))!;
        Assert.Equal(first.At, next.Since);
    }

    [Fact]
    public async Task A_digest_never_reaches_back_further_than_three_days()
    {
        using var rig = Quiet();
        await rig.Host.Digests!.Compose(DigestKind.Scheduled);
        rig.Clock.Advance(TimeSpan.FromDays(9));
        var late = (await rig.Host.Digests.Compose(DigestKind.Scheduled))!;
        Assert.Equal(rig.Clock.GetUtcNow() - DigestService.MaxLookback, late.Since);
    }

    [Fact]
    public async Task The_digest_prints_the_last_closed_days_activity_log_seal_and_the_book_remembers_it_for_the_logs_check()
    {
        var asked = new List<DateOnly>();
        using var rig = Quiet(Setup(dayFinal: day =>
        {
            lock (asked) asked.Add(day);
            return new string('c', 64);
        }));
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        Assert.Equal([new DateOnly(2026, 10, 12)], asked);
        Assert.Equal(new DigestSeal(new DateOnly(2026, 10, 12), new string('c', 64)), digest.Seal);
        Assert.Equal("Activity log 12 Oct: seal cccc cccc cccc cccc", digest.SealLine);
        Assert.Equal(new string('c', 64), rig.Host.Digests.Book.PrintedDayFinals()![new DateOnly(2026, 10, 12)]);
    }

    [Fact]
    public async Task A_day_with_no_activity_has_no_seal_and_a_log_that_cannot_be_read_does_not_stop_the_digest()
    {
        using var empty = Quiet(Setup(dayFinal: _ => null));
        Assert.Null((await empty.Host.Digests!.Compose(DigestKind.Asked))!.Seal);
        using var broken = Quiet(Setup(dayFinal: _ => throw new IOException("the file is locked")));
        var digest = (await broken.Host.Digests!.Compose(DigestKind.Asked))!;
        Assert.Null(digest.Seal);
        Assert.Contains(broken.Faults, f => f.Contains("could not be read for the digest's seal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_notes_are_folded_with_the_morning_digest_and_not_with_one_the_owner_asked_for_or_a_dry_run()
    {
        var folds = 0;
        using var rig = Quiet(Setup(fold: () => Interlocked.Increment(ref folds)));
        await rig.Host.Digests!.Compose(DigestKind.Asked);
        await rig.Host.Digests.Compose(DigestKind.Scheduled, preview: true);
        Assert.Equal(0, Volatile.Read(ref folds));
        await rig.Host.Digests.Compose(DigestKind.Scheduled);
        MissionRig.Until(() => Volatile.Read(ref folds) == 1, "the notes to be folded");
    }

    [Fact]
    public async Task A_dry_run_composes_the_digest_and_keeps_nothing()
    {
        using var rig = Quiet(Setup(dayFinal: _ => new string('d', 64), habits: () => [ThreeFridays()], projects: () => [new SpotProject(AppProject, "App", DateTimeOffset.MinValue)]));
        var preview = (await rig.Host.Digests!.Compose(DigestKind.Scheduled, preview: true))!;
        Assert.Same(preview, rig.Host.Digests.LastPreview);
        Assert.Null(rig.Host.Digests.Latest);
        Assert.Empty(rig.Host.Digests.Book.Ledger.Today);
        Assert.Null(rig.Host.Digests.Book.PrintedDayFinals());
        Assert.Single(preview.Suggestions);                          // it shows what it would offer, without using one of the day's three places
    }

    private static readonly Guid AppProject = MissionRig.Project;

    private static ChatHabit ThreeFridays()
    {
        DateTimeOffset Friday(int weeksAgo) => new DateTimeOffset(2026, 10, 9, 16, 5, 0, TimeSpan.Zero).AddDays(-7 * weeksAgo);
        return new ChatHabit("app:reprice", "Re-price listings", MissionRig.Project, "App", [Friday(2), Friday(1), Friday(0)]);
    }

    [Fact]
    public async Task Two_digests_asked_at_once_are_composed_one_after_the_other_and_use_the_days_three_places_once()
    {
        using var rig = Quiet(Setup(habits: () => [ThreeFridays()]));
        var both = await Task.WhenAll(rig.Host.Digests!.Compose(DigestKind.Asked), rig.Host.Digests.Compose(DigestKind.Asked));
        Assert.All(both, d => Assert.NotNull(d));
        Assert.Equal(2, rig.Host.Digests.Book.Digests.Count);
        Assert.Single(rig.Host.Digests.Book.Ledger.Today);
        Assert.Single(both[0]!.Suggestions);
        Assert.Single(both[1]!.Suggestions);
    }

    [Fact]
    public async Task A_compose_the_owner_cancels_keeps_nothing()
    {
        using var rig = Quiet();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Host.Digests!.Compose(DigestKind.Asked, cancellationToken: cancel.Token));
        Assert.Null(rig.Host.Digests!.Latest);
    }

    [Fact]
    public async Task A_digest_composed_after_the_service_is_let_go_is_nothing()
    {
        using var rig = Quiet();
        var service = rig.Host.Digests!;
        service.Dispose();
        Assert.Null(await service.Compose(DigestKind.Asked));
    }

    [Fact]
    public async Task The_digest_asks_nothing_of_the_owner_and_reads_no_chat()
    {
        using var rig = Quiet(Setup(habits: () => [ThreeFridays()]));
        Finished(rig, "Dependency audit");
        var cardsBefore = rig.Controller.Queue.Items.Count;
        var sentBefore = rig.Agents.Chats.Sum(c => c.Sent.Count);
        await rig.Host.Digests!.Compose(DigestKind.Scheduled);
        await rig.Host.Digests.Ask();
        Assert.Equal(cardsBefore, rig.Controller.Queue.Items.Count);       // no decision card, so no toast (13.1: toasts are for decisions)
        Assert.Equal(sentBefore, rig.Agents.Chats.Sum(c => c.Sent.Count));   // no chat was written to
    }

    [Fact]
    public async Task The_expired_sign_ins_the_setup_names_are_unusual()
    {
        using var rig = Quiet(Setup(logins: () => ["Channel B"]));
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        Assert.Equal(["Your sign-in for Channel B expired."], digest.Sections.Single(s => s.Part == DigestPart.Unusual).Items.Select(i => i.Text));
    }

    [Fact]
    public async Task What_the_delegate_answered_and_each_Asturs_paragraph_come_from_the_setup()
    {
        var taint = Taint.From(ContentOrigin.Email, "an invoice mail");
        using var rig = Quiet(Setup(
            answered: () => [new DigestAnswered("Pitch deck v3", 2, new DigestLink(DigestLinkKind.Thread, "t1"))],
            asturs: () => [new DigestAstur("Finance", "An invoice came in.") { Taint = taint }]));
        var digest = (await rig.Host.Digests!.Compose(DigestKind.Asked))!;
        Assert.Contains(digest.Sections, s => s.Part == DigestPart.Answered && s.Items[0].Link.Id == "t1");
        Assert.Contains(digest.Sections, s => s.Part == DigestPart.Asturs && s.Text == "From your Asturs: Finance: An invoice came in.");
        Assert.Equal(taint, digest.Taint);
    }

    [Fact]
    public async Task A_digest_that_carries_taint_taints_the_home_chat_it_is_pinned_in_and_a_clean_one_does_not()
    {
        var taint = Taint.From(ContentOrigin.Web, "https://shop.example/espresso");
        using var rig = Quiet(Setup(asturs: () => [new DigestAstur("Store", "A page said something.") { Taint = taint }]));
        var home = Guid.NewGuid();
        await rig.Host.Digests!.Compose(DigestKind.Asked);
        rig.Host.Digests.PinnedIn(home);
        Assert.True(rig.Controller.HomeTaint(home).IsTainted);
        var clean = Guid.NewGuid();
        using var other = Quiet();
        await other.Host.Digests!.Compose(DigestKind.Asked);
        other.Host.Digests.PinnedIn(clean);
        Assert.False(other.Controller.HomeTaint(clean).IsTainted);
    }

    [Fact]
    public async Task The_service_says_when_the_digest_changed()
    {
        using var rig = Quiet();
        var changes = 0;
        rig.Host.Digests!.Changed += () => changes++;
        await rig.Host.Digests.Compose(DigestKind.Asked);
        Assert.True(changes >= 1);
        Assert.NotNull(rig.Host.Digests.Latest);
    }
}
