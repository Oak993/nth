using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.1, 4.3, 15.3; ED-NEW-dextra-as22-1, -8): the delegate as the app holds it. StartDextra binds a responder whose book is the store's
// delegate-answers.json; with the delegate off a real chat's approval stays the chat's own card; with it on, the real ThreadRuntime path (the provider event, the tap, the
// engine, Resolve) answers an approval the engine allows, labelled in the chat, and a question the words close goes to the queue, never to a model. No provider is called.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-1")]
public sealed class DelegateWiringTests
{
    private static AssistantController Start(ThreadRuntimeTurnRig rig, AssistantStore store, bool on)
    {
        rig.H.Main.OnMain(() =>
        {
            rig.App.AssistantHost = DextraHosts.Make(rig.App);
            rig.App.AssistantHost.Settings.Update(p => p with { FirstRunDone = true, Delegate = on ? DelegateScope.Everywhere : DelegateScope.Off });
        });
        var autoAnswer = AutoAnswerSwitch.AfterCorpus(() => DextraTaintRig.Pass);
        Assert.True(MissionRig.Within(autoAnswer.Ready));
        return rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => [new AssistantProfile("dextra", "Dextra")], autoAnswer: autoAnswer, allowlist: () => ["dotnet test"]));
    }

    private static ApprovalRequest Command(string id, string command) => MissionRig.Approval(id, command);

    [Fact]
    public void Start_dextra_binds_a_responder_whose_book_is_the_stores_and_survives_the_controller()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        Guid id;
        using (var store = AssistantStore.Open(temp.Roots))
        {
            var controller = Start(rig, store, on: false);
            try
            {
                var responder = Assert.IsType<DelegateResponder>(controller.Responder);
                Assert.Equal(0, responder.Book.Count);
                id = responder.Book.Add(new DelegateRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, rig.InitialThread.Id, "Pitch deck v3", DelegateKind.Question,
                    "Which chart style?", "Keep the existing chart style", "Dextra", "A decision settles it.")).Id;
            }
            finally
            {
                rig.H.Main.OnMain(rig.App.UnbindDextra);
                controller.Dispose();                                                              // writes what is pending
            }
            Assert.NotNull(store.DelegateAnswers());
        }
        using var again = AssistantStore.Open(temp.Roots);
        var next = Start(rig, again, on: false);
        try
        {
            Assert.Equal("Keep the existing chart style", next.Responder!.Book.Find(id)!.Answer);
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            next.Dispose();
        }
    }

    [Fact]
    public void With_the_delegate_off_a_real_chats_approval_stays_its_own_card()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var controller = Start(rig, store, on: false);
        try
        {
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Approval(Command("a1", "git status"))));
            Assert.Empty(rig.Session.ApprovalAnswers);
            Assert.Single(rig.H.Main.OnMain(() => rig.Runtime.Approvals));
            Assert.Empty(controller.Queue.Items);
            Assert.Equal(0, controller.Responder!.Book.Count);
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
    }

    [Fact]
    public void With_it_on_the_real_path_answers_an_approval_the_engine_allows_and_labels_it_in_the_chat()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var controller = Start(rig, store, on: true);
        try
        {
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Approval(Command("a1", "git status"))));
            var answer = Assert.Single(rig.Session.ApprovalAnswers);
            Assert.Equal(("a1", "allow"), (answer.Id, answer.Option));
            Assert.Empty(rig.H.Main.OnMain(() => rig.Runtime.Approvals));                          // the chat's own card went with the answer
            var notices = rig.H.Main.OnMain(() => rig.Runtime.Entries.Select(e => e.Item.TimelineItemContent).OfType<TimelineItem.Content.Notice>().Select(n => n.Value.Message).ToList());
            Assert.Contains(notices, n => n.StartsWith("Answered by Dextra for you: allowed git status.", StringComparison.Ordinal));
            var record = Assert.Single(controller.Responder!.Book.Recent(10));
            Assert.Equal(DelegateKind.Approval, record.Kind);
            Assert.Equal(rig.InitialThread.Id, record.Chat);

            // what the engine asks about stays the chat's own and gets a card in the queue as well
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Approval(Command("a2", "git push origin main"))));
            Assert.Single(rig.Session.ApprovalAnswers);
            Assert.Single(rig.H.Main.OnMain(() => rig.Runtime.Approvals));
            var card = Assert.Single(controller.Queue.Waiting);
            Assert.Equal(("a2", rig.InitialThread.Id), (card.RequestId, card.ThreadId));

            // the owner answers in the chat: the card goes
            rig.H.Main.OnMain(() => rig.Runtime.Resolve(rig.Runtime.Approvals[0], rig.Runtime.Approvals[0].Options.First(o => o.OptionRole == ApprovalRequest.Option.Role.Decline)));
            Assert.Equal(DecisionState.Withdrawn, controller.Queue.Find(card.Id)!.State);
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
    }

    [Fact]
    public void A_question_the_words_close_goes_to_the_queue_and_the_chats_own_answer_takes_it_away()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var controller = Start(rig, store, on: true);
        try
        {
            var question = new QuestionRequest("q-1", new ValueList<QuestionRequest.Question>([
                new("q", "Pay", "Should I pay for the Pro plan?", new ValueList<QuestionRequest.Choice>([new("Yes"), new("No")]), false, false, false)]));
            rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.Question(question)));
            var card = Assert.Single(controller.Queue.Waiting);
            Assert.Equal(DecisionKind.Question, card.Kind);
            Assert.Equal(DelegateWords.RuleLine(DelegateReason.Money), card.Rule);
            Assert.Single(rig.H.Main.OnMain(() => rig.Runtime.Questions));                         // the chat keeps its own question too
            Assert.Equal(0, controller.Responder!.Pending);

            rig.H.Main.OnMain(() => rig.Runtime.Answer(question, new ValueMap<string, ValueList<string>>(new Dictionary<string, ValueList<string>> { ["q"] = new(["No"]) })));
            Assert.Equal(DecisionState.Withdrawn, controller.Queue.Find(card.Id)!.State);
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
    }
}
