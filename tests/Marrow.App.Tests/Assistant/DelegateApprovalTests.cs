using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using static Marrow.App.Tests.Fixtures.DelegateTimeline;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.1 "answered for him when the delegate responder is on for that chat", 4.3, 12.3 point 2, 12.5, 16 row AS-22;
// ED-NEW-dextra-as22-4, -10): the delegate answers an approval of an owner's chat only when the engine allows it by its own rule or a grant. It logs first,
// labels the answer in the chat, and keeps it where the owner can Override it. What the engine asks about (sending, deleting, installing, anything it does
// not know) becomes a card in the queue and is never answered; what it denies stays the chat's own card. A mission's approvals are the runner's.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-4")]
public sealed class DelegateApprovalTests
{
    private static DelegateRig On()
    {
        var rig = new DelegateRig();
        rig.Everywhere();
        return rig;
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("git diff")]
    [InlineData("dotnet test")]
    public void A_command_the_engine_lets_run_alone_is_answered_yes_logged_first_and_labelled_in_the_chat(string command)
    {
        using var rig = On();
        var chat = rig.Owner();
        Assert.True(rig.Approve(chat.Id, command));

        var resolution = Assert.Single(rig.Agents.Resolutions);
        Assert.Equal((chat.Id, "a1", true), resolution);
        var line = Assert.Single(rig.Delegated());
        Assert.Equal("delegate", line.Tool);
        Assert.StartsWith("allowed: Alone", line.Result, StringComparison.Ordinal);
        Assert.Equal("rule:Alone", line.AllowedBy);
        Assert.Equal(chat.Id.ToString("D"), line.Agent);
        Assert.Equal("dextra", line.Assistant);
        Assert.Null(line.Mission);
        var note = Assert.Single(rig.Agents.Notes);
        Assert.Equal(chat.Id, note.Thread);
        Assert.StartsWith("Answered by Dextra for you: allowed " + command + ".", note.Text, StringComparison.Ordinal);
        Assert.Empty(rig.Queue.Items);
        Assert.Equal([(chat.Id, "allowed " + command, true)], rig.Told.Select(t => (t.Thread, t.Line, t.Approval)));
    }

