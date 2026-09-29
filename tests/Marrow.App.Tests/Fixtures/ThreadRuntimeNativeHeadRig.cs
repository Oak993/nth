using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed class ThreadRuntimeNativeHeadRig(AppModelHarness harness, AppModel model, ThreadRuntime lead, ChatThread leadThread) : IDisposable
{
    public AppModelHarness Harness { get; } = harness;
    public AppModel Model { get; } = model;
    public ThreadRuntime Lead { get; } = lead;
    public ChatThread LeadThread { get; } = leadThread;
    public void Dispose() => Harness.Dispose();

    public void Feed(ProviderEvent @event) => Harness.Main.OnMain(() => Lead.Rehearse(@event));

    public IReadOnlyList<ChatThread> Heads => Harness.Main.OnMain(() => Model.HydraHeads(LeadThread.Id).ToList());

    public ChatThread Head(string nativeID) => Harness.Main.OnMain(() =>
        Model.HydraHeads(LeadThread.Id).Single(head => head.Hydra!.NativeID == nativeID));

    public ChatThread Fresh(ChatThread head) => Harness.Main.OnMain(() => Model.Thread(head.Id)!);

    public ThreadRuntime HeadRuntime(ChatThread head) => Harness.Main.OnMain(() => Model.ExistingRuntime(head.Id)!);

    // The head's rehearsed turn closes through FinishTurn, which is asynchronous: pump the loop until it is idle.
    public void WaitUntilIdle(ChatThread head)
    {
        var runtime = HeadRuntime(head);
        Harness.Main.Loop.RunUntil(() => !runtime.IsRunning, AppModelHarness.Bound);
    }

    public bool Registered(string nativeID) => Harness.Main.OnMain(() =>
        ((System.Collections.IDictionary)typeof(ThreadRuntime).GetField("hydraNativeHeads", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Lead)!).Contains(nativeID));
}
