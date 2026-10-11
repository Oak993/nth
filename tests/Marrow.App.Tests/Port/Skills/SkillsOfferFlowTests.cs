using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Services.Port.Skills.Providers;
namespace Marrow.App.Tests.Port.Skills;
public sealed class SkillsOfferFlowTests
{
    private const string Continuation = "LibreOffice 26.8.1 is installed now (`soffice` is on PATH). Please continue the task from where it stopped."; // the release's T3 (ED-NEW-dl-11)
    private const string Denial = "LibreOffice isn't on this PC and the user chose not to download it now.";

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void R1_approves_held_command_only_after_verified_done()
    {
        using var r = new SkillsOfferRig(); r.Turn.Send("fixture"); var approval = r.Held(); r.Offer(approval);
        Assert.Empty(r.Turn.Session.ApprovalAnswers); Assert.Equal(0, r.Download.Calls);
        r.Succeed(); r.Run(r.Flow.Accept);
        Assert.Equal((approval.Id, "allow", (string?)null), Assert.Single(r.Turn.Session.ApprovalAnswers));
        Assert.Single(r.Turn.Session.Inputs); Assert.Empty(r.Turn.Runtime.FollowUps); Assert.Equal(OfferStage.Continuing, r.Flow.Stage);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void R1_correlates_command_title_when_approval_detail_is_a_description()
    {
        using var r = new SkillsOfferRig(); r.Turn.Send("fixture"); var approval = r.Held("Convert the document");
        Assert.NotNull(r.Flow.Card); r.Succeed(); r.Run(r.Flow.Accept);
        Assert.Equal((approval.Id, "allow", (string?)null), Assert.Single(r.Turn.Session.ApprovalAnswers));
        Assert.Single(r.Turn.Session.Inputs);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void R1_declines_with_S1_reason_and_never_downloads()
    {
        using var r = new SkillsOfferRig(); r.Turn.Send("fixture"); var approval = r.Held(); r.Offer(approval); r.Main(r.Flow.Decline);
        Assert.Equal((approval.Id, "deny", (string?)Denial), Assert.Single(r.Turn.Session.ApprovalAnswers));
        Assert.Equal(OfferStage.Declined, r.Flow.Stage); Assert.Equal(0, r.Download.Calls); Assert.Single(r.Turn.Session.Inputs);
    }

    [Theory, InlineData(TurnStatus.Completed), InlineData(TurnStatus.Failed), Trait("Rule", "R-SKILLS-12")]
    public void R2_queues_S1_once_and_sends_after_turn_ends(TurnStatus status)
    {
        using var r = new SkillsOfferRig(); r.Turn.Send("fixture"); r.Offer(); r.Succeed(); r.Run(r.Flow.Accept); r.Run(r.Flow.Accept);
        Assert.Single(r.Turn.Session.Inputs); Assert.Equal(Continuation, Assert.Single(r.Turn.Runtime.FollowUps).Text);
        r.End(status); Assert.Equal(Continuation, r.Turn.Session.Inputs[1].Text); Assert.Empty(r.Turn.Runtime.FollowUps);
        Assert.Single(r.Turn.Runtime.Entries, e => e.Item.TimelineItemContent is TimelineItem.Content.Notice n && n.Value.Message == Continuation);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void R3_sends_S1_immediately_and_preserves_composer_draft()
    {
        using var r = new SkillsOfferRig(); r.Main(() => r.Turn.Runtime.Draft = new() { Text = "unsent draft" }); r.Offer(); r.Succeed(); r.Run(r.Flow.Accept);
        r.Turn.H.Main.Loop.RunUntil(() => r.Turn.Session.Inputs.Count == 1, AppModelHarness.Bound);
        Assert.Equal(Continuation, r.Turn.Session.Inputs[0].Text); Assert.Equal("unsent draft", r.Turn.Runtime.Draft.Text);
    }

    [Theory, InlineData(false), InlineData(true), Trait("Rule", "R-SKILLS-12")]
    public void R2_and_R3_decline_send_nothing(bool running)
    {
        using var r = new SkillsOfferRig(); if (running) r.Turn.Send("fixture"); r.Offer(); r.Main(r.Flow.Decline); r.Run(r.Flow.Accept);
        Assert.Equal(running ? 1 : 0, r.Turn.Session.Inputs.Count); Assert.Empty(r.Turn.Runtime.FollowUps); Assert.Equal(0, r.Download.Calls);
        Assert.True(r.State.IsDeclined(r.Turn.Runtime.ThreadID.ToString(), "T3"));
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void R4_returns_installed_path_in_same_turn_without_continuation()
    {
        using var r = new SkillsOfferRig(); r.Turn.Send("fixture"); Task<ToolRequestOutcome> answer = null!;
        r.Main(() => answer = r.Flow.Ask("soffice", "fixture", CancellationToken.None)); Assert.False(answer.IsCompleted); r.Succeed(); r.Run(r.Flow.Accept);
        r.Run(async () => Assert.Equal(r.InstalledPath, (await answer).InstalledPath)); Assert.Single(r.Turn.Session.Inputs); Assert.Empty(r.Turn.Runtime.FollowUps);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void R4_decline_returns_declined_without_continuation()
    {
        using var r = new SkillsOfferRig(); Task<ToolRequestOutcome> answer = null!;
        r.Main(() => answer = r.Flow.Ask("T3", "fixture", CancellationToken.None)); r.Main(r.Flow.Decline);
        r.Run(async () => Assert.Null((await answer).InstalledPath)); Assert.Empty(r.Turn.Session.Inputs);
    }

    [Theory, InlineData(OfferStage.Downloading), InlineData(OfferStage.Checking), Trait("Rule", "R-SKILLS-12")]
    public void Cancel_returns_to_asked_and_late_completion_cannot_resume(OfferStage stage)
    {
        using var r = new SkillsOfferRig(); r.Offer(); var completion = new TaskCompletionSource<IReadOnlyDictionary<string, string>>();
        r.Download.Script = (_, progress, _) => { progress(new(stage, 10, 20)); return completion.Task; };
        Task accepting = null!; r.Main(() => accepting = r.Flow.Accept()); Assert.Equal(stage, r.Flow.Stage); r.Main(r.Flow.Cancel);
        Assert.Equal(OfferStage.Asked, r.Flow.Stage); completion.SetResult(new Dictionary<string, string> { ["T3"] = r.InstalledPath }); r.Run(() => accepting);
        Assert.Empty(r.Turn.Session.Inputs); Assert.Empty(r.Turn.Runtime.FollowUps); Assert.Equal(OfferStage.Asked, r.Flow.Stage);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Failure_keeps_S1_reason_and_Retry_runs_again()
    {
        using var r = new SkillsOfferRig(); r.Offer(); r.Download.Script = (_, _, _) => throw new IOException("The file didn't match its checksum."); r.Run(r.Flow.Accept);
        Assert.Equal(OfferStage.Failed, r.Flow.Stage); Assert.Equal("The file didn't match its checksum.", r.Flow.Failure); Assert.Empty(r.Turn.Session.Inputs);
        r.Succeed(); r.Run(r.Flow.Accept); Assert.Equal(2, r.Download.Calls); Assert.Null(r.Flow.Failure);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void One_offer_per_item_per_chat_and_second_item_joins_card()
    {
        using var r = new SkillsOfferRig(); r.Offer(); var first = r.Flow.Card; r.Offer(); Assert.Same(first, r.Flow.Card);
        r.Main(() => r.Flow.Observe(new CommandHeld("python script.py"))); Assert.Equal(new[] { "T3", "T1" }, r.Flow.Card!.Requested);
        r.Main(r.Flow.Decline); r.Offer(); Assert.Equal(OfferStage.Declined, r.Flow.Stage); Assert.Equal(0, r.Download.Calls);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Progress_exposes_bytes_and_checking_before_done()
    {
        using var r = new SkillsOfferRig(); r.Offer(); r.Download.Script = (items, progress, _) =>
        {
            progress(new(OfferStage.Downloading, 10, 20)); Assert.Equal(10, r.Flow.Progress!.BytesDone); Assert.Equal(20, r.Flow.Progress.BytesTotal);
            progress(new(OfferStage.Checking, 20, 20)); Assert.Equal(OfferStage.Checking, r.Flow.Stage);
            return Task.FromResult<IReadOnlyDictionary<string, string>>(items.ToDictionary(i => i.Id, _ => r.InstalledPath));
        };
        r.Run(r.Flow.Accept); Assert.Equal(OfferStage.Continuing, r.Flow.Stage);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Missing_offline_download_seam_fails_closed()
    {
        using var r = new SkillsOfferRig(); r.Offer(); r.Run(r.Flow.Accept); Assert.Equal(OfferStage.Failed, r.Flow.Stage); Assert.Empty(r.Turn.Session.Inputs);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Cancelled_model_request_does_not_later_resume_it()
    {
        using var r = new SkillsOfferRig(); using var cancellation = new CancellationTokenSource(); Task<ToolRequestOutcome> answer = null!;
        r.Main(() => answer = r.Flow.Ask("T3", "fixture", cancellation.Token)); cancellation.Cancel();
        r.Run(async () => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer)); r.Succeed(); r.Run(r.Flow.Accept); Assert.Empty(r.Turn.Session.Inputs);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void S1_declared_skill_need_uses_same_offer_and_executable_message()
    {
        using var r = new SkillsOfferRig();
        r.SetState(Marrow.Core.Port.Skills.State.AddonsState.Restore([new("A2", Marrow.Core.Port.Skills.State.ItemState.Installed, "fixture-pin", null, null)], [], [], []));
        r.Main(() => r.Flow.Observe(new SkillUsed("docx", "A2"))); Assert.Contains("T3", r.Flow.Card!.Requested);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Verified_result_must_include_every_requested_install_path()
    {
        using var r = new SkillsOfferRig(); r.Offer(); r.Download.Script = (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        r.Run(r.Flow.Accept); Assert.Equal(OfferStage.Failed, r.Flow.Stage); Assert.Empty(r.Turn.Session.Inputs);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Repeated_accept_during_download_does_not_start_another_download()
    {
        using var r = new SkillsOfferRig(); r.Offer(); var completion = new TaskCompletionSource<IReadOnlyDictionary<string, string>>();
        r.Download.Script = (_, _, _) => completion.Task; Task first = null!; r.Main(() => first = r.Flow.Accept()); r.Run(r.Flow.Accept);
        Assert.Equal(1, r.Download.Calls); r.Main(r.Flow.Cancel); completion.SetResult(new Dictionary<string, string> { ["T3"] = r.InstalledPath }); r.Run(() => first);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Api_request_tool_receives_exact_installed_at_result_without_a_new_turn()
    {
        using var r = new SkillsOfferRig(); r.SetState(Marrow.Core.Port.Skills.State.AddonsState.Restore([new("A1", Marrow.Core.Port.Skills.State.ItemState.Installed, "fixture-pin", null, null)], [], [], []));
        var tools = new NativeSkillTools(null, offer: r.Flow); Assert.True(tools.Owns("request_tool")); Task<NativeToolResult> result = null!;
        r.Main(() => result = tools.Execute("request_tool", Marrow.Core.Support.JSONValue.FromDictionaryLiteral(("name", "soffice"), ("reason", "fixture"))));
        Assert.False(result.IsCompleted); r.Succeed(); r.Run(r.Flow.Accept);
        r.Run(async () => Assert.Equal("installed at " + r.InstalledPath, (await result).Output)); Assert.Empty(r.Turn.Session.Inputs);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Releasing_session_drops_stale_held_approval()
    {
        using var r = new SkillsOfferRig(); r.Turn.Send("fixture"); var approval = r.Held(); r.Offer(approval); r.Main(r.Turn.Runtime.StopSession);
        r.Succeed(); r.Run(r.Flow.Accept); Assert.Empty(r.Turn.Session.ApprovalAnswers);
    }

    [Fact, Trait("Rule", "R-SKILLS-12")]
    public void Existing_download_attaches_without_starting_a_second_download()
    {
        using var r = new SkillsOfferRig(); r.Turn.Send("fixture");
        r.SetState(Marrow.Core.Port.Skills.State.AddonsState.Restore([new("T3", Marrow.Core.Port.Skills.State.ItemState.Downloading, null, "26.8.1", null)], [], [], []));
        var completion = new TaskCompletionSource<IReadOnlyDictionary<string, string>>();
        r.Download.Script = (_, progress, _) => { progress(new(OfferStage.Downloading, 10, 20)); return completion.Task; };
        r.Offer(); Assert.NotNull(r.Flow.Card); Assert.Equal(1, r.Download.AttachCalls); Assert.Equal(0, r.Download.Calls);
        completion.SetResult(new Dictionary<string, string> { ["T3"] = r.InstalledPath });
        r.Turn.H.Main.Loop.RunUntil(() => r.Flow.Stage == OfferStage.Continuing, AppModelHarness.Bound);
        Assert.Equal(Continuation, Assert.Single(r.Turn.Runtime.FollowUps).Text); Assert.Single(r.Turn.Session.Inputs);
    }
}
