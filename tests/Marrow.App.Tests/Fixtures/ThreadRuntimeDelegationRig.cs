using System.Collections;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>What the lead runtime holds for a block it is sending out (ThreadRuntime.swift:301, hydraStream).</summary>
public sealed record DelegationStreamState(Guid TurnID, string EntryID, Guid BatchID, long Sent);

/// <summary>One task waiting for the pair cap to make room (ThreadRuntime.swift:287, hydraWaiting).</summary>
public sealed record WaitingTask(HydraDelegation Delegation, Guid BatchID, DateTimeOffset QueuedAt);

/// <summary>One open delegation batch (ThreadRuntime.swift:354-357, HydraBatch).</summary>
public sealed record OpenBatch(Guid Id, Guid[] Pending, HydraReport[] Reports);

/// <summary>A Hydra lead (a plain thread with Hydra on, no pair) over a real AppModel, for the delegation paths of
/// ThreadRuntime: the reply rows a turn left, spawnDelegatedHeads, dropDelegationBlock and spawnWaitingHeads. Heads spawn
/// as real head records; their own sessions only start when the main loop is pumped, which these tests do not do (and a
/// provider with no executable fails closed if they did). ED-119: the private state is read by reflection.</summary>
public sealed class ThreadRuntimeDelegationRig : IDisposable
{
    public ThreadRuntimeDelegationRig(long? maxHeads = null, int busyHeads = 0, ProviderKind provider = ProviderKind.Codex, Action<AppModel>? settings = null)
    {
        H = new AppModelHarness();
        Project = AppModelSamples.Project("Project", H.NewFolder("project"));
        var lead = AppModelSamples.Thread(Project) with { Provider = provider, HydraEnabled = true };
        var records = new List<ChatThread> { lead };
        for (var index = 0; index < busyHeads; index++)
            records.Add(AppModelSamples.Thread(Project) with
            {
                ParentThreadID = lead.Id,
                IsInPanel = true,
                Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with { Index = 20 + index },
            });
        Lead = lead;
        var busy = records.Skip(1).Select(head => head.Id).ToArray();
        H.WriteLibrary([Project], records);
        App = H.MakeModel();
        H.Main.OnMain(() =>
        {
            App.Settings.HydraEnabled = true;
            App.Settings.HydraIsolateHeads = false;
            App.Settings.HydraMaxHeads = maxHeads;
            settings?.Invoke(App);
            // A head recorded as running is found stopped when the library loads; running again is what a live one is.
            foreach (var id in busy)
                App.UpdateThread(id, head => head with { Hydra = head.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
        });
        H.PrepareBootstrap(App);
        Runtime = H.Main.OnMain(() => App.Runtime(lead.Id));
        H.Main.Loop.Run(() => Runtime.EnsureLoaded(), AppModelHarness.Bound);
    }

    public AppModelHarness H { get; }
    public Project Project { get; }
    public ChatThread Lead { get; }
    public AppModel App { get; }
    public ThreadRuntime Runtime { get; }

    public T OnMain<T>(Func<T> action) => H.Main.OnMain(action);
    public void OnMain(Action action) => H.Main.OnMain(action);

    /// <summary>The user's turn begins (a rehearsed turn: no session, no checkpoint); returns its id.</summary>
    public Guid BeginTurn(string text = "Delegate the work.") => OnMain(() =>
    {
        Runtime.RehearseTurn(text);
        return Runtime.Turns.Last().Id;
    });

    /// <summary>A reply row lands in the current turn while nothing reads it as it streams (the lead has no Hydra at
    /// that moment), so the block it holds is first met at the end of the turn.</summary>
    public void Reply(string id, string text)
    {
        SetLeadHydra(false);
        Delta(id, text);
        SetLeadHydra(true);
    }

    /// <summary>A reply delta arrives and flushes the way the app reads a streaming reply: a block in it is read as it goes.</summary>
    public void Delta(string id, string text) => OnMain(() =>
    {
        Runtime.Rehearse(new ProviderEvent.MessageDelta(id, text));
        ThreadRuntimeReflection.Call<object>(Runtime, "FlushDeltas", false);
    });

    /// <summary>The turn is over as finishTurn leaves things when it asks about heads: the lead idle, no turn current.</summary>
    public void EndTurn() => OnMain(() =>
    {
        ThreadRuntimeReflection.Set(Runtime, "phase", RuntimePhase.Idle);
        ThreadRuntimeReflection.Set(Runtime, "currentTurnID", null);
    });

    public void SetLeadHydra(bool on) => OnMain(() =>
        App.UpdateThread(Lead.Id, thread => thread with { HydraEnabled = on }));

    public void SpawnWaitingHeads(HydraLaunch? launch = null) => OnMain(() =>
        ThreadRuntimeReflection.Call<object>(Runtime, "SpawnWaitingHeads", new object?[] { launch }));

    /// <summary>The chat enters <paramref name="pair"/> (the pair is added to the settings and the thread names it).</summary>
    public void UsePair(HydraPair pair) => OnMain(() =>
    {
        App.Settings.AddHydraPair(pair);
        // A worker provider that is switched off cannot run the pair's heads: they fall back to the lead's own provider.
        if (pair.WorkerProvider is { } worker) App.Settings.SetEnabled(false, worker);
        App.UpdateThread(Lead.Id, thread => thread with { HydraPairID = pair.Id });
    });

    /// <summary>The brief a head was sent, once its own start has run (the loop is pumped for it).</summary>
    public string Brief(ChatThread head)
    {
        string? text = null;
        H.Main.Loop.RunUntil(() =>
        {
            text = App.ExistingRuntime(head.Id)?.Entries.Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>().Select(user => user.Value).FirstOrDefault(message => message.HydraBrief == true)?.Text;
            return text is not null;
        }, AppModelHarness.Bound);
        return text!;
    }

    public string SpawnDelegatedHeads(Guid turn) => OnMain(() =>
        ThreadRuntimeReflection.Call<object>(Runtime, "SpawnDelegatedHeads", turn)!.ToString()!);

    public void DropDelegationBlock(Guid turn, TurnStatus status) => OnMain(() =>
        ThreadRuntimeReflection.Call<object>(Runtime, "DropDelegationBlock", turn, status));

    public string Text(string id) => OnMain(() =>
        Runtime.Entries.Single(entry => entry.Id == id).Item.TimelineItemContent switch
        {
            TimelineItem.Content.Assistant assistant => assistant.Value.Text,
            TimelineItem.Content.User user => user.Value.Text,
            var other => throw new InvalidOperationException(other.GetType().Name),
        });

    /// <summary>The Hydra notes in the timeline, in order: rows of no turn that carry an (empty) head list.</summary>
    public string[] Notes() => OnMain(() => Runtime.Entries
        .Where(entry => entry.TurnID is null && entry.Item.TimelineItemContent is TimelineItem.Content.User { Value.HydraHeads: not null })
        .Select(entry => ((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value.Text)
        .ToArray());

    /// <summary>The heads of this lead, in the order they were made.</summary>
    public ChatThread[] Heads => OnMain(() => App.HydraHeads(Lead.Id).Where(head => head.Hydra!.Index < 20)
        .OrderBy(head => head.Hydra!.Index).ToArray());

    public HydraDelegationBudget Budget
    {
        get => OnMain(() => ThreadRuntimeReflection.Get<HydraDelegationBudget>(Runtime, "hydraBudget")!);
        set => OnMain(() => ThreadRuntimeReflection.Set(Runtime, "hydraBudget", value));
    }

    public DelegationStreamState? Stream => OnMain(() =>
    {
        var value = ThreadRuntimeReflection.Get<object>(Runtime, "hydraStream");
        if (value is null) return null;
        var type = value.GetType();
        return new DelegationStreamState(
            (Guid)type.GetField("Item1")!.GetValue(value)!, (string)type.GetField("Item2")!.GetValue(value)!,
            (Guid)type.GetField("Item3")!.GetValue(value)!, (long)type.GetField("Item4")!.GetValue(value)!);
    });

    public void SetStream(DelegationStreamState? state) => OnMain(() => ThreadRuntimeReflection.Set(Runtime, "hydraStream",
        state is null ? null : (state.TurnID, state.EntryID, state.BatchID, state.Sent)));

    /// <summary>A stream left over from another turn: the scan of a reply returns at its gate and the streamed branch of
    /// spawnDelegatedHeads skips it (ThreadRuntime.swift:2842-2851), so a block is sent out by the path below them.</summary>
    public DelegationStreamState StaleStream()
    {
        var state = new DelegationStreamState(Guid.NewGuid(), "gone", Guid.NewGuid(), 4);
        SetStream(state);
        return state;
    }

    public HydraDelegationStreamParser? Parser => OnMain(() => ThreadRuntimeReflection.Get<HydraDelegationStreamParser>(Runtime, "delegationParser"));

    public void SetParser(HydraDelegationStreamParser? parser) => OnMain(() => ThreadRuntimeReflection.Set(Runtime, "delegationParser", parser));

    public WaitingTask[] Waiting => OnMain(() => ((IEnumerable)ThreadRuntimeReflection.Get<object>(Runtime, "hydraWaitingStorage")!)
        .Cast<object>().Select(item =>
        {
            var type = item.GetType();
            return new WaitingTask((HydraDelegation)type.GetField("Item1")!.GetValue(item)!, (Guid)type.GetField("Item2")!.GetValue(item)!,
                (DateTimeOffset)type.GetField("Item3")!.GetValue(item)!);
        }).ToArray());

    // The property, not the field behind it: its setter keeps HydraWaitingCount current.
    public void SetWaiting(IEnumerable<WaitingTask> tasks) => OnMain(() =>
        typeof(ThreadRuntime).GetProperty("hydraWaiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(Runtime, tasks.Select(task => (task.Delegation, task.BatchID, task.QueuedAt)).ToList()));

    public OpenBatch[] Batches => OnMain(() => ((IEnumerable)ThreadRuntimeReflection.Get<object>(Runtime, "hydraBatches")!)
        .Cast<object>().Select(pair =>
        {
            var batch = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
            var type = batch.GetType();
            return new OpenBatch((Guid)pair.GetType().GetProperty("Key")!.GetValue(pair)!,
                ((IEnumerable<Guid>)type.GetProperty("Pending")!.GetValue(batch)!).ToArray(),
                ((IEnumerable<HydraReport>)type.GetProperty("Reports")!.GetValue(batch)!).ToArray());
        }).ToArray());

    public void SetBatch(Guid id, IEnumerable<Guid> pending, IEnumerable<HydraReport>? reports = null) => OnMain(() =>
    {
        var batches = (IDictionary)ThreadRuntimeReflection.Get<object>(Runtime, "hydraBatches")!;
        var type = typeof(ThreadRuntime).GetNestedType("HydraBatch", System.Reflection.BindingFlags.NonPublic)!;
        var batch = Activator.CreateInstance(type)!;
        type.GetProperty("Pending")!.SetValue(batch, new ValueSet<Guid>(pending));
        type.GetProperty("Reports")!.SetValue(batch, new ValueList<HydraReport>(reports ?? []));
        batches[id] = batch;
    });

    public HydraReport[] PendingReports => OnMain(() =>
        ThreadRuntimeReflection.Get<List<HydraReport>>(Runtime, "hydraPendingReports")!.ToArray());

    public void SetPendingReports(IEnumerable<HydraReport> reports) => OnMain(() =>
        ThreadRuntimeReflection.Set(Runtime, "hydraPendingReports", reports.ToList()));

    public static HydraReport Report(long index, string text = "done", HydraHeadInfo.Status status = HydraHeadInfo.Status.Completed) => new()
    {
        HeadIndex = index,
        Task = "Task " + index,
        Origin = HydraHeadInfo.Origin.Delegated,
        Status = status,
        Text = text,
    };

    /// <summary>A delegation block of <paramref name="count"/> tasks, titled "Task 1" and on (the block a lead writes at the end of its reply).</summary>
    public static string Block(int count, string before = "") =>
        before + "```hydra\n[" + string.Join(",", Enumerable.Range(1, count).Select(index =>
            "{\"task\":\"Task " + index + "\",\"prompt\":\"Do task " + index + ".\"}")) + "]\n```";

    public void Dispose() => H.Dispose();
}
