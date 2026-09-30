using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed class ThreadRuntimeDispatchHeadRig(AppModelHarness harness, AppModel model, ThreadRuntime runtime, ChatThread lead) : IDisposable
{
    public AppModelHarness Harness { get; } = harness;
    public AppModel Model { get; } = model;
    public ThreadRuntime Runtime { get; } = runtime;
    public ChatThread Lead { get; } = lead;
    public void Dispose() => Harness.Dispose();

    public bool Dispatch(FollowUpPrompt prompt, HydraHeadInfo.Origin origin) =>
        Harness.Main.OnMain(() => (bool)typeof(ThreadRuntime)
            .GetMethod("DispatchHead", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Runtime, [prompt, origin])!);

    public IReadOnlyList<ChatThread> Heads => Harness.Main.OnMain(() => Model.HydraHeads(Lead.Id).ToList());

    public void SetPhase(RuntimePhase phase) => Harness.Main.OnMain(() =>
        typeof(ThreadRuntime).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Runtime, phase));

    // The text of the brief the spawned head was sent, once its own start has run.
    public string Brief(ChatThread head)
    {
        string? text = null;
        Harness.Main.Loop.RunUntil(() =>
        {
            text = Model.ExistingRuntime(head.Id)?.Entries.Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>().Select(user => user.Value).FirstOrDefault(message => message.HydraBrief == true)?.Text;
            return text is not null;
        }, AppModelHarness.Bound);
        return text!;
    }
}
