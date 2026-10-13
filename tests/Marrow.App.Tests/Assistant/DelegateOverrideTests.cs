using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "Override ... is sent to the asking session as a correction and is saved as a past answer, so the delegate learns from it",
// 12.5 typed text as its length only; ED-NEW-dextra-as22-11): the owner's correction of an answer given for him. Checked, logged first, sent to the chat that
// asked as a message of his own, kept with the answer, and given to the delegate before anything else the next time the project asks.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-11")]
public sealed class DelegateOverrideTests
{
    private static readonly string[] Styles = ["Keep the existing chart style", "A new chart style"];

    private static (DelegateRig Rig, ChatThread Chat, DelegateRecord Answer) Answered(bool approval = false)
    {
        var rig = new DelegateRig();
        rig.Everywhere();
        var chat = rig.Owner();
        rig.Context.Decisions = "Decision: all reports keep the existing chart style so decks stay consistent.";
        rig.Model.Reply = _ => FakeDelegateModel.Json("Keep the existing chart style", 0.9, "decided", evidence: "all reports keep the existing chart style so decks stay consistent");
        if (approval) rig.Approve(chat.Id, "git status");
        else
        {
            rig.Ask(chat.Id, "Which chart style should the new report use?", Styles);
            rig.Think();
        }
        return (rig, chat, Assert.Single(rig.Book.Recent(1)));
    }

    [Fact]
    public void An_override_is_sent_to_the_chat_that_asked_as_a_correction_and_kept_with_the_answer()
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        var result = rig.Responder.Override(answer.Id, "Use the new chart style for the quarterly report.");
        rig.Agents.RunPosted();

