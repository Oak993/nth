using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed class ManualAccountPinRig : IDisposable
{
    private readonly IDisposable transport;
    private readonly IDisposable reader;
    public ManualAccountPinRig()
    {
        Http = new();
        Http.Answer = _ => InProcessHttpHandler.Reply(200,
            "data: {\"choices\":[{\"delta\":{\"content\":\"Prior answer stays.\"}}]}\n\ndata: [DONE]\n\n", "text/event-stream");
        transport = URLSession.Transport.Override(Http);
        reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) => Task.FromResult<PlanLimits?>(null));
        Rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false,
            configure: thread => thread with { Model = "source-model", Effort = "low" });
        Rig.H.Main.OnMain(() =>
        {
            Rig.App.Settings.DeepseekAPIKeyInput = "offline-source";
            Source = Rig.App.Settings.Accounts.ForService(ProviderKind.Deepseek)[0].Id;
            var next = Rig.App.Settings.Accounts.Add(ProviderKind.Deepseek);
            Target = next.Id;
            Assert.True(APIKeychain.SetKey("offline-next", next));
            Rig.App.Settings.Accounts.SetDefaults(Target, new() { Model = "target-model", Effort = "high" });
        });
        Api = LiveApiKeyLaunch.Ensure(Rig);
    }
    public InProcessHttpHandler Http { get; }
    public ThreadRuntimeTurnRig Rig { get; }
    public OpenAICompatibleSession Api { get; }
    public Guid Source { get; private set; }
    public Guid Target { get; private set; }
    public bool Pin(Guid? target)
    {
        var method = typeof(AppModel).GetMethod("TryPinThreadAccount", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return Rig.H.Main.OnMain(() => Assert.IsType<bool>(method.Invoke(Rig.App, [Rig.InitialThread.Id, target])));
    }
    public void WaitForWrites()
    {
        Rig.H.Main.Advance(TimeSpan.FromSeconds(5));
        typeof(AppModelHarness).GetMethod("FinishDiskWrites", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Rig.H, null);
    }
    public void Send(string text)
    {
        Rig.Send(text);
        Rig.H.Main.Loop.RunUntil(() => Rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
    }
    public void Dispose()
    {
        Rig.H.Main.OnMain(() => Rig.Runtime.CancelPersistence());
        Rig.H.Main.Loop.Run(() => Rig.App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
        Rig.Dispose(); reader.Dispose(); transport.Dispose(); Http.Dispose();
    }
}
