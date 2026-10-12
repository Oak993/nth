using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the chat's side of pill v2 (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1 "Resume after the turn ended",
// 10.2, 10.4 "Resolution", 10.7). The rows' words, the chip's words, the strip's line, the answer rows, and the continue
// message only when all four conditions hold.
[Trait("Decision", "ED-NEW-pcctl-27")]
public sealed class PcChatMirrorTests
{
    private const long Minute = 60_000;

    private static PcPendingAsk Ask(PcAskKind kind, object payload) =>
        new("a1", kind, "chat-1", payload, 0, Task.FromResult(new PcAskOutcome(PcAskStatus.Pending))) { App = "exe:c:\\office\\outlook.exe", AppName = "Outlook" };

    [Fact]
    public void A_resume_continues_the_chat_only_when_all_four_conditions_hold()
    {
        var ended = new PcTurnFacts(EndedAtMs: 1_000, Latest: true, UserWroteSince: false);

        Assert.Equal(PcResumeOutcome.Continue, PcChatMirror.Resume(true, ended, 1_000 + (9 * Minute)));
        Assert.Equal(PcResumeOutcome.AskToContinue, PcChatMirror.Resume(false, ended, 1_000 + Minute));
        Assert.Equal(PcResumeOutcome.AskToContinue, PcChatMirror.Resume(true, ended, 1_000 + (10 * Minute)));
        Assert.Equal(PcResumeOutcome.AskToContinue, PcChatMirror.Resume(true, ended with { UserWroteSince = true }, 1_000 + Minute));
        Assert.Equal(PcResumeOutcome.AskToContinue, PcChatMirror.Resume(true, ended with { Latest = false }, 1_000 + Minute));
        Assert.Equal(PcResumeOutcome.None, PcChatMirror.Resume(true, ended with { EndedAtMs = null }, 1_000 + Minute));
        Assert.Equal("Tactus was resumed in Excel. Continue the task.", PcChatMirror.ContinueMessage("Excel"));
        Assert.Equal("Resumed. Ask to continue.", PcChatMirror.AskToContinueLine);
    }

    [Fact]
    public void The_chip_and_the_rows_name_where_it_came_from()
    {
        Assert.Equal("Resumed from the pill", PcChatMirror.ResumedChip(PcPauseSource.Pill, "Ctrl+Alt+Space"));
        Assert.Equal("Resumed from the pill", PcChatMirror.ResumedChip(PcPauseSource.Row, null));
        Assert.Equal("Resumed from the chat", PcChatMirror.ResumedChip(PcPauseSource.Chat, null));
        Assert.Equal("Resumed with Ctrl+Alt+Space", PcChatMirror.ResumedChip(PcPauseSource.Chord, "Ctrl+Alt+Space"));
        Assert.Equal("Resumed from the tray", PcChatMirror.ResumedChip(PcPauseSource.Tray, null));

        Assert.Equal("Paused Excel (from the pill)", PcChatMirror.PausedRow("Excel", PcPauseSource.Pill, "Ctrl+Alt+Space"));
        Assert.Equal("Paused Excel (Ctrl+Alt+Space)", PcChatMirror.PausedRow("Excel", PcPauseSource.Chord, "Ctrl+Alt+Space"));
        Assert.Equal("Resumed (Ctrl+Alt+Space)", PcChatMirror.ResumedRow(PcPauseSource.Chord, "Ctrl+Alt+Space"));
        Assert.Equal("Resumed (from the chat)", PcChatMirror.ResumedRow(PcPauseSource.Chat, null));
    }

    [Fact]
    public void A_stop_row_says_its_path()
    {
        Assert.Equal("Stopped (Esc pressed 3 times)", PcChatMirror.StoppedRow(PcStopPath.EscapeThreeTimes, "Ctrl+Alt+Esc"));
        Assert.Equal("Stopped (Ctrl+Alt+Esc)", PcChatMirror.StoppedRow(PcStopPath.StopKey, "Ctrl+Alt+Esc"));
        Assert.Equal("Stopped (from the pill)", PcChatMirror.StoppedRow(PcStopPath.PillStop, null));
        Assert.Equal("Stopped Excel (from the pill)", PcChatMirror.StoppedRow(PcStopPath.PillRow, null, "Excel"));
        Assert.Equal("Stopped Excel (from the chat)", PcChatMirror.StoppedRow(PcStopPath.StripRow, null, "Excel"));
        Assert.Equal("Stopped (from the chat)", PcChatMirror.StoppedRow(PcStopPath.ChatStop, null));
        Assert.Equal("Stopped (from the tray)", PcChatMirror.StoppedRow(PcStopPath.Tray, null));
        Assert.Equal("Stopped (Tactus turned off in Settings)", PcChatMirror.StoppedRow(PcStopPath.MasterSwitch, null));
    }

