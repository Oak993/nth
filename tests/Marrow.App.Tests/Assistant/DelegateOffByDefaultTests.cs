using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "It is off until the owner turns it on", 16 "Ordering rule", 5.4 hidden chats; ED-NEW-dextra-as22-9): off by default. With
// the settings at their defaults the delegate takes no approval and no question, asks no model, raises no card, writes no line; it is not engaged for a chat
// the owner hid, for Dextra's own chats, or before the injection check has passed.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-9")]
public sealed class DelegateOffByDefaultTests
{
    private static void NothingHappened(DelegateRig rig)
    {
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Empty(rig.Agents.Answers);
        Assert.Empty(rig.Agents.Notes);
        Assert.Empty(rig.Queue.Items);
        Assert.Empty(rig.Model.Prompts);
        Assert.Empty(rig.Delegated());
        Assert.Empty(rig.Told);
        Assert.Equal(0, rig.Book.Count);
        Assert.Equal(0, rig.Responder.Pending);
    }

    [Fact]
    public void With_the_defaults_an_approval_and_a_question_stay_the_chats_own()
    {
        using var rig = new DelegateRig();
        var chat = rig.Owner();
        Assert.Equal(DextraPreferences.Default, rig.Preferences);
        Assert.False(rig.Approve(chat.Id, "git status"));
        Assert.False(rig.Ask(chat.Id, "Which database does the repo use?", ["Postgres", "SQLite"]));
        NothingHappened(rig);
    }

    [Fact]
    public void A_build_without_the_delegate_takes_nothing_either()
    {
        using var rig = new DextraTaintRig();
        var chat = MissionRig.NewChat(() => new ChatThread(rig.Project.Id, ProviderKind.Claude, "model-a", null, RuntimeMode.AutoAcceptEdits) { Title = "Pitch deck v3" });
        rig.Chats[chat.Id] = chat;
        Assert.Null(rig.Controller.Responder);
        Assert.False(rig.Ask(chat.Id, "r1", "git status"));
        Assert.False(rig.Controller.OnQuestion(chat.Id, new QuestionRequest("r2", ValueList<QuestionRequest.Question>.Empty)));
        Assert.Empty(rig.Agents.Resolutions);
    }

    [Fact]
    public void Turned_on_for_everywhere_it_takes_the_chat_and_turned_off_again_it_lets_go()
    {
        using var rig = new DelegateRig();
        var chat = rig.Owner();
        rig.Everywhere();
        Assert.True(rig.Approve(chat.Id, "git status", "a1"));
        Assert.Single(rig.Agents.Resolutions);
        rig.Off();
        Assert.False(rig.Approve(chat.Id, "git status", "a2"));
        Assert.Single(rig.Agents.Resolutions);
    }

    [Fact]
    public void A_chats_menu_turns_it_on_for_that_chat_alone()
    {
        using var rig = new DelegateRig();
        var chosen = rig.Owner("Chosen");
        var other = rig.Owner("Other");
        rig.Preferences = rig.Preferences with { DelegateChats = new ValueList<DelegateChatChoice>([new(chosen.Id, true)]) };
        Assert.True(rig.Approve(chosen.Id, "git status"));
        Assert.False(rig.Approve(other.Id, "git status"));
        Assert.Single(rig.Agents.Resolutions);
    }

    [Fact]
    public void This_project_only_takes_that_projects_chats_and_the_chat_menu_beats_it()
    {
        using var rig = new DelegateRig();
        var mine = rig.Owner("Mine");
        var elsewhere = rig.Owner("Elsewhere", project: Guid.NewGuid());
        rig.Preferences = rig.Preferences with { Delegate = DelegateScope.ThisProject, DelegateProject = rig.Project.Id };
        Assert.True(rig.Approve(mine.Id, "git status", "a1"));
        Assert.False(rig.Approve(elsewhere.Id, "git status", "a2"));
        rig.Preferences = rig.Preferences with { DelegateChats = new ValueList<DelegateChatChoice>([new(mine.Id, false)]) };
        Assert.False(rig.Approve(mine.Id, "git status", "a3"));
        Assert.Single(rig.Agents.Resolutions);
    }

    [Fact]
    public void A_chat_the_owner_hid_from_the_assistant_is_never_answered_in()
    {
        using var rig = new DelegateRig();
        var hidden = rig.Owner("Private plan", hidden: true);
        rig.Everywhere();
        Assert.False(rig.Approve(hidden.Id, "git status"));
        Assert.False(rig.Ask(hidden.Id, "Which database does the repo use?", ["Postgres", "SQLite"]));
        var head = rig.Owner("A head of the private plan", parent: hidden.Id);
        Assert.False(rig.Approve(head.Id, "git status", "a2"));          // a head of a hidden chat is hidden too (5.4)
        NothingHappened(rig);
    }

    [Fact]
    public void Dextras_own_chats_are_not_the_owners_to_delegate()
    {
        using var rig = new DelegateRig();
        rig.Everywhere();
        var home = rig.Core.Home();
        rig.Live(home);
        Assert.False(rig.Approve(home.Id, "git status"));
        Assert.False(rig.Ask(home.Id, "Which database?", ["Postgres", "SQLite"]));
        NothingHappened(rig);
    }

    [Fact]
    public void A_head_follows_the_chat_it_belongs_to()
    {
        using var rig = new DelegateRig();
        var lead = rig.Owner("Lead");
        var head = rig.Owner("Head", parent: lead.Id);
        rig.Preferences = rig.Preferences with { DelegateChats = new ValueList<DelegateChatChoice>([new(lead.Id, true)]) };
        Assert.True(rig.Approve(head.Id, "git status"));
        Assert.Equal(head.Id, Assert.Single(rig.Agents.Resolutions).Thread);
    }

    [Fact]
    public void Until_the_injection_checks_pass_it_answers_nothing_even_when_it_is_on()
    {
        using var rig = new DelegateRig(autoAnswer: false);
        var chat = rig.Owner();
        rig.Everywhere();
        Assert.False(rig.Approve(chat.Id, "git status"));
        Assert.False(rig.Ask(chat.Id, "Which database does the repo use?", ["Postgres", "SQLite"]));
        NothingHappened(rig);
    }

    [Fact]
    public void A_chat_nth_cannot_read_is_not_engaged_and_the_fault_is_not_thrown()
    {
        using var rig = new DelegateRig();
        var chat = rig.Owner();
        rig.Everywhere();
        rig.Core.ThreadFails = _ => true;
        Assert.False(rig.Approve(chat.Id, "git status"));
        Assert.Empty(rig.Agents.Resolutions);
    }
}
