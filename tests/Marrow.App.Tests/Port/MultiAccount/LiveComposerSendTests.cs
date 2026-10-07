using System.Reflection;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.MultiAccount;

[Collection("Headless")]
[Trait("Rule", "R-TEST-4")]
public sealed class LiveComposerSendTests(HeadlessSession headless)
{
    [Fact]
    public void The_composer_send_uses_the_opinion_engine_and_clears_only_the_sent_draft()
    {
        headless.OnUiThread(() =>
        {
            using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
            using var rig = new ThreadRuntimeTurnRig();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            rig.App.Settings.Accounts.MarkSignedIn(rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id);
            var other = rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id;
            rig.App.Settings.Accounts.MarkSignedIn(other);
            using var owner = new LiveThreadPorts(rig.App, decorateHeads: host => new OfflineOpinionHost(host));
            using var model = owner.AskOthers(rig.InitialThread.Id)!;
            using var offer = AskOthersComposerSeams.Offer.Override(_ => model);
            var composer = new ComposerView(rig.Runtime, rig.Directory, takesFocusOnAppear: false);
            var view = (AskOthersView)typeof(ComposerView).GetField("askOthers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(composer)!;
            var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.Sent += (_, _) => sent.TrySetResult();
            model.Toggle(other);
            rig.Runtime.Draft = new ComposerDraft { Text = "Selected opinion send" };
            typeof(ComposerView).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(composer, null);
            HeadlessSession.RunDispatcherUntil(sent.Task, AppModelHarness.Bound);
            Assert.Equal("Selected opinion send", Assert.Single(owner.Opinions.Rounds(rig.InitialThread.Id)).Message);
            Assert.Equal("Selected opinion send", Assert.IsType<TimelineItem.Content.User>(Assert.Single(rig.Runtime.Entries).Item.TimelineItemContent).Value.Text);
            Assert.True(rig.Runtime.Draft.IsEmpty);
            Assert.Empty(rig.Session.Inputs);
        });
    }
}