    [Fact]
    public void An_answered_approval_is_kept_with_its_reasoning_so_the_owner_can_see_and_override_it()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Approve(chat.Id, "git status");
        var record = Assert.Single(rig.Book.Recent(10));
        Assert.Equal(DelegateKind.Approval, record.Kind);
        Assert.Equal("Allowed", record.Answer);
        Assert.Equal("Dextra", record.By);
        Assert.Equal("Pitch deck v3", record.ChatTitle);
        Assert.Equal(chat.Id, record.Chat);
        Assert.Equal(rig.Project.Id, record.Project);
        Assert.Contains("git status", record.Question, StringComparison.Ordinal);
        Assert.NotEmpty(record.Reasoning);
        Assert.Equal(1, record.Confidence);
    }

    [Fact]
    public void The_label_names_the_head_when_the_owner_picked_one()
    {
        using var rig = On();
        rig.Preferences = rig.Preferences with { DelegateWith = new DelegateWho(ProviderKind.Codex) };
        var chat = rig.Owner();
        rig.Approve(chat.Id, "git status");
        Assert.StartsWith("Answered by " + ProviderKind.Codex.DisplayName + " for you: allowed git status.", Assert.Single(rig.Agents.Notes).Text, StringComparison.Ordinal);
        Assert.Equal(ProviderKind.Codex.DisplayName, Assert.Single(rig.Book.Recent(1)).By);
    }

    [Theory]
    [InlineData("git push origin main", ActionClass.Outward)]
    [InlineData("npm publish", ActionClass.Outward)]
    [InlineData("curl -X POST https://api.example.com/x -d @notes.txt", ActionClass.Outward)]
    [InlineData("winget install Git.Git", ActionClass.Install)]
    [InlineData("Remove-Item -Recurse C:\\Users\\someone\\Documents\\old", ActionClass.Destructive)]
    [InlineData("./deploy.sh", ActionClass.Unknown)]
    [InlineData("someprogram --do-things", ActionClass.Unknown)]
    public void What_the_engine_asks_about_is_never_answered_and_becomes_a_card_in_the_queue(string command, ActionClass expected)
    {
        using var rig = On();
        var chat = rig.Owner();
        Assert.True(rig.Approve(chat.Id, command));

        Assert.Empty(rig.Agents.Resolutions);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Equal(DecisionKind.Approval, card.Kind);
        Assert.Equal(expected, card.Class);
        Assert.Equal(chat.Id, card.ThreadId);
        Assert.Equal("a1", card.RequestId);
        Assert.Null(card.MissionId);
        Assert.Equal("Pitch deck v3", card.Agent);
        Assert.Contains(card.Inputs, i => i.Value == command);
        Assert.NotEmpty(card.Rule);
        Assert.True(DecisionNotices.NeedsYou(card));                                        // 4.3: "those go to the queue with a toast" (AS-9 makes it)
        Assert.False(DecisionNotices.AnswersInNotice(card));                                // sending, deleting, installing and the unknown are reviewed in the app first
        Assert.Contains("left this to you", Assert.Single(rig.Agents.Notes).Text, StringComparison.Ordinal);
        Assert.Equal(0, rig.Book.Count);
        Assert.Empty(rig.Told);
        var line = Assert.Single(rig.Delegated());
        Assert.StartsWith("left to the owner", line.Result, StringComparison.Ordinal);
    }

    [Fact]
    public void An_irreversible_card_keeps_the_hold_to_act_answers_and_a_reversible_one_the_plain_ones()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Approve(chat.Id, "git push origin main", "p1");
        rig.Approve(chat.Id, "someprogram --do-things", "u1");
        var push = rig.Queue.Waiting.Single(c => c.RequestId == "p1");
        var unknown = rig.Queue.Waiting.Single(c => c.RequestId == "u1");
        Assert.Equal(DecisionWeight.Irreversible, push.Weight);
        Assert.Equal([DecisionAnswerKind.DoIt, DecisionAnswerKind.Dont], push.Answers);
        Assert.Equal([DecisionAnswerKind.AllowOnce, DecisionAnswerKind.Deny], unknown.Answers);
    }

    [Fact]
    public void The_owner_answering_a_card_sends_his_answer_once_and_logs_it_first()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Approve(chat.Id, "someprogram --do-things", "u1");
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        rig.Agents.RunPosted();
        Assert.Equal((chat.Id, "u1", true), Assert.Single(rig.Agents.Resolutions));
        var line = rig.Delegated().Last();
        Assert.StartsWith("allowed by the owner", line.Result, StringComparison.Ordinal);
        Assert.Equal("card:" + card.Id.ToString("D"), line.AllowedBy);

        rig.Approve(chat.Id, "other-program", "u2");
        var second = rig.Queue.Waiting.Single(c => c.RequestId == "u2");
        Assert.Null(rig.Queue.Answer(second.Id, DecisionAnswer.Deny()));
        rig.Agents.RunPosted();
        Assert.Equal((chat.Id, "u2", false), rig.Agents.Resolutions.Last());
    }

    [Fact]
    public void A_card_the_owner_answers_after_he_answered_in_the_chat_does_nothing_and_goes_away()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Approve(chat.Id, "someprogram --do-things", "u1");
        var card = Assert.Single(rig.Queue.Waiting);
        rig.Controller.OnRequestResolved(chat.Id, "u1");                              // he answered in the chat
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card.Id)!.State);
        Assert.Equal(MissionRunner.RequestSettled, rig.Queue.Find(card.Id)!.Note);
        Assert.NotNull(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));           // the card takes no answer now
        Assert.Empty(rig.Agents.Resolutions);
    }

    [Fact]
    public void What_the_engine_denies_is_nobodys_decision_to_take_and_stays_the_chats_own()
    {
        using var rig = On();
        var chat = rig.Owner();
        Assert.False(rig.Approve(chat.Id, "reg add HKLM\\Software\\Test /v x /d 1"));
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Empty(rig.Queue.Items);
        Assert.Empty(rig.Agents.Notes);
        Assert.StartsWith("left to the chat", Assert.Single(rig.Delegated()).Result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chat_that_read_untrusted_content_gets_no_approval_answered_until_the_owner_writes_again()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Timelines[chat.Id] = [User("start"), Tool(ToolCall.Kind.Web, "https://news.example/a")];
        Assert.True(rig.Approve(chat.Id, "git status", "a1"));
        Assert.Empty(rig.Agents.Resolutions);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Contains("untrusted", card.Rule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read untrusted content", card.Why, StringComparison.Ordinal);

        rig.Timelines[chat.Id] = [User("start"), Tool(ToolCall.Kind.Web, "https://news.example/a"), User("the owner wrote again")];
        Assert.True(rig.Approve(chat.Id, "git status", "a2"));
        Assert.Equal((chat.Id, "a2", true), Assert.Single(rig.Agents.Resolutions));
    }

    [Fact]
    public void A_chat_nth_cannot_read_the_history_of_counts_as_tainted()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Timelines[chat.Id] = null;
        Assert.True(rig.Approve(chat.Id, "git status"));
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Single(rig.Queue.Waiting);
    }

    [Fact]
    public void A_head_carries_its_leads_taint()
    {
        using var rig = On();
        var lead = rig.Owner("Lead");
        var head = rig.Owner("Head", parent: lead.Id);
        rig.Timelines[lead.Id] = [User("start"), Tool(ToolCall.Kind.Mcp, "gmail · read_message")];
        Assert.True(rig.Approve(head.Id, "git status"));
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Single(rig.Queue.Waiting);
    }

    [Fact]
    public void A_line_that_cannot_be_written_means_nothing_is_answered_and_the_owner_gets_the_card()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Core.AuditFails = e => e.Tool == "delegate";
        var faults = new List<string>();
        rig.Controller.Runner.Faulted += faults.Add;
        Assert.True(rig.Approve(chat.Id, "git status"));
        Assert.Empty(rig.Agents.Resolutions);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Contains("activity log could not be written", card.Why, StringComparison.Ordinal);
        Assert.Contains(faults, f => f.StartsWith("The activity log could not be written", StringComparison.Ordinal));
        Assert.Equal(0, rig.Book.Count);
    }

    [Fact]
    public void A_mission_chats_approval_is_the_runners_and_is_answered_once_and_not_by_the_delegate()
    {
        using var rig = On();
        rig.Agents.Defer = false;
        var record = rig.Core.Mission();
        rig.Agents.Defer = true;
        var lead = record.LeadThread!.Value;
        Assert.True(rig.Core.Ask(lead, "r1", "git status"));
        Assert.Equal(1, rig.Agents.Resolutions.Count(r => r.Request == "r1"));
        var note = Assert.Single(rig.Agents.Notes);
        Assert.StartsWith("Dextra allowed this for you", note.Text, StringComparison.Ordinal);
        Assert.Equal(0, rig.Book.Count);
        Assert.Empty(rig.Told);
        Assert.DoesNotContain(rig.Delegated(), e => e.Result?.StartsWith("allowed", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void A_grant_the_owner_made_for_dextra_lets_the_delegate_answer_a_step_that_would_otherwise_ask()
    {
        using var rig = On();
        var chat = rig.Owner();
        var elsewhere = Path.Combine(Path.GetDirectoryName(rig.Project.Path)!, "notes", "today.txt");
        var write = new ApprovalRequest("w1", ApprovalRequest.Kind.FileChange, elsewhere, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline)]));
        Assert.True(rig.Controller.OnApproval(chat.Id, write));
        Assert.Empty(rig.Agents.Resolutions);                                              // no grant yet: a card
        var card = Assert.Single(rig.Queue.Waiting);

        // the owner grants it for Dextra from a mission's card, as the controller keeps grants for the assistant
        var grant = new Grant(Guid.NewGuid(), "dextra", ActionClass.WriteElsewhere, GrantTarget.Folder(Path.GetDirectoryName(elsewhere)!), GrantScope.Standing);
        rig.Controller.RestoreGrants(new ValueList<Grant>([grant]));
        rig.Controller.OnRequestResolved(chat.Id, "w1");
        Assert.True(rig.Controller.OnApproval(chat.Id, write with { Id = "w2" }));
        Assert.Equal((chat.Id, "w2", true), Assert.Single(rig.Agents.Resolutions));
        var line = rig.Delegated().Last();
        Assert.Equal(grant.Id.ToString("D"), line.AllowedBy);
        Assert.Equal(["approval:w2"], Assert.Single(rig.Controller.Grants).UsedBy);
        Assert.NotEqual(Guid.Empty, card.Id);
    }
}