        Assert.True(result.Saved);
        Assert.True(result.Sent);
        Assert.Null(result.Problem);
        var sent = Assert.Single(rig.Agents.Of(chat.Id).Sent);
        Assert.StartsWith("A correction from the owner.", sent, StringComparison.Ordinal);
        Assert.Contains("Which chart style should the new report use?", sent, StringComparison.Ordinal);
        Assert.Contains("“Keep the existing chart style”", sent, StringComparison.Ordinal);
        Assert.Contains("Use the new chart style for the quarterly report.", sent, StringComparison.Ordinal);
        var kept = rig.Book.Find(answer.Id)!;
        Assert.Equal("Use the new chart style for the quarterly report.", kept.Correction);
        Assert.True(kept.CorrectionSent);
        Assert.NotNull(kept.CorrectedAt);
    }

    [Fact]
    public void While_the_chats_turn_runs_the_correction_waits_and_goes_when_it_ends()
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        rig.Agents.Of(chat.Id).Running = true;
        var result = rig.Responder.Override(answer.Id, "Use the new chart style.");
        rig.Agents.RunPosted();
        Assert.True(result.Sent);
        Assert.Empty(rig.Agents.Of(chat.Id).Sent);
        rig.Agents.Of(chat.Id).Running = false;
        rig.Controller.OnTurnFinished(chat.Id, TurnStatus.Completed, continues: false);
        Assert.Contains("Use the new chart style.", Assert.Single(rig.Agents.Of(chat.Id).Sent), StringComparison.Ordinal);
    }

    [Fact]
    public void The_next_question_of_the_project_is_given_the_correction_before_anything_else()
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        rig.Responder.Override(answer.Id, "Use the new chart style for quarterly reports.");
        var past = rig.Book.Past(rig.Project.Id, 5);
        Assert.Equal(answer.Id, past[0].Id);
        Assert.Equal("Use the new chart style for quarterly reports.", past[0].Correction);
    }

    [Fact]
    public void An_approval_the_delegate_allowed_can_be_overridden_too()
    {
        var (rig, chat, answer) = Answered(approval: true);
        using var _ = rig;
        Assert.Equal(DelegateKind.Approval, answer.Kind);
        var result = rig.Responder.Override(answer.Id, "Do not run git commands in this chat without asking.");
        rig.Agents.RunPosted();
        Assert.True(result.Saved && result.Sent);
        Assert.Contains("Do not run git commands", Assert.Single(rig.Agents.Of(chat.Id).Sent), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_say_is_refused_and_nothing_is_sent_kept_or_logged(string text)
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        var before = rig.Delegated().Count;
        var result = rig.Responder.Override(answer.Id, text);
        Assert.False(result.Saved);
        Assert.Equal(DelegateResponder.NothingToSay, result.Problem);
        Assert.Empty(rig.Agents.Of(chat.Id).Sent);
        Assert.Null(rig.Book.Find(answer.Id)!.Correction);
        Assert.Equal(before, rig.Delegated().Count);
    }

    [Fact]
    public void Too_long_unknown_and_already_corrected_are_refused()
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        Assert.Equal(DelegateResponder.TooLong, rig.Responder.Override(answer.Id, new string('x', DelegateWords.CorrectionChars + 1)).Problem);
        Assert.Equal(DelegateResponder.NotOnRecord, rig.Responder.Override(Guid.NewGuid(), "anything").Problem);
        Assert.True(rig.Responder.Override(answer.Id, "Use the new chart style.").Saved);
        Assert.Equal(DelegateResponder.AlreadyCorrected, rig.Responder.Override(answer.Id, "And another thing.").Problem);
        rig.Agents.RunPosted();
        Assert.Single(rig.Agents.Of(chat.Id).Sent);
    }

    [Fact]
    public void A_chat_that_is_gone_cannot_be_told_but_the_correction_is_kept_for_next_time()
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        rig.Core.Chats.Remove(chat.Id);
        var result = rig.Responder.Override(answer.Id, "Use the new chart style.");
        Assert.True(result.Saved);
        Assert.False(result.Sent);
        Assert.False(rig.Book.Find(answer.Id)!.CorrectionSent);
        Assert.Equal("Use the new chart style.", rig.Book.Past(null, 1)[0].Correction);
    }

    [Fact]
    public void A_line_that_cannot_be_written_means_the_correction_is_neither_sent_nor_kept()
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        rig.Core.AuditFails = e => e.Tool == "delegate_override";
        var result = rig.Responder.Override(answer.Id, "Use the new chart style.");
        Assert.False(result.Saved);
        Assert.Equal(DelegateResponder.NotLoggedOverride, result.Problem);
        rig.Agents.RunPosted();
        Assert.Empty(rig.Agents.Of(chat.Id).Sent);
        Assert.Null(rig.Book.Find(answer.Id)!.Correction);
    }

    [Fact]
    public void His_words_are_logged_as_their_length_only_and_a_secret_in_them_is_masked_where_it_is_kept()
    {
        var (rig, chat, answer) = Answered();
        using var _ = rig;
        const string secret = "sk-abcdefghijklmnopqrstuvwxyz";
        rig.Responder.Override(answer.Id, "Use the new style, and the key is " + secret);
        var line = Assert.Single(rig.Core.Logged(), e => e.Tool == "delegate_override");
        Assert.Contains(line.Inputs, i => i.Name == "correction" && i.Kind == AuditInputKind.Typed && i.TypedLength > 0 && i.Text is null);
        Assert.DoesNotContain(secret, Everything(line), StringComparison.Ordinal);
        Assert.DoesNotContain("Use the new style", Everything(line), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, rig.Book.Find(answer.Id)!.Correction, StringComparison.Ordinal);
        Assert.Equal("delegate:" + answer.Id.ToString("D"), line.AllowedBy);
    }

    private static string Everything(AuditEntry e) =>
        string.Join("|", new[] { e.Assistant, e.Mission, e.Agent, e.Tool, e.ActionClass, e.Target, e.Result, e.AllowedBy }.Concat(e.Inputs.Select(i => i.Name + "=" + i.Text)));
}
