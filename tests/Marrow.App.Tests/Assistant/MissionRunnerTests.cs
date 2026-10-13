using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 3.3, 3.4, 4.2, 12.3, 12.6, 12.6a, 12.6b, 13.2, 15.3; ED-NEW-dextra-as5-6 to -11): the mission
// runner over fake provider chats. A mission makes its workspace, waits at the agent gate, starts a sealed lead (a Hydra
// team when asked), queues every approval (Allow included) and answers the engine's Deny at once, runs a reviewer that
// passes it or sends it back (two redirects, then Failed), offers the merge, and obeys Stop, Pause, Resume and Redirect.
// Depth, the 16-agent cap and the memory floor hold, and a crash comes back through the journal.
[Trait("Decision", "ED-NEW-dextra-as5-6")]
[Trait("Feature", "AS-5")]
public sealed class MissionRunnerTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private MissionRig Rig(Func<ApprovalRequest, ApprovalVerdict>? policy = null, MissionLimits? limits = null, MemoryReading? memory = null,
        Action<MissionRecord>? save = null) => new(temp.Folder, policy, limits, memory, save);

    private static ApprovalVerdict Says(ActionClass actionClass, Verdict verdict, PolicyRule rule = PolicyRule.Alone, string reason = "the rules") =>
        new(new Classification(actionClass, reason), new PolicyDecision(verdict, rule, reason));

    /// <summary>The policy of most tests: rm is denied, git push is irreversible, ls would be allowed, the rest asks.</summary>
    private static ApprovalVerdict Policy(ApprovalRequest request) => request.Title switch
    {
        var t when t.StartsWith("rm ", StringComparison.Ordinal) => Says(ActionClass.HardDeny, Verdict.Deny, PolicyRule.HardDeny, "Never runs"),
        var t when t.StartsWith("git push", StringComparison.Ordinal) => Says(ActionClass.Outward, Verdict.Ask, PolicyRule.AlwaysConfirmed, "Always confirmed"),
        var t when t.StartsWith("ls", StringComparison.Ordinal) || t.StartsWith("cat", StringComparison.Ordinal) => Says(ActionClass.ReadLocal, Verdict.Allow),
        _ => Says(ActionClass.Unknown, Verdict.Ask, PolicyRule.Unknown, "Not recognised"),
    };

    private static void EndTurn(MissionRig rig, Guid thread, string? answer, TurnStatus status = TurnStatus.Completed)
    {
        rig.Agents.Finish(thread, answer);
        rig.Runner.OnTurnFinished(thread, status, continues: false);
    }

    private static Guid ReviewerOf(MissionRig rig, MissionRecord record)
    {
        MissionRig.Until(() => rig.Now(record).ReviewerThread is not null, "the reviewer to start");
        return rig.Now(record).ReviewerThread!.Value;
    }

    // ---- a mission from start to merge ----

    [Fact]
    public void A_lead_and_its_Hydra_team_work_in_the_missions_worktree_then_the_reviewer_passes_it_and_the_merge_waits_for_the_owner()
    {
        var rig = Rig(Policy);
        var record = rig.Started(rig.Request(team: true));
        var lead = rig.Agents.Lead(record);
        Assert.Equal(MissionState.Working, record.Mission.State);
        Assert.Equal(["create " + Path.Combine(temp.Folder, "owner")], rig.Workspaces.Calls);
        Assert.NotNull(record.Workspace);
        Assert.Equal(record.Workspace!.WorkingFolder, lead.Launch.Folder);
        Assert.Equal(record.Workspace.WorkingFolder, record.Mission.WorktreePath);
        Assert.True(lead.Launch.Team);
        Assert.False(lead.Launch.Reviewer);
        Assert.Equal("model-a", lead.Launch.Model);
        Assert.Equal(record.Workspace.Branch, lead.Launch.Branch);
        Assert.Contains("You run a Hydra team", lead.Launch.Brief, StringComparison.Ordinal);
        Assert.Contains("Make the login wait 30 s", lead.Launch.Brief, StringComparison.Ordinal);
        Assert.True(rig.Gate.Holds(lead.Id));
        Assert.Equal([lead.Id], rig.Runner.AgentThreads(record.Id));

        // Two heads: one nth starts behind the gate, one the provider runs inside the lead.
        var tests = Guid.NewGuid();
        var docs = Guid.NewGuid();
        rig.Runner.HeadAdded(lead.Id, tests, "Tests");
        Assert.Equal(HeadState.Queued, rig.Now(record).Mission.Heads.Single().State);
        Assert.True(MissionRig.Within(rig.Runner.AdmitHead(tests, ProviderKind.Claude), 10));
        MissionRig.Until(() => rig.Now(record).Mission.Heads.Single().State == HeadState.Working, "the head to start");
        Assert.True(rig.Gate.Holds(tests));
        rig.Runner.HeadAdded(lead.Id, docs, "Docs", native: true);
        Assert.Equal(HeadState.Working, rig.Now(record).Mission.Heads[1].State);
        Assert.Equal(record.Id, rig.Runner.MissionOf(docs)!.Id);
        rig.Agents.HeadsOf[lead.Id] = [new AgentHead(tests, "Tests", false, false), new AgentHead(docs, "Docs", false, false)];

        // The lead's turn ends while its heads work: no review yet.
        EndTurn(rig, lead.Id, "I split the work into two heads.");
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);
        Assert.Null(rig.Now(record).ReviewerThread);

        rig.Runner.HeadFinished(tests, TurnStatus.Completed);
        Assert.False(rig.Gate.Holds(tests));
        Assert.Equal(HeadState.Done, rig.Now(record).Mission.Heads[0].State);
        rig.Agents.HeadsOf[lead.Id] = [new AgentHead(tests, "Tests", true, false), new AgentHead(docs, "Docs", true, false)];

        // Hydra hands the lead its heads' reports; its turn ends with the result.
        Assert.True(rig.Agents.Send(lead.Id, "Your heads finished."));
        EndTurn(rig, lead.Id, "The login now waits 30 s; LoginTests cover it.");
        var reviewer = ReviewerOf(rig, record);
        var reviewing = rig.Now(record);
        Assert.Equal(MissionState.Reviewing, reviewing.Mission.State);
        Assert.All(reviewing.Mission.Heads, h => Assert.Equal(HeadState.Done, h.State));
        Assert.Equal("The login now waits 30 s; LoginTests cover it.", reviewing.Report);
        var review = rig.Agents.Of(reviewer);
        Assert.True(review.Launch.Reviewer);
        Assert.Equal("model-b", review.Launch.Model);
        Assert.Equal("Review: Fix the login timeout", review.Launch.Title);
        Assert.Contains("The login now waits 30 s", review.Launch.Brief, StringComparison.Ordinal);
        Assert.Contains("- Modified: src/login.cs", review.Launch.Brief, StringComparison.Ordinal);
        Assert.EndsWith(ReviewVerdict.Instruction, review.Launch.Brief, StringComparison.Ordinal);

        EndTurn(rig, reviewer, "I read the change and ran the tests.\nVERDICT: PASS");
        MissionRig.Until(() => rig.Now(record).MergePending, "the merge card");
        var done = rig.Now(record);
        Assert.Equal(MissionState.Done, done.Mission.State);
        Assert.Equal("model-b", done.ReviewedBy);
        Assert.True(done.Reviews.Single().Passed);
        Assert.NotNull(done.Ended);
        Assert.Equal(1, done.Mission.ChangedFiles);
        Assert.Equal(1, rig.Agents.Of(reviewer).SessionStops);
        Assert.Equal(0, rig.Gate.Running);

        var merge = Assert.Single(rig.Queue.Of(record.Id));
        Assert.Equal(DecisionKind.Merge, merge.Kind);
        Assert.Equal(DecisionWeight.Irreversible, merge.Weight);
        Assert.Equal("Merge “Fix the login timeout” (38 lines) into main?", merge.Title);
        Assert.Contains(merge.Inputs, i => i.Name == "Branch" && i.Value == record.Workspace.Branch);
        Assert.DoesNotContain("merge", rig.Workspaces.Calls);

        Assert.Null(rig.Queue.Answer(merge.Id, DecisionAnswer.Merge));
        MissionRig.Until(() => !rig.Now(record).MergePending, "the merge");
        Assert.Equal(["create " + Path.Combine(temp.Folder, "owner"), "review", "review", "merge"], rig.Workspaces.Calls);
        Assert.Contains(rig.Audit, e => e.Tool == "merge" && e.Result == "Merged");
    }

    [Fact]
    public void Discard_drops_the_workspace_and_merges_nothing()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        EndTurn(rig, record.LeadThread!.Value, "Done.");
        EndTurn(rig, ReviewerOf(rig, record), "VERDICT: PASS");
        MissionRig.Until(() => rig.Now(record).MergePending, "the merge card");
        var merge = rig.Queue.Of(record.Id).Single();
        Assert.Null(rig.Queue.Answer(merge.Id, DecisionAnswer.Discard));
        MissionRig.Until(() => rig.Now(record).Workspace is null, "the discard");
        Assert.Contains("discard", rig.Workspaces.Calls);
        Assert.DoesNotContain("merge", rig.Workspaces.Calls);
        var after = rig.Now(record);
        Assert.False(after.MergePending);
        Assert.Null(after.Mission.WorktreePath);
        Assert.Equal(MissionState.Done, after.Mission.State);
    }

    [Fact]
    public void A_mission_that_changed_nothing_raises_no_merge_card()
    {
        var rig = Rig(Policy);
        rig.Workspaces.Changes = new MergeReview(ValueList<WorkspaceChange>.Empty, "");
        var record = rig.Started();
        EndTurn(rig, record.LeadThread!.Value, "Nothing needed changing.");
        EndTurn(rig, ReviewerOf(rig, record), "VERDICT: PASS");
        MissionRig.Until(() => rig.Now(record).Mission.State == MissionState.Done, "Done");
        MissionRig.Until(() => rig.Workspaces.Calls.Count(c => c == "review") == 2, "the change check");
        Assert.Empty(rig.Queue.Of(record.Id));
        Assert.False(rig.Now(record).MergePending);
    }

    // ---- approvals ----

    [Fact]
    public void Every_approval_waits_for_the_owner_even_one_the_engine_would_allow_and_a_deny_is_answered_at_once()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        var lead = record.LeadThread!.Value;

        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a-ls", "ls src")));
        Assert.Empty(rig.Agents.Resolutions);
        var allowed = rig.Queue.Of(record.Id).Single();
        Assert.Equal(DecisionKind.Approval, allowed.Kind);
        Assert.Equal(DecisionWeight.Reversible, allowed.Weight);
        Assert.Equal("Run this command?", allowed.Title);
        Assert.Equal("Asks until nth's injection checks pass", allowed.Rule);
        Assert.Equal("nth does not answer for you until its injection checks pass (it would have allowed this).", allowed.Why);
        Assert.Equal("Lead", allowed.Agent);
        Assert.Equal("a-ls", allowed.RequestId);
        Assert.Equal(MissionState.NeedsYou, rig.Now(record).Mission.State);

        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a-rm", "rm -rf build")));
        Assert.Equal([(lead, "a-rm", false)], rig.Agents.Resolutions);
        Assert.Single(rig.Queue.Of(record.Id));

        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a-push", "git push origin main")));
        var push = rig.Queue.Of(record.Id).Single(c => c.RequestId == "a-push");
        Assert.Equal(DecisionWeight.Irreversible, push.Weight);
        Assert.Equal([DecisionAnswerKind.DoIt, DecisionAnswerKind.Dont], push.Answers);
        Assert.Equal("Always confirmed", push.Rule);

        Assert.Null(rig.Queue.Answer(allowed.Id, DecisionAnswer.AllowOnce));
        Assert.Equal((lead, "a-ls", true), rig.Agents.Resolutions[1]);
        Assert.Equal(MissionState.NeedsYou, rig.Now(record).Mission.State);
        Assert.Null(rig.Queue.Answer(push.Id, DecisionAnswer.Dont("not yet")));
        Assert.Equal((lead, "a-push", false), rig.Agents.Resolutions[2]);
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);

        Assert.Contains(rig.Audit, e => e.Result == "asked: AutoAnswerOff");
        Assert.Contains(rig.Audit, e => e.Result == "refused: HardDeny");
        Assert.Contains(rig.Audit, e => e.Result == "allowed by the owner: AllowOnce");
        Assert.False(rig.Runner.OnApproval(Guid.NewGuid(), MissionRig.Approval("x", "ls")));
    }

    [Fact]
    public void A_cancelled_request_withdraws_its_card_and_a_question_is_a_card_too()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Runner.OnApproval(lead, MissionRig.Approval("a1", "npm test"));
        rig.Runner.OnRequestResolved(lead, "a1");
        var withdrawn = rig.Queue.Items.Single();
        Assert.Equal(DecisionState.Withdrawn, withdrawn.State);
        Assert.Equal(MissionRunner.RequestSettled, withdrawn.Note);
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);

        var question = new QuestionRequest("q1", new ValueList<QuestionRequest.Question>([
            new QuestionRequest.Question("pick", "Style", "Which button style?", new ValueList<QuestionRequest.Choice>([new("Glass"), new("Flat")]), false, false, false),
        ]));
        Assert.True(rig.Runner.OnQuestion(lead, question));
        var card = rig.Queue.Of(record.Id).Single();
        Assert.Equal(DecisionKind.Question, card.Kind);
        Assert.Equal(["Glass", "Flat"], card.Options);
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.Pick("Glass")));
        var answered = rig.Agents.Answers.Single();
        Assert.Equal("q1", answered.Request);
        Assert.Equal(["Glass"], answered.Answers["pick"]);

        var secret = new QuestionRequest("q2", new ValueList<QuestionRequest.Question>([
            new QuestionRequest.Question("key", "Key", "Paste the deploy key", ValueList<QuestionRequest.Choice>.Empty, false, true, true),
        ]));
        rig.Runner.OnQuestion(lead, secret);
        var secretCard = rig.Queue.Of(record.Id).Single();
        Assert.Empty(secretCard.Options);
        Assert.Equal("It asks for a secret: answer it in the chat.", rig.Queue.Answer(secretCard.Id, DecisionAnswer.Discuss("fake-not-a-key")));
        Assert.Single(rig.Agents.Answers);
    }

    [Fact]
    public void The_reviewer_only_reads_and_every_read_it_asks_for_still_waits_for_the_owner()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        EndTurn(rig, record.LeadThread!.Value, "Done.");
        var reviewer = ReviewerOf(rig, record);
        rig.Policy = r => r.Title.StartsWith("cat", StringComparison.Ordinal) ? Says(ActionClass.ReadLocal, Verdict.Allow) : Says(ActionClass.Work, Verdict.Allow);
        Assert.True(rig.Runner.OnApproval(reviewer, MissionRig.Approval("w1", "git commit -am fix")));
        Assert.Equal([(reviewer, "w1", false)], rig.Agents.Resolutions);
        Assert.Empty(rig.Queue.Of(record.Id));
        Assert.Contains(rig.Audit, e => e.Result == "refused: OwnerNever");

        Assert.True(rig.Runner.OnApproval(reviewer, MissionRig.Approval("r1", "cat src/login.cs")));
        var read = rig.Queue.Of(record.Id).Single();
        Assert.Equal("Reviewer", read.Agent);
        Assert.Single(rig.Agents.Resolutions);
    }

    // ---- the reviewer's verdicts ----

    [Fact]
    public void A_fail_goes_back_to_the_lead_twice_and_the_third_fail_fails_the_mission()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        var reviewers = new List<Guid>();
        for (var round = 1; round <= 2; round++)
        {
            EndTurn(rig, lead, "Done, round " + round + ".");
            var reviewer = ReviewerOf(rig, record);
            reviewers.Add(reviewer);
            if (round == 2) Assert.Contains("Earlier reviews failed it for:\n- the timeout is still 5 s", rig.Agents.Of(reviewer).Launch.Brief.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            EndTurn(rig, reviewer, "VERDICT: FAIL the timeout is still 5 s");
            var back = rig.Now(record);
            Assert.Equal(MissionState.Working, back.Mission.State);
            Assert.Equal(round, back.Mission.ReviewRounds);
            Assert.Null(back.ReviewerThread);
            Assert.Equal(1, rig.Agents.Of(reviewer).SessionStops);
            Assert.Equal($"The reviewer did not pass the result (review {round} of 3). Fix this, then finish again:\nthe timeout is still 5 s",
                rig.Agents.Of(lead).Sent[^1]);
        }
        EndTurn(rig, lead, "Done, round 3.");
        var last = ReviewerOf(rig, record);
        Assert.DoesNotContain(last, reviewers);
        EndTurn(rig, last, "Still wrong.\nVERDICT: FAIL no test covers it");
        var failed = rig.Now(record);
        Assert.Equal(MissionState.Failed, failed.Mission.State);
        Assert.Equal("The reviewer failed it 3 times. Last: no test covers it", failed.Reason);
        Assert.Equal(3, failed.Reviews.Count);
        Assert.All(failed.Reviews, r => Assert.False(r.Passed));
        Assert.Equal(3, rig.Agents.Of(lead).Sent.Count); // the brief and two redirects, no third
        Assert.Empty(rig.Queue.Of(record.Id));
        Assert.False(failed.MergePending);
        Assert.Equal(0, rig.Gate.Running);
    }

    [Fact]
    public void An_answer_without_a_verdict_and_a_failed_review_turn_fail_closed()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        EndTurn(rig, lead, "Done.");
        EndTurn(rig, ReviewerOf(rig, record), "Looks fine to me.");
        Assert.Equal(1, rig.Now(record).Mission.ReviewRounds);
        Assert.Equal("The reviewer gave no verdict line.", rig.Now(record).Reviews.Single().Reasons);
        EndTurn(rig, lead, "Done again.");
        EndTurn(rig, ReviewerOf(rig, record), null, TurnStatus.Failed);
        Assert.Equal(2, rig.Now(record).Mission.ReviewRounds);
        Assert.Equal("The reviewer gave no answer.", rig.Now(record).Reviews[1].Reasons);
    }

    [Fact]
    public void A_redirect_waits_for_the_leads_turn_and_one_sent_now_reaches_an_idle_lead()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        Assert.Equal("Say what to tell the lead.", rig.Runner.Redirect(record.Id, "  "));
        Assert.Null(rig.Runner.Redirect(record.Id, "Use 30 s, not 20 s."));
        Assert.Equal("Use 30 s, not 20 s.", rig.Now(record).PendingRedirect);
        Assert.Single(rig.Agents.Of(lead).Sent);

        EndTurn(rig, lead, "Working on it.");
        Assert.Equal("Use 30 s, not 20 s.", rig.Agents.Of(lead).Sent[^1]);
        Assert.Null(rig.Now(record).PendingRedirect);
        Assert.Null(rig.Now(record).ReviewerThread);

        // Idle, waiting on a card: the redirect goes now.
        rig.Runner.OnApproval(lead, MissionRig.Approval("a1", "npm install"));
        EndTurn(rig, lead, "Waiting for npm install.");
        Assert.Equal(MissionState.NeedsYou, rig.Now(record).Mission.State);
        Assert.Null(rig.Runner.Redirect(record.Id, "Skip the install."));
        Assert.Equal("Skip the install.", rig.Agents.Of(lead).Sent[^1]);
        Assert.Null(rig.Now(record).PendingRedirect);
        Assert.Contains(rig.Audit, e => e.Tool == "message_agent" && e.Result == "redirect");
    }

    // ---- Stop, Pause, Resume ----

    [Fact]
    public void Stop_denies_pending_approvals_holds_the_rest_cuts_every_turn_and_ends_every_job_then_reads_Stopped()
    {
        var rig = Rig(Policy);
        rig.JobFactory.EndOnTerminate = false;
        var record = rig.Started(rig.Request(team: true));
        var lead = record.LeadThread!.Value;
        rig.Jobs.For(lead);
        var head = Guid.NewGuid();
        rig.Runner.HeadAdded(lead, head, "Tests");
        Assert.True(MissionRig.Within(rig.Runner.AdmitHead(head, ProviderKind.Claude), 10));
        MissionRig.Until(() => rig.Now(record).Mission.Heads.Single().State == HeadState.Working, "the head to start");
        rig.Agents.Restore(head, rig.Agents.Of(lead).Launch).Running = true;
        rig.Jobs.For(head);

        rig.Runner.OnApproval(lead, MissionRig.Approval("a1", "npm test"));
        rig.Runner.OnApproval(head, MissionRig.Approval("a2", "git push origin main"));
        rig.Runner.OnQuestion(lead, new QuestionRequest("q1", new ValueList<QuestionRequest.Question>([
            new QuestionRequest.Question("x", "", "Which port?", ValueList<QuestionRequest.Choice>.Empty, false, true, false)])));
        Assert.Equal(3, rig.Queue.Of(record.Id).Count);

        Assert.Null(rig.Runner.Stop(record.Id));
        var stopping = rig.Now(record);
        Assert.Equal(MissionState.Stopping, stopping.Mission.State);
        Assert.Equal(MissionState.Working, stopping.Mission.ResumeTo); // its cards are answered: Resume goes back to work
        Assert.Equal(HeadState.Stopping, stopping.Mission.Heads.Single().State);
        Assert.Contains((lead, "a1", false), rig.Agents.Resolutions);
        Assert.Contains((head, "a2", false), rig.Agents.Resolutions);
        Assert.Empty(rig.Queue.Of(record.Id));
        Assert.All(rig.Queue.Items.Where(c => c.MissionId == record.Id), c => Assert.Equal(DecisionState.Withdrawn, c.State));
        Assert.Contains(rig.Queue.Items, c => c.RequestId == "a1" && c.Note == MissionRunner.Withdrawn);
        Assert.Contains(rig.Queue.Items, c => c.RequestId == "q1" && c.Note == MissionRunner.WithdrawnQuestion);
        Assert.Equal(1, rig.Agents.Of(lead).Interrupts);
        Assert.Equal(1, rig.Agents.Of(head).Interrupts);
        Assert.Equal([lead], rig.Agents.HeadsStopped);
        Assert.All(rig.JobFactory.Made, j => Assert.Equal(1, j.Terminations));
        Assert.False(rig.Gate.Holds(lead));
        Assert.False(rig.Gate.Holds(head));
        Assert.Equal("The task is already stopping or stopped.", rig.Runner.Stop(record.Id));
        Assert.Equal("The task is stopping; Resume opens once it has stopped.", rig.Runner.Resume(record.Id));
        // What the cut turn still asks for is answered no at once.
        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a3", "npm test")));
        Assert.Contains((lead, "a3", false), rig.Agents.Resolutions);
        Assert.Empty(rig.Queue.Of(record.Id));

        // The turns end, but a process still runs: Stopping until the jobs are empty.
        EndTurn(rig, lead, null, TurnStatus.Interrupted);
        rig.Agents.Finish(head, null);
        rig.Runner.HeadFinished(head, TurnStatus.Interrupted);
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State);
        foreach (var job in rig.JobFactory.Made) job.LiveProcesses = 0;
        MissionRig.Until(() => rig.Now(record).Mission.State == MissionState.Stopped, "Stopped");
        Assert.Equal(HeadState.Stopped, rig.Now(record).Mission.Heads.Single().State);
        Assert.Empty(rig.Jobs.Threads);
        Assert.All(rig.JobFactory.Made, j => Assert.True(j.Disposed));
        Assert.Equal(StopCause.Owner, rig.Now(record).Mission.StoppedBy);
    }

    [Fact]
    public void Stop_holds_a_card_that_is_no_provider_request_and_Resume_runs_the_cut_turn_again_behind_the_gate()
    {
        var rig = Rig(Policy, limits: new MissionLimits { Depth = 2 });
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        Assert.Null(rig.Runner.RequestHelpers(record.Id, "Write the docs", 2, "It is a lot", out var waiting));
        Assert.StartsWith("Waiting for the owner: ", waiting, StringComparison.Ordinal);
        var limit = rig.Queue.Of(record.Id).Single();
        Assert.Equal(DecisionKind.Limit, limit.Kind);

        Assert.Null(rig.Runner.Stop(record.Id));
        Assert.Equal(DecisionHold.Stop, rig.Queue.Find(limit.Id)!.HeldBy);
        Assert.Equal("Held by Stop.", rig.Queue.Answer(limit.Id, DecisionAnswer.AllowOnce));
        EndTurn(rig, lead, null, TurnStatus.Interrupted);
        MissionRig.Until(() => rig.Now(record).Mission.State == MissionState.Stopped, "Stopped");

        Assert.Null(rig.Runner.Resume(record.Id));
        Assert.Equal(DecisionState.Open, rig.Queue.Find(limit.Id)!.State);
        MissionRig.Until(() => rig.Agents.Of(lead).Sent.Count == 2, "the cut turn to run again");
        Assert.Equal("The mission was stopped part way. Go on from where it stopped; do the last step again, since it may have been cut.",
            rig.Agents.Of(lead).Sent[1]);
        Assert.True(rig.Gate.Holds(lead));
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);
    }

    [Fact]
    public void Pause_lets_the_turn_finish_and_holds_only_irreversible_cards()
    {
        var rig = Rig(Policy);
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Runner.OnApproval(lead, MissionRig.Approval("a0", "git push origin feature"));
        Assert.Null(rig.Runner.Pause(record.Id));
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State); // Needs you runs no turn: it pauses at once
        Assert.Equal(DecisionHold.Pause, rig.Queue.Of(record.Id).Single().HeldBy);
        Assert.Null(rig.Runner.Resume(record.Id));
        Assert.Equal(MissionState.NeedsYou, rig.Now(record).Mission.State);
        Assert.Null(rig.Queue.Answer(rig.Queue.Of(record.Id).Single().Id, DecisionAnswer.DoIt));
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);

        Assert.True(rig.Agents.TurnRunning(lead));
        Assert.Null(rig.Runner.Pause(record.Id));
        Assert.Equal(MissionState.Pausing, rig.Now(record).Mission.State);
        Assert.Equal(0, rig.Agents.Of(lead).Interrupts);
        rig.Runner.OnApproval(lead, MissionRig.Approval("a1", "npm test"));
        rig.Runner.OnApproval(lead, MissionRig.Approval("a2", "git push origin main"));
        Assert.Equal(MissionState.Pausing, rig.Now(record).Mission.State);
        var reversible = rig.Queue.Of(record.Id).Single(c => c.RequestId == "a1");
        var irreversible = rig.Queue.Of(record.Id).Single(c => c.RequestId == "a2");
        Assert.Equal(DecisionState.Open, reversible.State);
        Assert.Equal(DecisionHold.Pause, irreversible.HeldBy);
        Assert.Equal("Held by Pause.", rig.Queue.Answer(irreversible.Id, DecisionAnswer.DoIt));
        Assert.Null(rig.Queue.Answer(reversible.Id, DecisionAnswer.AllowOnce));
        Assert.Contains((lead, "a1", true), rig.Agents.Resolutions);

        EndTurn(rig, lead, "Paused here.");
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);
        Assert.Null(rig.Now(record).ReviewerThread);

        Assert.Null(rig.Runner.Resume(record.Id));
        Assert.Equal(MissionState.NeedsYou, rig.Now(record).Mission.State);
        Assert.Equal(DecisionState.Open, rig.Queue.Find(irreversible.Id)!.State);
        Assert.Null(rig.Queue.Answer(irreversible.Id, DecisionAnswer.DoIt));
        Assert.Contains((lead, "a2", true), rig.Agents.Resolutions);
        // The lead's turn had ended before the pause took: once nothing waits, the mission moves on to its review.
        ReviewerOf(rig, record);
    }

    // ---- depth, the cap and the memory floor ----

    [Fact]
    public void Helpers_go_one_level_down_past_the_owners_depth_they_need_his_allow_and_past_four_they_are_refused()
    {
        var rig = Rig(Policy);
        var top = rig.Started();
        Assert.Equal(MissionLimits.TopLevel, top.Level);

        var child = rig.Runner.RequestHelpers(top.Id, "Write the tests", 2, "Two files", out var refused);
        Assert.Null(refused);
        Assert.NotNull(child);
        Assert.True(MissionRig.Within(child!.Launched, 10));
        var level3 = rig.Now(child.Record);
        Assert.Equal(3, level3.Level);
        Assert.Equal(top.Id, level3.Mission.ParentMissionId);
        Assert.Equal(MissionOrigin.Delegation, level3.Mission.Origin);
        Assert.True(level3.Team);
        Assert.Contains(level3.Id, rig.Now(top).Mission.ChildMissionIds);

        Assert.Null(rig.Runner.RequestHelpers(level3.Id, "Deeper", 1, "More", out refused));
        Assert.Equal("Waiting for the owner: Helpers here would run 4 levels deep; your limit is 3.", refused);
        var limit = rig.Queue.Of(level3.Id).Single();
        Assert.Equal(DecisionKind.Limit, limit.Kind);
        Assert.Equal(2, rig.Runner.Missions.Count);
        Assert.Null(rig.Queue.Answer(limit.Id, DecisionAnswer.AllowOnce));
        MissionRig.Until(() => rig.Runner.Missions.Count == 3, "the helpers past the depth");
        var level4 = rig.Runner.Missions.Single(m => m.Level == 4);
        Assert.Equal(level3.Id, level4.Mission.ParentMissionId);

        Assert.Null(rig.Runner.RequestHelpers(level4.Id, "Deepest", 1, "Even more", out refused));
        Assert.Equal("Helpers here would run 5 levels deep; nothing runs deeper than 4.", refused);
        Assert.Empty(rig.Queue.Of(level4.Id));
        Assert.Equal(3, rig.Runner.Missions.Count);

        var tooDeep = rig.Runner.Start(rig.Request() with { Parent = level4.Id });
        Assert.Equal(MissionState.Blocked, tooDeep.Record.Mission.State);
        Assert.Equal("Helpers here would run 5 levels deep; nothing runs deeper than 4.", tooDeep.Record.Reason);
        Assert.True(tooDeep.Launched.IsCompleted);
    }

    [Fact]
    public void An_agent_past_the_cap_waits_with_its_line_and_starts_when_a_slot_frees()
    {
        var rig = Rig(Policy, limits: new MissionLimits { LocalAgents = 1 });
        var first = rig.Started();
        var second = rig.Runner.Start(rig.Request("Second mission"));
        Assert.False(second.Launched.IsCompleted);
        var waiting = rig.Now(second.Record);
        Assert.Equal(MissionState.Planning, waiting.Mission.State);
        Assert.Equal("1 local agents are running; this one starts when one ends.", waiting.Waiting);
        Assert.Null(waiting.LeadThread);

        Assert.Null(rig.Runner.Stop(first.Id));
        Assert.True(MissionRig.Within(second.Launched, 10));
        var started = rig.Now(second.Record);
        Assert.Equal(MissionState.Working, started.Mission.State);
        Assert.Null(started.Waiting);
        Assert.True(rig.Gate.Holds(started.LeadThread!.Value));
    }

    [Fact]
    public void Sixteen_local_agents_run_and_the_seventeenth_mission_waits()
    {
        var rig = Rig(Policy);
        var running = Enumerable.Range(0, 16).Select(i => rig.Started(rig.Request("Mission " + i))).ToList();
        Assert.Equal(16, rig.Gate.Running);
        var seventeenth = rig.Runner.Start(rig.Request("Mission 16"));
        Assert.False(seventeenth.Launched.IsCompleted);
        Assert.Equal("16 local agents are running; this one starts when one ends.", rig.Now(seventeenth.Record).Waiting);
        Assert.Null(rig.Runner.Stop(running[0].Id));
        Assert.True(MissionRig.Within(seventeenth.Launched, 10));
        Assert.Equal(16, rig.Gate.Running);
    }

    [Fact]
    public void No_local_agent_starts_under_the_memory_floor_but_an_API_agent_does()
    {
        var rig = Rig(Policy, memory: new MemoryReading(32UL << 30, 2UL << 30));
        var local = rig.Runner.Start(rig.Request());
        Assert.False(local.Launched.IsCompleted);
        Assert.Equal("Free memory is under 15% (6% free); this agent starts when more is free.", rig.Now(local.Record).Waiting);

        var api = rig.Runner.Start(rig.Request("Local model work", ProviderKind.Local));
        Assert.True(MissionRig.Within(api.Launched, 10));
        Assert.Equal(MissionState.Working, rig.Now(api.Record).Mission.State);

        rig.Memory.Reading = FakeMemory.Plenty;
        rig.Gate.Pump();
        Assert.True(MissionRig.Within(local.Launched, 10));
        Assert.Equal(MissionState.Working, rig.Now(local.Record).Mission.State);
    }

    [Fact]
    public void A_provider_nth_cannot_seal_is_blocked_before_anything_starts()
    {
        var rig = Rig(Policy);
        foreach (var provider in Enum.GetValues<ProviderKind>().Where(p => !SealedProviderConfig.CanSeal(p)))
        {
            var start = rig.Runner.Start(rig.Request("Work on " + provider, provider));
            Assert.True(start.Launched.IsCompleted);
            Assert.Equal(MissionState.Blocked, start.Record.Mission.State);
            Assert.Equal(SealedProviderConfig.Refusal(provider), start.Record.Reason);
        }
        Assert.Empty(rig.Agents.Chats);
        Assert.Empty(rig.Workspaces.Calls);
        Assert.Equal(0, rig.Gate.Running);
    }

    [Fact]
    public void A_workspace_or_lead_that_cannot_start_blocks_the_mission_and_frees_its_slot()
    {
        var rig = Rig(Policy);
        rig.Workspaces.CreateFails = new MissionWorkspaceError("The folder is not there.");
        var noFolder = rig.Runner.Start(rig.Request());
        Assert.True(MissionRig.Within(noFolder.Launched, 10));
        Assert.Equal(MissionState.Blocked, rig.Now(noFolder.Record).Mission.State);
        Assert.Equal("The folder is not there.", rig.Now(noFolder.Record).Reason);

        rig.Workspaces.CreateFails = null;
        rig.Agents.FailStart = _ => new InvalidOperationException("no session");
        var noLead = rig.Runner.Start(rig.Request());
        Assert.True(MissionRig.Within(noLead.Launched, 10));
        Assert.Equal(MissionState.Blocked, rig.Now(noLead.Record).Mission.State);
        Assert.Equal("Its lead chat could not start: no session", rig.Now(noLead.Record).Reason);
        Assert.Equal(0, rig.Gate.Running);
    }

    // ---- crash resume ----

    [Fact]
    public void After_a_crash_a_live_mission_comes_back_paused_and_resumes_its_cut_turn_and_a_pending_merge_asks_again()
    {
        var options = new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted };
        var store = AssistantStore.Open(temp.Roots, options);
        void Save(MissionRecord r) => store.SaveMission(MissionJournal.StoreId(r.Id), MissionJournal.ToJson(r));
        var rig = Rig(Policy, save: Save);
        var live = rig.Started();
        var lead = live.LeadThread!.Value;
        var leadLaunch = rig.Agents.Of(lead).Launch;
        rig.Runner.OnApproval(lead, MissionRig.Approval("a1", "npm test"));
        Assert.Equal(MissionState.NeedsYou, rig.Now(live).Mission.State);

        var merged = rig.Started(rig.Request("Second"));
        EndTurn(rig, merged.LeadThread!.Value, "Done.");
        EndTurn(rig, ReviewerOf(rig, merged), "VERDICT: PASS");
        MissionRig.Until(() => rig.Now(merged).MergePending, "the merge card");
        store.AbandonForTests();

        using var reopened = AssistantStore.Open(temp.Roots, options);
        Assert.Contains(MissionJournal.StoreId(live.Id), reopened.OpenReport.Interrupted);
        var records = reopened.Missions().Select(m => MissionJournal.FromJson(m.Body)).OfType<MissionRecord>().ToList();
        Assert.Equal(2, records.Count);

        var after = Rig(Policy, save: r => reopened.SaveMission(MissionJournal.StoreId(r.Id), MissionJournal.ToJson(r)));
        after.Agents.Restore(lead, leadLaunch);
        after.Runner.Load(records);
        var back = after.Now(live);
        Assert.Equal(MissionState.Paused, back.Mission.State);
        Assert.Equal(MissionState.Working, back.Mission.ResumeTo);
        Assert.True(back.Interrupted);
        Assert.Equal(lead, back.LeadThread);
        Assert.Equal(live.Workspace, back.Workspace);
        Assert.Equal(back.Id, after.Runner.MissionOf(lead)!.Id);

        MissionRig.Until(() => after.Queue.Of(merged.Id).Count == 1, "the merge card again");
        var merge = after.Queue.Of(merged.Id).Single();
        Assert.Equal("Raised again after nth restarted.", merge.Note);
        Assert.Empty(after.Queue.Of(live.Id));

        Assert.Null(after.Runner.Resume(live.Id));
        MissionRig.Until(() => after.Agents.Of(lead).Sent.Count == 1, "the cut turn to run again");
        Assert.StartsWith("The mission was stopped part way.", after.Agents.Of(lead).Sent[0], StringComparison.Ordinal);
        Assert.Equal(MissionState.Working, after.Now(live).Mission.State);
        Assert.False(after.Now(live).Interrupted);
        // It asks again, and the card waits as before.
        after.Runner.OnApproval(lead, MissionRig.Approval("a1-again", "npm test"));
        Assert.Single(after.Queue.Of(live.Id));

        Assert.Null(after.Queue.Answer(merge.Id, DecisionAnswer.Merge));
        MissionRig.Until(() => !after.Now(merged).MergePending, "the merge");
        var journaled = MissionJournal.FromJson(reopened.Mission(MissionJournal.StoreId(merged.Id))!.Body)!;
        Assert.False(journaled.MergePending);
    }
}
