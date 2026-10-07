using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Core.Port.MultiAccount.Opinions;

namespace Marrow.App.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveOpinionBindingTests
{
    [Fact]
    public void Real_rounds_use_distinct_sent_entry_ids_and_capture_metadata_before_the_first_notification()
    {
        using var reader = Marrow.App.Runtime.MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            var source = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(source);
            var target = rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id;
            rig.App.Settings.Accounts.MarkSignedIn(target);
            rig.App.Settings.Accounts.Rename(target, "Historical checker");
            using var owner = new LiveThreadPorts(rig.App, decorateHeads: host => new OfflineOpinionHost(host));
            using var model = owner.AskOthers(rig.InitialThread.Id)!;
            var context = owner.Timeline(rig.InitialThread.Id)!;
            var seen = 0;
            owner.Opinions.Changed += thread =>
            {
                foreach (var round in owner.Opinions.Rounds(thread))
                {
                    Assert.NotNull(context.PromptEntry(round.Id));
                    Assert.NotNull(context.AccountsAtAsk!(round.Id));
                    seen++;
                }
            };
            model.Toggle(target);
            Assert.Equal(AskSendResult.Sent, model.SendAsync("Identical prompt").GetAwaiter().GetResult());
            var first = model.LastRound!.Value;
            owner.Opinions.CombineAsync(rig.InitialThread.Id, first).GetAwaiter().GetResult();
            var combined = context.CombinedEntry(first);
            Assert.NotNull(combined);
            Assert.Contains(rig.Runtime.Entries, entry => entry.Id == combined && entry.Item.TimelineItemContent is TimelineItem.Content.Assistant);
            Assert.Equal(owner.Opinions.Rounds(rig.InitialThread.Id).Single().Answers.Count + " answers", context.PostedByline!(first)!.Split("combined ")[1]);
            rig.App.Settings.Accounts.Rename(target, "Renamed checker");
            model.Toggle(target);
            Assert.Equal(AskSendResult.Sent, model.SendAsync("Identical prompt").GetAwaiter().GetResult());
            var second = model.LastRound!.Value;
            Assert.NotEqual(context.PromptEntry(first), context.PromptEntry(second));
            Assert.Equal("Historical checker", context.AccountsAtAsk!(first)!.Single(row => row.Id == target).Name);
            Assert.Equal("Renamed checker", context.AccountsAtAsk!(second)!.Single(row => row.Id == target).Name);
            Assert.Equal(2, rig.Runtime.Entries.Count(entry => entry.Item.TimelineItemContent is TimelineItem.Content.User));
            Assert.True(seen > 0);
            Assert.Empty(rig.Session.Inputs);
        });
    }

    [Fact]
    public void The_context_projects_real_head_errors_and_rejects_combine()
    {
        using var reader = Marrow.App.Runtime.MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            var target = rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id;
            rig.App.Settings.Accounts.MarkSignedIn(target);
            using var owner = new LiveThreadPorts(rig.App, decorateHeads: host => new OfflineOpinionHost(host) { FailedAccount = target });
            using var model = owner.AskOthers(rig.InitialThread.Id)!;
            model.Toggle(target);
            Assert.Equal(AskSendResult.Sent, model.SendAsync("Offline failure").GetAwaiter().GetResult());
            var round = model.LastRound!.Value;
            Assert.Equal("Offline head failed.", owner.Timeline(rig.InitialThread.Id)!.HeadError(round, target));
            Assert.Throws<InvalidOperationException>(() => owner.Opinions.CombineAsync(rig.InitialThread.Id, round).GetAwaiter().GetResult());
            Assert.Single(rig.Runtime.Entries);
        });
    }

    [Fact]
    public void Cross_service_confirmation_starts_unchecked_and_engine_decline_rolls_back_the_prompt()
    {
        using var reader = Marrow.App.Runtime.MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false);
        rig.H.Main.OnMain(() =>
        {
            rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            rig.App.Settings.MetaAPIKeyInput = "offline-target";
            var target = rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0].Id;
            var prompt = new DeclinePrompt();
            OfflineOpinionHost host = null!;
            using var owner = new LiveThreadPorts(rig.App, prompt, inner => host = new(inner));
            using var model = owner.AskOthers(rig.InitialThread.Id)!;
            model.Toggle(target);
            Assert.Equal(AskSendResult.NeedsConfirmation, model.SendAsync("Confirm before transfer").GetAwaiter().GetResult());
            Assert.False(model.Confirmation!.DontAskAgain);
            Assert.Empty(rig.Runtime.Entries);
            Assert.Throws<OperationCanceledException>(() => model.ConfirmAsync(true).GetAwaiter().GetResult());
            Assert.Single(prompt.Questions);
            Assert.Empty(host.Requests);
            Assert.Empty(rig.Runtime.Entries);
            Assert.Empty(owner.Opinions.Rounds(rig.InitialThread.Id));
            Assert.False(rig.App.Settings.ViewConversationConsent().Permits(ConsentPurpose.Opinions, ProviderKind.Meta));
            Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        });
    }
}
