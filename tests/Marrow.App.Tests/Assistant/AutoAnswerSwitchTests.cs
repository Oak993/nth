using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.3 point 2, 16 "Ordering rule"; ED-NEW-dextra-as7-8): nth answers for the owner only
// once its injection self-check has passed in this process. Off is the default; a check that is still running, fails or
// throws leaves it off; the real corpus turns it on; and the controller logs which it is.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-8")]
public sealed class AutoAnswerSwitchTests
{
    [Fact]
    public void Off_is_the_default_and_stays_off()
    {
        Assert.False(AutoAnswerSwitch.Off.IsOn);
        Assert.True(AutoAnswerSwitch.Off.Ready.IsCompleted);
        Assert.Null(AutoAnswerSwitch.Off.Report);
        Assert.Equal(AutoAnswerSwitch.Never, AutoAnswerSwitch.Off.Status);
        using var rig = new DextraTaintRig(autoAnswer: false);
        Assert.False(rig.Controller.AutoAnswering);
    }

    [Fact]
    public void It_is_off_while_the_check_runs_and_on_only_once_it_passed()
    {
        using var release = new ManualResetEventSlim();
        var check = AutoAnswerSwitch.AfterCorpus(() =>
        {
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return DextraTaintRig.Pass;
        });
        // The check is held: whatever thread asks, the switch is off and says it is checking.
        Assert.False(check.IsOn);
        Assert.Equal(AutoAnswerSwitch.Checking, check.Status);
        Assert.False(check.Ready.IsCompleted);
        release.Set();
        Assert.True(MissionRig.Within(check.Ready));
        Assert.True(check.IsOn);
        Assert.Equal(AutoAnswerSwitch.Passed, check.Status);
    }

    [Fact]
    public void A_check_that_fails_or_throws_leaves_it_off()
    {
        var failed = AutoAnswerSwitch.AfterCorpus(() => new InjectionReport(60, 100, new ValueList<string>(["web-01: the reader: Command curl: allowed: Alone"])));
        var thrown = AutoAnswerSwitch.AfterCorpus(() => throw new InvalidOperationException("broken"));
        var empty = AutoAnswerSwitch.AfterCorpus(() => new InjectionReport(0, 0, ValueList<string>.Empty));
        Assert.True(MissionRig.Within(Task.WhenAll(failed.Ready, thrown.Ready, empty.Ready)));
        Assert.False(failed.IsOn);
        Assert.False(thrown.IsOn);
        Assert.False(empty.IsOn);
        Assert.StartsWith("Off: nth's injection checks failed (1 problems)", failed.Status, StringComparison.Ordinal);
        Assert.Equal(["the check could not run: InvalidOperationException"], thrown.Report!.Failures);
    }

    [Fact]
    public void The_real_corpus_turns_it_on()
    {
        var real = AutoAnswerSwitch.AfterCorpus();
        Assert.True(MissionRig.Within(real.Ready, 120));
        Assert.True(real.IsOn, string.Join("\n", real.Report?.Failures.Take(5) ?? []));
        Assert.True(real.Report!.Cases >= InjectionSelfCheck.MinimumCases);
    }

    [Fact]
    public void The_controller_logs_whether_nth_answers_for_the_owner()
    {
        using var on = new DextraTaintRig();
        MissionRig.Until(() => Lines(on).Count > 0, "the auto-answer line");
        Assert.Equal(AutoAnswerSwitch.Passed, Assert.Single(Lines(on)).Result);
        Assert.True(on.Controller.AutoAnswering);

        using var off = new DextraTaintRig(check: () => new InjectionReport(60, 10, new ValueList<string>(["mail-01: the lead after the merge: nth-dextra remember: a memory note would be used"])));
        MissionRig.Until(() => Lines(off).Count > 0, "the auto-answer line");
        Assert.Contains("First: mail-01", Assert.Single(Lines(off)).Result, StringComparison.Ordinal);
        Assert.False(off.Controller.AutoAnswering);
    }

    private static List<AuditEntry> Lines(DextraTaintRig rig) => [.. rig.Logged().Where(a => a.Tool == "auto_answer")];
}
