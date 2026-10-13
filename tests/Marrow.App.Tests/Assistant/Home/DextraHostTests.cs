using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 2.1 mission chats stay out of the day groups, 2.2 the home thread, 2.6 nothing runs before the
// first-run card is done, 5.4 "Hide from the Assistant", 12.2 grants kept across launches; ED-NEW-dextra-as8-8): the app's one
// Dextra host starts the controller only when Dextra is on and set up, keeps its grants and brain switches in step, and stops
// everything when it is turned off.
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-8")]
public sealed class DextraHostTests
{
    private static Grant Login() =>
        new(Guid.NewGuid(), "dextra", ActionClass.Login, GrantTarget.Site("https://shop.example"), GrantScope.Standing) { Created = HomeSamples.Now };

    [Fact]
    public void Dextra_starts_only_when_it_is_on_and_its_first_run_is_done()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var starts = 0;
        using var host = DextraHosts.Make(model, starter: (_, _, _) => { starts++; return null; });

        h.Main.OnMain(() =>
        {
            Assert.True(host.Settings.Value.On);
            Assert.False(host.MayRun);
            Assert.Null(host.EnsureStarted());
            Assert.Equal(0, starts);

            host.Settings.Update(p => p with { FirstRunDone = true });
            Assert.True(host.MayRun);
            Assert.Equal(1, starts); // the card's Done starts it

            host.Settings.Update(p => p with { On = false });
            Assert.False(host.MayRun);
            Assert.Null(host.EnsureStarted());
            Assert.Equal(1, starts);
        });
    }

    [Fact]
    public void A_start_that_fails_says_why_and_lets_go_of_nothing_it_did_not_hold()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var host = DextraHosts.Make(model, starter: (_, _, _) => throw new InvalidOperationException("The audit key is missing."));
        var changes = 0;
        host.Changed += () => changes++;

        h.Main.OnMain(() =>
        {
            host.Settings.Update(p => p with { FirstRunDone = true });
            Assert.Null(host.Controller);
            Assert.Equal("The audit key is missing.", host.StartFault);
            Assert.Same(DextraLive.Idle, host.Live);
            Assert.True(changes >= 1);
        });
    }

    [Fact]
    public void A_started_controller_gets_the_kept_grants_and_the_brain_switches_and_turning_Dextra_off_stops_it()
    {
        using var h = new AppModelHarness();
        using var temp = new AssistantTempRoot();
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        using var store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted });
        var kept = Login();
        var defaults = new InMemorySettingsStore();
        new DextraSettings(defaults).Value = DextraPreferences.Default with { FirstRunDone = true, ReadOtherProviders = true, Grants = new ValueList<Grant>(new[] { kept }) };
        using var host = DextraHosts.Make(model, defaults, (app, _, id) => app.StartDextra(store, _ => { }, () => [new AssistantProfile(id.ToString("N"), "Dextra")]));

        h.Main.OnMain(() =>
        {
            var controller = host.EnsureStarted();
            Assert.NotNull(controller);
            Assert.Same(controller, host.Controller);
            Assert.Same(controller, host.EnsureStarted()); // one controller
            Assert.Contains(controller.Grants, g => g.Id == kept.Id);
            Assert.NotSame(DextraLive.Idle, host.Live);
            Assert.True(model.AssistantEvents.Brain.AlsoOtherProviders);
            Assert.Equal("Dextra", host.NameOf(host.AssistantId));

            // a revoke from Settings › Assistant is kept across launches
            Assert.True(controller.RevokeGrant(kept.Id, HomeSamples.Now));
            Assert.Empty(host.Settings.Value.Grants);

            host.Settings.Update(p => p with { On = false });
            Assert.Null(host.Controller);
            Assert.Same(DextraLive.Idle, host.Live);
            Assert.Equal(FeedBrain.Unknown, model.AssistantEvents.Brain);
            Assert.Null(model.Dextra);
        });
    }

    [Fact]
    public void The_home_thread_is_made_once_in_the_first_project_and_stays_out_of_the_day_groups()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var chat = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [chat]);
        var model = h.MakeModel();
        using var host = DextraHosts.Make(model);

        h.Main.OnMain(() =>
        {
            var folder = h.NewFolder("dextra-home");
            var home = host.HomeThread(() => folder)!;
            Assert.Equal("Dextra", home.Title);
            Assert.True(home.HasCustomTitle);
            Assert.Equal(host.Id, home.AssistantID);
            Assert.Null(home.MissionID);
            Assert.Equal(project.Id, home.ProjectID);
            Assert.Equal(SealedProviderConfig.CappedMode, home.RuntimeMode);
            Assert.Equal(folder, home.WorktreePath);

            Assert.Equal(home.Id, host.HomeThread()!.Id);
            Assert.Single(model.Threads, t => t.AssistantID == host.Id);
            Assert.True(AssistantChats.IsAssistants(home));
            Assert.Equal([chat.Id], AssistantChats.Listed(model.Threads).Select(t => t.Id));
            var plain = new[] { model.Thread(chat.Id)! };
            Assert.Same(plain, AssistantChats.Listed(plain)); // nothing of the Assistant's: the same list, no copy
        });
    }

    [Fact]
    public void With_no_project_there_is_no_home_thread_yet()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var host = DextraHosts.Make(model);
        h.Main.OnMain(() => Assert.Null(host.HomeThread()));
    }

    [Fact]
    public void A_chat_hides_from_the_Assistant_only_while_Dextra_runs_and_never_its_own_chats()
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
            Assert.Null(AssistantChats.HideAction(model, model.Thread(chat.Id)!)); // not set up: the upstream menu

            host.Settings.Update(p => p with { FirstRunDone = true });
            var hide = AssistantChats.HideAction(model, model.Thread(chat.Id)!)!;
            Assert.Equal("Hide from the Assistant", hide.Title);
            Assert.Equal("eye.slash", hide.Symbol);
            hide.Action();
            Assert.True(model.Thread(chat.Id)!.HiddenFromAssistant);
            var show = AssistantChats.HideAction(model, model.Thread(chat.Id)!)!;
            Assert.Equal("Show to the Assistant", show.Title);
            show.Action();
            Assert.False(model.Thread(chat.Id)!.HiddenFromAssistant);

            var home = host.HomeThread()!;
            Assert.Null(AssistantChats.HideAction(model, home));
            model.SetHiddenFromAssistant(home.Id, true);
            Assert.False(model.Thread(home.Id)!.HiddenFromAssistant);
        });
    }

    [Fact]
    public void Opening_the_home_clears_the_selected_chat_and_picking_a_chat_leaves_the_home()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var chat = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [chat]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = chat.Id;
            model.ShowAssistantHome();
            Assert.True(model.AssistantHomeShown);
            Assert.Null(model.SelectedThreadID);

            model.SelectedThreadID = chat.Id;
            Assert.False(model.AssistantHomeShown);
            Assert.Equal(chat.Id, model.SelectedThreadID);
        });
    }
}