    [Fact]
    public void An_answer_row_says_what_was_chosen_and_where_and_never_a_secret()
    {
        var approval = Ask(PcAskKind.Approval, new PcApprovalAsk(PcCapability.Act, false, true));
        Assert.Equal("You allowed Outlook for this chat (from the pill)", PcChatMirror.AnswerRow(approval, new PcApprovalReply(PcApproval.Chat, true), PcAnswerSource.Pill));
        Assert.Equal("You always allowed Outlook (from the pill)", PcChatMirror.AnswerRow(approval, PcApproval.Always, PcAnswerSource.Pill));
        Assert.Equal("You did not allow Outlook", PcChatMirror.AnswerRow(approval, PcApproval.Denied, PcAnswerSource.Chat));

        var confirm = Ask(PcAskKind.Confirm, new PcConfirmContent(PcConfirmCategory.Send, "send", "Send", "Outlook", []));
        Assert.Equal("You cancelled Send in Outlook (from the pill)", PcChatMirror.AnswerRow(confirm, new PcConfirmReply(false, "wrong person"), PcAnswerSource.Pill));
        Assert.Equal("You allowed Send in Outlook", PcChatMirror.AnswerRow(confirm, true, PcAnswerSource.Chat));

        var show = Ask(PcAskKind.ShowWindow, new PcShowAsk("Book1 - Excel"));
        Assert.Equal("You showed \u201CBook1 - Excel\u201D (from the pill)", PcChatMirror.AnswerRow(show, PcShowAnswer.Show, PcAnswerSource.Pill));
        Assert.Equal("You kept \u201CBook1 - Excel\u201D hidden (from the pill)", PcChatMirror.AnswerRow(show, PcShowAnswer.NotNow, PcAnswerSource.Pill));

        var question = Ask(PcAskKind.Question, new PcAskRequest("Which file?", ["March", "April"], true, null));
        Assert.Equal("You answered from the pill: Use the March file", PcChatMirror.AnswerRow(question, PcAnswer.Typed("Use the March file"), PcAnswerSource.Pill));
        Assert.Equal("You answered in the chat: April", PcChatMirror.AnswerRow(question, PcAnswer.Chose("April"), PcAnswerSource.Chat));
        var secret = Ask(PcAskKind.ProviderQuestion, new PcProviderAsk("Sign in", "Password", Secret: true));
        Assert.Equal("You answered in the chat", PcChatMirror.AnswerRow(secret, PcAnswer.Typed("hunter2"), PcAnswerSource.Chat));
        Assert.Null(PcChatMirror.AnswerRow(Ask(PcAskKind.ProviderApproval, new PcProviderAsk("Run a command", "npm test")), true, PcAnswerSource.Chat));
    }

    [Fact]
    public void The_strip_line_names_the_working_pausing_and_paused_apps()
    {
        Assert.Equal("Tactus. Using Excel and Outlook", PcChatMirror.StripLine([
            new PcStripApp("x", "Excel", PcStripAppState.Working), new PcStripApp("o", "Outlook", PcStripAppState.Working)]));
        Assert.Equal("Tactus. Using Outlook. Excel paused", PcChatMirror.StripLine([
            new PcStripApp("x", "Excel", PcStripAppState.Paused), new PcStripApp("o", "Outlook", PcStripAppState.Working)]));
        Assert.Equal("Tactus. Excel pausing. Word paused", PcChatMirror.StripLine([
            new PcStripApp("x", "Excel", PcStripAppState.Pausing), new PcStripApp("w", "Word", PcStripAppState.Paused)]));
        Assert.Equal("Excel, Outlook and Word", PcChatMirror.And(["Excel", "Outlook", "Word"]));
        Assert.Equal("Excel", PcChatMirror.NameOf("exe:c:\\office\\excel.exe"));
        Assert.Equal("Notepad", PcChatMirror.NameOf("aumid:microsoft.windowsnotepad_8wekyb3d8bbwe!notepad"));
    }
}
