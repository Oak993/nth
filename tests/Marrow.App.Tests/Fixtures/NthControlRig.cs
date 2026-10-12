using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A model with one thread per title, a shell that records its exit, and the host nth Setup talks to (nth installer PLAN.md 3a, unit U4). A thread
/// is "running" the way the app means it (its runtime is not idle: a real turn needs a provider, so the phase and start are set directly, as
/// HydraButtonRig does).
/// </summary>
public sealed class NthControlRig : IDisposable
{
    public NthControlRig(params string[] titles)
    {
        var project = AppModelSamples.Project("Project", H.NewFolder("project"));
        var threads = titles.Select(title => AppModelSamples.Thread(project) with { Title = title }).ToList();
        H.WriteLibrary([project], threads);
        Model = H.MakeModel();
        Runtimes = H.Main.OnMain(() => threads.Select(thread => Model.Runtime(thread.Id)).ToList());
        Shell = new AppShell(new AppDelegate { Model = Model }, Recorder.Exit, Recorder.AfterWillTerminate);
        Host = new NthControlHost(Shell, processId: 4242, executable: @"C:\Users\ana\AppData\Local\Programs\nth\current\nth.exe");
    }

    public AppModelHarness H { get; } = new();

    public AppModel Model { get; }

    public IReadOnlyList<ThreadRuntime> Runtimes { get; }

    public ShellRecorder Recorder { get; } = new();

    public AppShell Shell { get; }

    public NthControlHost Host { get; }

    public void SetRunning(int index, TimeSpan runningFor, bool running = true) => H.Main.OnMain(() =>
    {
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(Runtimes[index], running ? RuntimePhase.Running : RuntimePhase.Idle);
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.TurnStartedAt))!.SetValue(Runtimes[index], running ? SwiftDate.Now - runningFor : null);
    });

    public void SetMerging(int index, bool merging, TimeSpan mergingFor = default) => H.Main.OnMain(() =>
    {
        Runtimes[index].IsHydraMerging = merging;
        Runtimes[index].HydraMergeStartedAt = merging ? SwiftDate.Now - mergingFor : null;
    });

    /// <summary>The host's decision for one request, made on the main actor.</summary>
    public NthControlAnswer Ask(NthControlRequest request) => H.Main.OnMain(() => Host.Answer(request));

    /// <summary>What the pipe server does once the answer was read.</summary>
    public NthControlResponse Finish(NthControlAnswer answer)
    {
        if (answer.PrepareCommit is { } prepare)
            answer = H.Main.OnMain(() => prepare(CancellationToken.None)).GetAwaiter().GetResult();
        if (answer.Commit is { } commit)
        {
            var outcome = H.Main.OnMain(() => commit(CancellationToken.None)).GetAwaiter().GetResult();
            if (outcome is not null) answer = new NthControlAnswer(outcome);
        }
        else answer.AfterReply?.Invoke();
        H.Main.RunUntilIdle();
        return answer.Response;
    }

    public void Dispose() => H.Dispose();
}
