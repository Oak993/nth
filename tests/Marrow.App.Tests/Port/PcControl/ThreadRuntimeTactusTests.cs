using Marrow.App.Port.PcControl;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the chat mirror in the chat's runtime (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1, 10.4 "Resolution",
// 10.7). A Tactus row is a notice in the timeline; a resume with no turn of Tactus's still latest asks to continue; the
// chat's approvals, confirms and show-window asks are its badges and its pc_ask questions sit in the question sheet, whose
// answer goes to the broker and never to the provider; the provider's own questions are listed without them.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-27")]
public sealed class ThreadRuntimeTactusTests(HeadlessSession headless)
{
    private static PcPendingAsk Pending(string id, PcAskKind kind, object payload) =>
        new(id, kind, "chat-1", payload, 0, Task.FromResult(new PcAskOutcome(PcAskStatus.Pending))) { AppName = "Excel" };

    [Fact]
    public void Rows_are_notices_asks_are_badges_and_a_pc_ask_answer_goes_to_the_broker() => headless.OnUiThread(() =>
    {
        // the login environment loads off the UI thread first, as the other runtime tests on this harness do
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var answered = new List<(string Thread, string Id, PcAnswer Answer)>();
        void Record(string thread, string id, PcAnswer answer) => answered.Add((thread, id, answer));
        ThreadRuntime.TactusQuestionAnswered += Record;
        try
        {
            h.Main.OnMain(() =>
            {
                ITactusChat chat = h.Runtime;
                Assert.Equal(h.Runtime.ThreadID.ToString(), chat.Thread);
                chat.Row("Paused Excel (from the pill)");
                Assert.Contains(h.Runtime.Entries, e => e.Item.TimelineItemContent is TimelineItem.Content.Notice { Value.Message: "Paused Excel (from the pill)" });
                // no turn of Tactus's is the chat's latest: never a continue
                Assert.Equal(PcResumeOutcome.AskToContinue, chat.Resume(true, DateTimeOffset.Now));

                chat.Asks([
                    Pending("ask-1", PcAskKind.Approval, new PcApprovalAsk(PcCapability.Act, false, true)),
                    Pending("ask-2", PcAskKind.Question, new PcAskRequest("Which sheet?", ["Q1", "Q2"], false, null)),
                    Pending("ask-3", PcAskKind.ProviderApproval, new PcProviderAsk("Run this command?", "npm test")),
                ]);
                Assert.Equal(new[] { "ask-1" }, h.Runtime.TactusAsks.Select(a => a.Id));
                var sheet = Assert.Single(h.Runtime.Questions);
                Assert.Equal(ThreadRuntime.TactusQuestionPrefix + "ask-2", sheet.Id);
                Assert.Equal(new[] { "Q1", "Q2" }, sheet.Questions[0].Choices.Select(c => c.Label));
                Assert.False(sheet.Questions[0].AllowsOther);
                Assert.Empty(chat.ProviderQuestions);

                // the same asks again change nothing; the sheet's answer goes to the broker
                chat.Asks([Pending("ask-2", PcAskKind.Question, new PcAskRequest("Which sheet?", ["Q1", "Q2"], false, null))]);
                Assert.Same(sheet, Assert.Single(h.Runtime.Questions));
                Assert.Empty(h.Runtime.TactusAsks);
                h.Runtime.Answer(sheet, ValueMap<string, ValueList<string>>.Empty.Mutating(m => m[ThreadRuntime.TactusQuestionID] = new ValueList<string>(["Q2"])));
                Assert.Empty(h.Runtime.Questions);
            });
            Assert.Equal(new[] { (h.Runtime.ThreadID.ToString(), "ask-2", PcAnswer.Chose("Q2")) }, answered);
        }
        finally
        {
            ThreadRuntime.TactusQuestionAnswered -= Record;
        }
    });
}
