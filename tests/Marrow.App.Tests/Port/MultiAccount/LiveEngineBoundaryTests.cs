using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveEngineBoundaryTests
{
    [Fact]
    public void Pins_use_the_real_thread_boundary_and_retired_heads_never_reuse_a_revision()
    {
        using var rig = new ManualAccountPinRig();
        rig.Rig.H.Main.OnMain(() =>
        {
            using var owner = new LiveThreadPorts(rig.Rig.App);
            var thread = rig.Rig.InitialThread.Id;
            var port = owner.Ports.Account(thread, ProviderKind.Deepseek)!;
            var before = owner.Revision(thread);
            Assert.Equal(PinResult.Pinned, port.PinAsync(rig.Target).GetAwaiter().GetResult());
            Assert.Equal(rig.Target, port.PinnedAccountId);
            Assert.Equal(rig.Target, rig.Rig.App.Thread(thread)!.ProviderAccountPinID);
            Assert.Equal(rig.Source, port.ActiveAccountId);
            port.BackToOrder();
            Assert.Null(rig.Rig.App.Thread(thread)!.ProviderAccountPinID);
            Assert.Empty(rig.Http.Received);
            owner.Detach(thread);
            Assert.Equal(-1, owner.Revision(thread));
            owner.Ports.Account(thread, ProviderKind.Deepseek);
            Assert.True(owner.Revision(thread) > before);
        });
    }

    [Fact]
    public void Combined_opinions_append_once_to_the_real_timeline_without_changing_the_route()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var before = rig.App.Thread(rig.InitialThread.Id);
            var round = Guid.NewGuid();
            rig.Runtime.AppendCombinedOpinion(round, "The combined answer.", "Combined by Source", default);
            rig.Runtime.AppendCombinedOpinion(round, "The combined answer.", "Combined by Source", default);
            var item = Assert.Single(rig.Runtime.Entries);
            Assert.Equal("The combined answer.\n\nCombined by Source", Assert.IsType<TimelineItem.Content.Assistant>(item.Item.TimelineItemContent).Value.Text);
            Assert.Equal(before!.ProviderAccountID, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountID);
            Assert.Equal(before.ProviderAccountPinID, rig.App.Thread(rig.InitialThread.Id)!.ProviderAccountPinID);
            Assert.Empty(rig.Session.Inputs);
        });
    }

    [Fact]
    public void The_live_card_port_forwards_Ask_and_cancellation_to_the_real_routing_engine()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        LiveThreadPorts owner = null!;
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id);
            owner = new(rig.App);
            Assert.True(rig.App.AccountRouting.NoteLimit(rig.Runtime, null, rig.H.Main.Time.GetUtcNow().AddHours(1)));
        });
        try
        {
            rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
            Assert.True(owner.Ports.Cards!.State(rig.InitialThread.Id)!.Request!.FirstTime);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var refused = rig.H.Main.OnMain(() => owner.Ports.Cards.AnswerAsync(rig.InitialThread.Id,
                [new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.AllThreads, cancellation.Token));
            Assert.Throws<OperationCanceledException>(() => rig.H.Main.Loop.Run(() => refused, AppModelHarness.Bound));
            Assert.Null(rig.App.Settings.AccountDecisions.GetSetting(DecisionKind.OnLimit));
            var answer = rig.H.Main.OnMain(() => owner.Ports.Cards.AnswerAsync(rig.InitialThread.Id,
                [new(DecisionKind.OnLimit, DecisionValues.Ask)], RememberScope.ThisThread));
            rig.H.Main.Loop.Run(() => answer, AppModelHarness.Bound);
            Assert.False(owner.Ports.Cards.State(rig.InitialThread.Id)!.Request!.FirstTime);
            Assert.Equal(DecisionValues.Ask, rig.App.Settings.AccountDecisions.GetThread(rig.InitialThread.Id, DecisionKind.OnLimit));
            Assert.False(rig.App.Settings.AccountHandshakeEnabled);
            Assert.Empty(rig.Session.Inputs);
        }
        finally { rig.H.Main.OnMain(owner.Dispose); }
    }

    [Fact]
    public void Cross_service_opinions_require_the_real_confirmation_before_any_head_or_context_is_launched()
    {
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        var prompt = new DeclinePrompt(); LiveThreadPorts owner = null!; Guid target = default;
        using var launches = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) => throw new InvalidOperationException("No head may launch without confirmation."));
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            rig.App.Settings.MetaAPIKeyInput = "offline-target";
            target = rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0].Id;
            owner = new(rig.App, prompt);
        });
        try
        {
            var ask = rig.H.Main.OnMain(() => owner.Opinions.AskAsync(rig.InitialThread.Id, "Check this one message.", [target]));
            Assert.Throws<OperationCanceledException>(() => rig.H.Main.Loop.Run(() => ask, AppModelHarness.Bound));
            var question = Assert.Single(prompt.Questions);
            Assert.Equal(ProviderKind.Deepseek, question.Source);
            Assert.Equal([ProviderKind.Meta], question.Destinations);
            Assert.False(question.DontAskAgain);
            Assert.Empty(owner.Opinions.Rounds(rig.InitialThread.Id));
            Assert.False(rig.App.Settings.AccountHandshakeEnabled);
            rig.H.Main.OnMain(() =>
            {
                rig.App.Settings.AccountKeys.SetCap(target, 0);
                Assert.False(owner.HeadAccounts(rig.InitialThread.Id).Single(account => account.Id == target).HasRoom);
                Assert.Equal(0, owner.HeadAccounts(rig.InitialThread.Id).Single(account => account.Id == target).MonthlyCapUsd);
            });
        }
        finally { rig.H.Main.OnMain(owner.Dispose); }
    }

}
