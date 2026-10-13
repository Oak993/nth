using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "listed in the digest", 6 "Answered for you: 2 questions in “Pitch deck v3”, each with Override"; ED-NEW-dextra-as22-8): the morning digest
// is told what the delegate answered since the digest's own last time: the questions, counted per chat, each leading to its chat. The seam AS-11 left (DigestSetup.Answered) is
// given the time the digest tells from, because only the digest knows it.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-8")]
public sealed class DelegateDigestTests
{
    [Fact]
    public void The_digest_setup_asks_the_delegates_book_for_the_questions_since_and_each_item_leads_to_its_chat()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        rig.H.Main.OnMain(() => rig.App.AssistantHost = DextraHosts.Make(rig.App));
        var controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => [new AssistantProfile("dextra", "Dextra")]));
        try
        {
            var now = DateTimeOffset.UtcNow;
            var chat = rig.InitialThread.Id;
            var book = controller.Responder!.Book;
            book.Add(new DelegateRecord(Guid.NewGuid(), now.AddHours(-30), chat, "Pitch deck v3", DelegateKind.Question, "Which chart style?", "Keep it", "Dextra", "A decision."));
            book.Add(new DelegateRecord(Guid.NewGuid(), now.AddHours(-3), chat, "Pitch deck v3", DelegateKind.Question, "Which file name?", "report.md", "Dextra", "A decision."));
            book.Add(new DelegateRecord(Guid.NewGuid(), now.AddHours(-2), chat, "Pitch deck v3", DelegateKind.Question, "Which font?", "Inter", "Dextra", "A decision."));
            book.Add(new DelegateRecord(Guid.NewGuid(), now.AddHours(-1), chat, "Pitch deck v3", DelegateKind.Approval, "Run this command? git status", "Allowed", "Dextra", "Runs alone."));

            var setup = rig.App.DigestSetupOf(controller);
            var told = Assert.Single(setup.AnsweredSince!(now.AddHours(-24)));
            Assert.Equal("Pitch deck v3", told.Thread);
            Assert.Equal(2, told.Count);                                                          // the older one was told in the last digest, the approval is not a question
            Assert.Equal(new DigestLink(DigestLinkKind.Thread, chat.ToString("D")), told.Link);
            Assert.Empty(setup.AnsweredSince!(now));
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
    }

    [Fact]
    public void With_no_delegate_in_the_build_there_is_nothing_to_tell()
    {
        using var rig = new DextraTaintRig();
        using var app = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        var setup = app.App.DigestSetupOf(rig.Controller);
        Assert.Empty(setup.AnsweredSince!(DateTimeOffset.UtcNow.AddDays(-1)));
    }
}
