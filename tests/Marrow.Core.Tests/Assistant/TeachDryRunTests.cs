using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 7.3, 10 step 4; ED-NEW-dextra-as19-5): the dry run of a draft lists the steps in "would" words, marks the ones that
// would ask the owner, and does nothing: it is a walk over the draft with no executor in it.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-5")]
public sealed class TeachDryRunTests
{
    private static TeachDraft Mail() => TeachDraftBuilder.Skeleton(TeachSamples.Info(), TeachSamples.Mail(), []);

    [Fact]
    public void Each_step_is_read_out_in_would_words_and_the_one_that_sends_would_ask_you()
    {
        var steps = TeachDryRun.Walk(Mail());
        Assert.Equal(
        [
            "Would click “New mail” (button) in Outlook",
            "Would type “sam@example.com” into “To” (edit)",
            "Would type “Weekly report” into “Subject” (edit)",
            "Would click “Send” (button) in Outlook",
        ], steps.Select(s => s.Text));
        Assert.Equal([DryVerdict.OnItsOwn, DryVerdict.OnItsOwn, DryVerdict.OnItsOwn, DryVerdict.WouldAskYou], steps.Select(s => s.Verdict));
        Assert.All(steps, s => Assert.False(s.RealRead));
        Assert.Equal([1, 2, 3, 4], steps.Select(s => s.Number));
    }

    [Fact]
    public void A_step_the_owner_does_himself_would_be_refused_and_a_flagged_one_would_ask()
    {
        var events = new List<TeachEvent>
        {
            TeachSamples.Click(1, 1, TeachSamples.Button("Open")),
            TeachSamples.Type(2, 3, TeachSamples.Field("Password", "chrome", "Sign in", password: true), null, 9, secret: true),
        };
        var draft = TeachDraftBuilder.Skeleton(TeachSamples.Info(), events, []).Flag(1, true);
        var steps = TeachDryRun.Walk(draft);
        Assert.Equal([DryVerdict.WouldAskYou, DryVerdict.WouldBeRefused], steps.Select(s => s.Verdict));
    }

    [Fact]
    public void The_report_has_the_banner_the_numbered_steps_and_how_many_would_ask()
    {
        var report = TeachDryRun.Report(Mail());
        var lines = report.Split('\n');
        Assert.Equal(TeachDryRun.Banner, lines[0]);
        Assert.Equal("4. Would click “Send” (button) in Outlook (would ask you)", lines[4]);
        Assert.Equal("1 of 4 steps would ask you.", lines[^1]);
        var plain = TeachDraftBuilder.Skeleton(TeachSamples.Info(), [TeachSamples.Click(1, 1, TeachSamples.Button("Open"))], []);
        Assert.Equal("No step would ask you.", TeachDryRun.Report(plain).Split('\n')[^1]);
    }

    [Fact]
    public void The_walk_stops_at_the_dry_run_step_limit()
    {
        var steps = Enumerable.Range(1, 260).Select(i => new TeachStep(i, "Click " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var draft = new TeachDraft("r1", "many", "Many steps.") { Steps = new ValueList<TeachStep>(steps) };
        Assert.Equal(DryRunLog.StepLimit, TeachDryRun.Walk(draft).Count);
    }

    [Fact]
    public void Walking_the_same_draft_twice_gives_the_same_steps_and_leaves_the_draft_alone()
    {
        var draft = Mail();
        var before = draft;
        Assert.Equal(TeachDryRun.Report(draft), TeachDryRun.Report(draft));
        Assert.Equal(before, draft);
    }
}
