using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class PrivateHandshakeCostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Private_briefing_and_readback_use_the_same_throttled_account_reporter(bool incoming)
    {
        var percent = 20.0;
        var reads = 0;
        using var reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) =>
        {
            reads++;
            return Task.FromResult<PlanLimits?>(new() { PlanName = "Pro", Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = percent, DurationMinutes = 300 }]) });
        });
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, settings: app =>
        {
            app.Settings.DeepseekAPIKeyInput = "offline-cost-source";
            app.Settings.MetaAPIKeyInput = "offline-cost-destination";
        });
        IDisposable view = null!;
        Guid source = default, target = default;
        rig.H.Main.OnMain(() =>
        {
            source = rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            target = incoming ? rig.App.Settings.Accounts.ForService(ProviderKind.Meta)[0].Id : source;
            view = rig.App.AccountRouting.ObserveReports();
        });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(20, rig.H.Main.OnMain(() => rig.App.AccountRouting.Snapshot(target)!.Limits!.Windows[0].Percent));
        var initialReads = reads;
        var privateSession = new ThreadRuntimeScriptedSession();
        rig.H.Main.Loop.Run(async () =>
        {
            var summaries = new HandshakeSummaryAdapter((id, ct) => rig.Runtime.BuildHandshakeSourceAsync(ct));
            var host = new ThreadAccountHost(rig.App, rig.Runtime, rig.App.Settings.Pills, summaries, (configuration, id) =>
            {
                Assert.Equal(target, id);
                return new(configuration, privateSession);
            });
            privateSession.SendScript = input =>
            {
                Assert.True(input.IsFinalReport);
                rig.H.Main.Time.Advance(TimeSpan.FromSeconds(1));
                privateSession.Emit(new ProviderEvent.MessageCompleted("brief", "Actual private context acknowledgement."));
                privateSession.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
                return Task.CompletedTask;
            };
            var summary = await summaries.BuildAsync(rig.InitialThread.Id, CancellationToken.None);
            if (incoming)
            {
                var row = rig.App.Settings.Accounts.Get(target);
                await using var standby = await host.StartStandbyAsync(new(new(target.ToString("D"), row.Service.Id, "Next")), CancellationToken.None);
                await standby.ReceiveAsync(new(Bring.MarrowSummary, summary, null), true, CancellationToken.None);
            }
            else Assert.NotNull(await host.RequestBriefingAsync(summary, CancellationToken.None));
        }, AppModelHarness.Bound);
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.Equal(initialReads, reads);
        var limits = rig.H.Main.OnMain(() => (HandshakeLimits)typeof(MultiAccountRouting).GetField("limits", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.App.AccountRouting)!);
        Assert.Null(rig.H.Main.OnMain(() => limits.HandshakeCostPercent(target.ToString("D"))));
        percent = 24;
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromMinutes(1)));
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Assert.True(reads > initialReads);
        Assert.Equal(4, rig.H.Main.OnMain(() => limits.HandshakeCostPercent(target.ToString("D"))));
        if (incoming) Assert.Null(rig.H.Main.OnMain(() => limits.HandshakeCostPercent(source.ToString("D"))));
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Empty(rig.Session.Inputs);
        Assert.False(rig.App.Settings.AccountHandshakeEnabled);
        rig.H.Main.OnMain(() => { view.Dispose(); rig.Runtime.CancelPersistence(); });
        rig.H.Main.Loop.Run(() => rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    }
}
