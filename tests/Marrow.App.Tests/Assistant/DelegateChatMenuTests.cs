using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "off until the owner turns it on (first run, 2.6, or the chat's menu)"; ED-NEW-dextra-as22-9): a chat's own "Answer questions for
// me" in its menu. Offered only while Dextra runs, never on Dextra's own chats, a hidden chat or a head. Choosing it sets that chat's own choice, which beats the
// scope; choosing what the scope already says clears the choice again, so the list stays small.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-9")]
public sealed class DelegateChatMenuTests
{
    [Fact]
    public void The_toggle_is_offered_only_while_dextra_runs_and_never_on_its_own_chats()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var chat = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [chat]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.AssistantHost = DextraHosts.Make(model);
            var host = model.AssistantHost;
            Assert.Null(AssistantChats.DelegateAction(model, model.Thread(chat.Id)!));       // not set up: the upstream menu

            host.Settings.Update(p => p with { FirstRunDone = true });
            var action = AssistantChats.DelegateAction(model, model.Thread(chat.Id)!)!;
            Assert.Equal("Answer questions for me", action.Title);
            Assert.NotNull(action.Symbol);

            var home = host.HomeThread()!;
            Assert.Null(AssistantChats.DelegateAction(model, home));

            model.SetHiddenFromAssistant(chat.Id, true);
            Assert.Null(AssistantChats.DelegateAction(model, model.Thread(chat.Id)!));       // hidden from the Assistant: nothing for it to answer in
        });
    }

    [Fact]
    public void Choosing_it_turns_the_delegate_on_for_that_chat_and_choosing_it_again_goes_back_to_what_the_scope_says()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var chat = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [chat, other]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.AssistantHost = DextraHosts.Make(model);
            var host = model.AssistantHost;
            host.Settings.Update(p => p with { FirstRunDone = true });

            AssistantChats.DelegateAction(model, model.Thread(chat.Id)!)!.Action();
            Assert.True(DelegateReach.ChatChoice(host.Settings.Value, chat.Id));
            var stop = AssistantChats.DelegateAction(model, model.Thread(chat.Id)!)!;
            Assert.Equal("Stop answering for me", stop.Title);
            Assert.Equal("Answer questions for me", AssistantChats.DelegateAction(model, model.Thread(other.Id)!)!.Title);   // the other chat is untouched

            stop.Action();
            Assert.Null(DelegateReach.ChatChoice(host.Settings.Value, chat.Id));             // the scope (off) says what it says: no choice kept
            Assert.Empty(host.Settings.Value.DelegateChats);
        });
    }

    [Fact]
    public void With_the_delegate_on_everywhere_the_menu_offers_to_stop_and_a_stop_is_kept_for_that_chat_alone()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var chat = AppModelSamples.Thread(project);
        var other = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [chat, other]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.AssistantHost = DextraHosts.Make(model);
            var host = model.AssistantHost;
            host.Settings.Update(p => p with { FirstRunDone = true, Delegate = DelegateScope.Everywhere });

            var stop = AssistantChats.DelegateAction(model, model.Thread(chat.Id)!)!;
            Assert.Equal("Stop answering for me", stop.Title);
            stop.Action();
            Assert.False(DelegateReach.ChatChoice(host.Settings.Value, chat.Id));
            Assert.Equal("Answer questions for me", AssistantChats.DelegateAction(model, model.Thread(chat.Id)!)!.Title);
            Assert.Equal("Stop answering for me", AssistantChats.DelegateAction(model, model.Thread(other.Id)!)!.Title);

            AssistantChats.DelegateAction(model, model.Thread(chat.Id)!)!.Action();           // back to what the scope says
            Assert.Empty(host.Settings.Value.DelegateChats);
        });
    }

    [Fact]
    public void A_head_has_no_toggle_of_its_own()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        var head = AppModelSamples.Thread(project) with { ParentThreadID = lead.Id };
        h.WriteLibrary([project], [lead, head]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.AssistantHost = DextraHosts.Make(model);
            model.AssistantHost.Settings.Update(p => p with { FirstRunDone = true });
            Assert.NotNull(AssistantChats.DelegateAction(model, model.Thread(lead.Id)!));
            Assert.Null(AssistantChats.DelegateAction(model, model.Thread(head.Id)!));
        });
    }

    [Fact]
    public void Dextras_home_project_is_the_project_its_home_thread_works_in()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var chat = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [chat]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.AssistantHost = DextraHosts.Make(model);
            model.AssistantHost.Settings.Update(p => p with { FirstRunDone = true });
            Assert.Null(model.DextraHomeProject());
            var home = model.AssistantHost.HomeThread()!;
            Assert.Equal(home.ProjectID, model.DextraHomeProject());
        });
    }
}
