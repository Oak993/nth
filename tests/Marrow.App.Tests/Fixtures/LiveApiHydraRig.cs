using Marrow.App.Runtime;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed class LiveApiHydraRig : IDisposable
{
    private readonly InProcessHttpHandler http = new();
    private readonly IDisposable transport;
    private readonly IDisposable reader;
    public LiveApiHydraRig()
    {
        http.Answer = _ => InProcessHttpHandler.Reply(200,
            "data: {\"choices\":[{\"delta\":{\"content\":\"Recorded response.\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
        transport = URLSession.Transport.Override(http);
        reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        Rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, hydraOn: true, install: false,
            configure: thread => thread with { Model = "offline-composer", Effort = "low" },
            settings: app => app.Settings.HydraEnabled = true);
        Rig.H.Main.OnMain(() =>
        {
            Rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            var source = Rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0];
            var next = Rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-next", next));
            Rig.App.Settings.Accounts.SetDefaults(Target, new() { Model = "offline-destination", Effort = "high", FastMode = true });
            Rig.App.Settings.AccountKeys.SetCap(source.Id, 0);
            Rig.App.Settings.AccountDecisions.SetSetting(DecisionKind.OnLimit, DecisionValues.Switch);
        });
        Api = LiveApiKeyLaunch.Ensure(Rig);
        Rig.Send("Keep the conversation on the selected account.");
        Rig.H.Main.Loop.RunUntil(() => Rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        Assert.Equal(Target, Rig.Thread.ProviderAccountID);
        Assert.Equal("Bearer offline-next", Assert.Single(http.Received).Header("Authorization"));
    }
    public ThreadRuntimeTurnRig Rig { get; }
    public OpenAICompatibleSession Api { get; }
    public Guid Target { get; private set; }
    public void Dispose()
    {
        Rig.H.Main.OnMain(() => Rig.Runtime.CancelPersistence());
        Rig.H.Main.Loop.Run(() => Rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Rig.Dispose();
        reader.Dispose();
        transport.Dispose();
        http.Dispose();
    }
}
