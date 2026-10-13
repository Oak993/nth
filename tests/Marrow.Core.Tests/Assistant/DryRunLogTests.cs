using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.3 "Dry run"; canvas 5; ED-NEW-dextra-as10-8): a dry run does the reads for real and nothing
// else. Every other action the agent tries is refused to it and listed as a "would" step, marked "on its own" when the real
// run would do it alone and "would ask you" when it would stop for the owner. The banner says what is real.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-8")]
public sealed class DryRunLogTests
{
    [Fact]
    public void A_read_is_performed_and_listed_as_a_real_read()
    {
        var log = new DryRunLog();
        var outcome = log.Intercept(ActionClass.ReadWeb, Verdict.Allow, "Would fetch the page", "Fetch https://shop.example/a");
        Assert.True(outcome.Perform);
        var step = Assert.Single(log.Steps);
        Assert.True(step.RealRead);
        Assert.Equal("Fetch https://shop.example/a (a real read)", step.Text);
        Assert.Equal(DryVerdict.OnItsOwn, step.Verdict);
    }

    [Theory]
    [InlineData(ActionClass.Work, Verdict.Allow, DryVerdict.OnItsOwn)]
    [InlineData(ActionClass.Outward, Verdict.Ask, DryVerdict.WouldAskYou)]
    [InlineData(ActionClass.Money, Verdict.Ask, DryVerdict.WouldAskYou)]
    [InlineData(ActionClass.HardDeny, Verdict.Deny, DryVerdict.WouldBeRefused)]
    public void Anything_else_is_refused_to_the_agent_and_listed_with_what_the_real_run_would_do(ActionClass actionClass, Verdict verdict, DryVerdict listed)
    {
        var log = new DryRunLog();
        var outcome = log.Intercept(actionClass, verdict, "Would do the thing", "Do the thing");
        Assert.False(outcome.Perform);
        Assert.Equal(DryRunWords.Refused, outcome.Answer);
        var step = Assert.Single(log.Steps);
        Assert.False(step.RealRead);
        Assert.Equal("Would do the thing", step.Text);
        Assert.Equal(listed, step.Verdict);
    }

    [Fact]
    public void A_read_the_policy_refuses_is_not_done_either()
    {
        var log = new DryRunLog();
        var outcome = log.Intercept(ActionClass.ReadLocal, Verdict.Deny, "Would read the passwords file", "Read the passwords file");
        Assert.False(outcome.Perform);
        Assert.Equal(DryVerdict.WouldBeRefused, Assert.Single(log.Steps).Verdict);
    }

    [Fact]
    public void The_report_has_the_banner_and_numbered_steps_and_says_when_nothing_was_tried()
    {
        var log = new DryRunLog();
        Assert.Equal(DryRunWords.Banner + "\nIt tried no action.", log.Report());
        log.Intercept(ActionClass.ReadLocal, Verdict.Allow, "x", "List new files");
        log.Intercept(ActionClass.Outward, Verdict.Ask, DryRunWords.Would(ApprovalRequest.Kind.Command, "mail.exe --to accountant"), "x");
        Assert.Equal(
            "Dry run: reads are real; nothing is written, sent or clicked\n1. List new files (a real read) (on its own)\n2. Would run: mail.exe --to accountant (would ask you)",
            log.Report());
    }

    [Fact]
    public void The_words_for_a_provider_approval_and_a_tool_call()
    {
        Assert.Equal("Would apply changes: src/a.cs", DryRunWords.Would(ApprovalRequest.Kind.FileChange, " src/a.cs "));
        Assert.Equal("Would use: WebFetch", DryRunWords.Would(ApprovalRequest.Kind.Tool, "WebFetch"));
        Assert.Equal("Would use nth-dextra/start_mission: Fix the login", DryRunWords.WouldCall("nth-dextra/start_mission", "Fix the login"));
        Assert.Equal("Would use nth-dextra/start_mission", DryRunWords.WouldCall("nth-dextra/start_mission", null));
    }

    [Fact]
    public void A_runaway_dry_run_keeps_a_bounded_list()
    {
        var log = new DryRunLog();
        for (var i = 0; i < DryRunLog.StepLimit + 50; i++) log.Intercept(ActionClass.Work, Verdict.Allow, "Would write", "x");
        Assert.Equal(DryRunLog.StepLimit, log.Steps.Count);
    }
}
