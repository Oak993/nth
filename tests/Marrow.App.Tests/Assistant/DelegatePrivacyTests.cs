using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 5.4 "Private by default across providers", 12.5 "secrets masked", 4.3; ED-NEW-dextra-as22-6, -10): the delegate reads a chat's words
// only where the filter lets its provider (another provider only when the owner allowed it, a local-model chat never reaches a cloud one, a hidden chat never),
// no secret goes to the model, and none reaches a log line, the book or the feed.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-6")]
public sealed class DelegatePrivacyTests
{
    private static readonly string[] Db = ["Postgres", "SQLite"];
    private const string Sentinel = "sk-SENTINELabcdefghijklmnop123456";

    private static DelegateRig On(ProviderKind model = ProviderKind.Claude)
    {
        var rig = new DelegateRig();
        rig.Everywhere();
        rig.Model.Provider = model;
        rig.Model.Reply = _ => FakeDelegateModel.Json("Postgres");
        return rig;
    }

    [Fact]
    public void A_delegate_on_another_provider_reads_a_chat_only_when_the_owner_allowed_it()
    {
        using var rig = On(ProviderKind.Codex);
        var chat = rig.Owner(provider: ProviderKind.Claude);
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        Assert.Empty(rig.Model.Prompts);
        Assert.Equal(DelegateWords.Reason(DelegateReason.Private), Assert.Single(rig.Queue.Waiting).Why);

        rig.Preferences = rig.Preferences with { ReadOtherProviders = true };
        rig.Ask(chat.Id, "Which database does the repo use?", Db, id: "r2");
        rig.Think();
        Assert.True(rig.Agents.Answers.Count == 1, rig.Why());
        Assert.Equal("r2", Assert.Single(rig.Agents.Answers).Request);
    }

    [Fact]
    public void A_delegate_on_the_chats_own_provider_reads_it_unless_the_owner_turned_the_same_provider_switch_off()
    {
        using var rig = On();
        var chat = rig.Owner(provider: ProviderKind.Claude);
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        rig.Think();
        Assert.Single(rig.Agents.Answers);

        rig.Preferences = rig.Preferences with { ReadSameProvider = false };
        rig.Ask(chat.Id, "Which database does the repo use?", Db, id: "r2");
        Assert.Equal(DelegateWords.Reason(DelegateReason.Private), Assert.Single(rig.Queue.Waiting).Why);
        Assert.Equal(1, rig.Model.Asked);
    }

    [Fact]
    public void A_local_model_chat_never_reaches_a_cloud_delegate_whatever_the_cross_provider_switch_says()
    {
        using var rig = On(ProviderKind.Claude);
        rig.Preferences = rig.Preferences with { ReadOtherProviders = true };
        var chat = rig.Owner(provider: ProviderKind.Local);
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        Assert.Empty(rig.Model.Prompts);
        Assert.Equal(DelegateWords.Reason(DelegateReason.Private), Assert.Single(rig.Queue.Waiting).Why);
    }

    [Fact]
    public void A_local_delegate_may_read_a_local_chat()
    {
        using var rig = On(ProviderKind.Local);
        var chat = rig.Owner(provider: ProviderKind.Local);
        rig.Ask(chat.Id, "Which database does the repo use?", Db);
        rig.Think();
        Assert.Single(rig.Agents.Answers);
    }

    [Fact]
    public void No_key_goes_to_the_model_from_the_decisions_the_past_answers_the_chat_or_the_question()
    {
        using var rig = On();
        var chat = rig.Owner();
        rig.Context.Decisions = "Deploy with api_key: " + Sentinel;
        rig.Context.PastAnswers = "Q: which key? | Answered: " + Sentinel;
        rig.Context.Turns.Add(new DelegateTurn(true, "the token is " + Sentinel));
        rig.Ask(chat.Id, "Which database does the repo use, given " + Sentinel + "?", Db);
        rig.Think();
        var prompt = Assert.Single(rig.Model.Prompts);
        Assert.DoesNotContain(Sentinel, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void No_key_reaches_a_log_line_the_book_or_the_feed_on_any_path()
    {
        using var rig = On();
        var chat = rig.Owner("Chat about " + Sentinel);
        // an answered question whose reasoning repeats the key
        rig.Model.Reply = _ => FakeDelegateModel.Json("Postgres", reason: "Because " + Sentinel + " is what the repo uses.");
        rig.Ask(chat.Id, "Which database does the repo use with " + Sentinel + "?", Db, id: "r1");
        rig.Think();
        // a question left to the owner, an approval that asks and one the engine allows (the cards show their exact inputs to the owner, as 4.2 says; the logs never do)
        rig.Ask(chat.Id, "Should I pay with " + Sentinel + "?", ["Yes", "No"], id: "r2");
        rig.Approve(chat.Id, "curl -X POST https://api.example.com/x -H \"Authorization: Bearer " + Sentinel + "\" -d @notes.txt", "a1");
        rig.Approve(chat.Id, "git status # " + Sentinel, "a2");

        var seen = new List<string>();
        foreach (var e in rig.Core.Logged())
            seen.Add(string.Join("|", new[] { e.Assistant, e.Mission, e.Agent, e.Tool, e.ActionClass, e.Target, e.Result, e.AllowedBy }.Concat(e.Inputs.Select(i => i.Name + "=" + i.Text))));
        foreach (var kept in rig.Book.Recent(50)) seen.Add(string.Join("|", kept.ChatTitle, kept.Question, kept.Answer, kept.By, kept.Reasoning));
        foreach (var told in rig.Told) seen.Add(told.Line);
        Assert.NotEmpty(seen);
        Assert.All(seen, text => Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal));
    }
}
