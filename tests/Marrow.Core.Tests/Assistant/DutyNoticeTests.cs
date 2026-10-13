using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Duty opt-in", 7.1 "a report style ... Notify me"; ED-NEW-dextra-as9-9): what a "Notify me" duty tells the owner. Only
// a real run that finished and did not end with the nothing-changed line tells him, and only a duty set to Notify me. The toast is the duty's name over
// the first line of its report, masked and cut to one line; runs that end together are one summary. A duty's report is not a decision, so the notice has no
// button that answers anything and the base channel says it cannot show one.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class DutyNoticeTests
{
    private static Duty Watch(ReportStyle report = ReportStyle.NotifyMe, string name = "Espresso price") =>
        new("d1", name, "dextra", new PageWatchTrigger("https://shop.example/espresso", 60), "Tell me the price.") { Report = report };

    private static DutyFinding Finding(string duty = "Espresso price", string? report = "The price fell to $640.") => new(Guid.NewGuid(), duty, report);

    [Fact]
    public void Only_a_notify_me_duty_whose_real_run_finished_with_something_to_report_tells_the_owner()
    {
        Assert.True(DutyNotices.Notifies(Watch(), DutyRunOutcome.Done, "The price fell to $640.", dryRun: false));
        foreach (var style in new[] { ReportStyle.SilentUnlessChanged, ReportStyle.Always, ReportStyle.DigestOnly })
            Assert.False(DutyNotices.Notifies(Watch(style), DutyRunOutcome.Done, "The price fell to $640.", dryRun: false));
    }

    [Theory]
    [InlineData(DutyRunOutcome.Started)]
    [InlineData(DutyRunOutcome.NothingChanged)]
    [InlineData(DutyRunOutcome.Failed)]
    [InlineData(DutyRunOutcome.Skipped)]
    [InlineData(DutyRunOutcome.Stopped)]
    [InlineData(DutyRunOutcome.Refused)]
    public void A_run_that_did_not_finish_with_something_is_the_boards_to_say_not_a_toast(DutyRunOutcome outcome) =>
        Assert.False(DutyNotices.Notifies(Watch(), outcome, "The price fell to $640.", dryRun: false));

    [Fact]
    public void A_report_that_ends_with_the_nothing_changed_line_and_a_dry_run_make_no_toast()
    {
        Assert.False(DutyNotices.Notifies(Watch(), DutyRunOutcome.Done, "Looked at the page.\nRESULT: nothing_changed\n", dryRun: false));
        Assert.False(DutyNotices.Notifies(Watch(), DutyRunOutcome.Done, "The price fell to $640.", dryRun: true));
    }

    [Fact]
    public void A_run_with_no_report_at_all_still_tells_the_owner_that_it_found_something()
    {
        Assert.True(DutyNotices.Notifies(Watch(), DutyRunOutcome.Done, null, dryRun: false));
        var notice = DutyNotices.For(Finding(report: null));
        Assert.Equal("Has something for you.", notice.Body);
    }

    [Fact]
    public void The_toast_is_the_duty_name_over_the_first_line_of_the_report_and_opens_that_mission()
    {
        var finding = Finding(report: "The price fell to $640.\nIt was $720 last week.");
        var notice = DutyNotices.For(finding);
        Assert.Equal("Espresso price", notice.Title);
        Assert.Equal("The price fell to $640.", notice.Body);
        Assert.Equal(finding.Mission, notice.Target);
        Assert.Equal([finding.Mission], notice.Missions);
        Assert.False(notice.IsSummary);
    }

    [Fact]
    public void Blank_lines_a_heading_a_bullet_a_quote_and_the_structured_result_line_are_not_the_message()
    {
        Assert.Equal("The price fell to $640.", DutyNotices.For(Finding(report: "\n\n  ## The price fell to $640.\nmore")).Body);
        Assert.Equal("The price fell to $640.", DutyNotices.For(Finding(report: "- The price fell to $640.")).Body);
        Assert.Equal("The price fell to $640.", DutyNotices.For(Finding(report: "> * The price fell to $640.")).Body);
        Assert.Equal("The price fell to $640.", DutyNotices.For(Finding(report: "RESULT: updated\nThe price fell to $640.")).Body);
        Assert.Equal("-5% on the espresso machine", DutyNotices.For(Finding(report: "-5% on the espresso machine")).Body);   // a minus is not a bullet
        Assert.Equal("#1 seller", DutyNotices.For(Finding(report: "#1 seller")).Body);                                         // a hash with no space is not a heading
    }

    [Fact]
    public void A_secret_in_the_report_or_the_name_is_masked_and_only_one_line_of_the_report_is_shown()
    {
        const string key = "sk-abcdefghijklmnopqrstuvwxyz0123456789";
        var notice = DutyNotices.For(Finding(duty: "Check " + key, report: $"Found Bearer {key} in the page.\nsecond line with token=hunter2hunter2"));
        Assert.DoesNotContain(key, notice.Title + notice.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", notice.Title + notice.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("second line", notice.Body, StringComparison.Ordinal);
        Assert.Contains("[secret]", notice.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_name_and_a_long_line_are_cut_on_one_line_with_an_ellipsis()
    {
        var notice = DutyNotices.For(Finding(duty: new string('N', 300), report: "Found\ttext " + new string('x', 400)));
        Assert.DoesNotContain('\t', notice.Body);
        Assert.EndsWith("…", notice.Body, StringComparison.Ordinal);
        Assert.True(notice.Body.Length <= DecisionNotices.BodyChars + 1);
        Assert.EndsWith("…", notice.Title, StringComparison.Ordinal);
        Assert.True(notice.Title.Length <= DutyNotices.NameChars + 1);
    }

    [Fact]
    public void A_blank_name_reads_dextra()
    {
        Assert.Equal("Dextra", DutyNotices.For(Finding(duty: "  ")).Title);
        Assert.Equal("Dextra", DutyNotices.For(Finding(duty: "")).Title);
    }

    [Fact]
    public void Runs_that_ended_together_are_one_summary_that_lists_the_duties_three_at_most()
    {
        Assert.Null(DutyNotices.ForMany([]));
        var one = Finding("Espresso price");
        Assert.Equal(DutyNotices.For(one), DutyNotices.ForMany([one]));

        var three = new[] { Finding("Espresso price"), Finding("Inbox"), Finding("Backup") };
        var summary = DutyNotices.ForMany(three)!;
        Assert.Equal("3 duties have news", summary.Title);
        Assert.Equal("Espresso price, Inbox and Backup have something for you.", summary.Body);
        Assert.True(summary.IsSummary);
        Assert.Equal(three[0].Mission, summary.Target);
        Assert.Equal(three.Select(f => f.Mission), summary.Missions);

        var many = new[] { Finding("A"), Finding("B"), Finding("C"), Finding("D"), Finding("E") };
        Assert.Equal("A, B, C and 2 more have something for you.", DutyNotices.ForMany(many)!.Body);
    }

    [Fact]
    public void The_same_duty_twice_in_a_window_is_still_one_name()
    {
        var summary = DutyNotices.ForMany([Finding("Espresso price"), Finding("Espresso price")])!;
        Assert.Equal("2 duties have news", summary.Title);
        Assert.Equal("Espresso price has something for you.", summary.Body);
        Assert.Equal(2, summary.Missions.Count);
    }

    [Fact]
    public async Task A_channel_that_does_not_know_a_duty_toast_says_it_took_nothing()
    {
        var channel = new BareNotificationChannel();
        Assert.False(await channel.Send(DutyNotices.For(Finding())));
    }
}
