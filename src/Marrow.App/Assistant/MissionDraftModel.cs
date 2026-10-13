// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 2, 12.4 "Reader and actor split", 15.4; unit AS-19; ED-NEW-dextra-as19-4). No Swift twin.
// The model that writes a teach draft up, as the app has it: a reader mission on the account and model Dextra's brain uses. The brief is the
// draft prompt (the steps as fenced data, no frames); the mission's agents may only read; nothing of it is reviewed or merged; its report is
// the model's answer, read afterwards by TeachDraftModel as data. The mission is an ordinary one on the board, so Stop all, Pause all and the
// usage reserve all apply to it. If it ends any way but Done, or takes too long, there is no answer and the draft stays as the recording made it.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class MissionDraftModelOptions
{
    public required string AssistantId { get; init; }

    public required Func<MissionProject?> Project { get; init; }

    /// <summary>Runs work on the app's main thread (the mission runner lives there).</summary>
    public required Action<Action> OnMain { get; init; }

    public ProviderKind Provider { get; init; } = ProviderKind.Claude;

    public string? Model { get; init; }

    /// <summary>How long the mission may take before it is stopped and the draft goes without it.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

public sealed class MissionDraftModel(AssistantController controller, MissionDraftModelOptions options) : ITeachDraftModel
{
    public async Task<string?> Complete(string prompt, CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        cancel.ThrowIfCancellationRequested();
        if (options.Project() is not { } project) return null;
        var finished = new TaskCompletionSource<MissionRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? id = null;
        var gate = new object();
        void Watch(MissionRecord record)
        {
            lock (gate)
            {
                if (id != record.Id) return;
            }
            if (record.Mission.State is MissionState.Done or MissionState.Failed or MissionState.Stopped or MissionState.Blocked) finished.TrySetResult(record);
        }
        Action<MissionRecord> watch = Watch;
        options.OnMain(() =>
        {
            controller.Runner.Changed += watch;
            try
            {
                var started = controller.Runner.Start(new MissionRequest("Write up a taught skill", prompt, options.AssistantId, project.Id, project.Path, options.Provider)
                {
                    Model = options.Model,
                    Readers = true,
                });
                lock (gate) id = started.Record.Id;
                if (controller.Runner.Find(started.Record.Id) is { } now) Watch(now);
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException)
            {
                finished.TrySetResult(null);
            }
        });
        using var timeout = new CancellationTokenSource();
        using var timer = options.Time.CreateTimer(_ => timeout.Cancel(), null, options.Timeout, System.Threading.Timeout.InfiniteTimeSpan);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
        using var registration = linked.Token.Register(() => finished.TrySetResult(null));
        MissionRecord? record;
        try
        {
            record = await finished.Task.ConfigureAwait(false);
        }
        finally
        {
            options.OnMain(() => controller.Runner.Changed -= watch);
        }
        if (record is null || record.Mission.State != MissionState.Done)
        {
            // Ended any way but Done (or never began, or ran out of time or was cancelled): stop what is left of it.
            Guid? mission;
            lock (gate) mission = id;
            if (mission is { } found) options.OnMain(() => controller.Controls.Stop(found));
            cancel.ThrowIfCancellationRequested();
            return null;
        }
        return string.IsNullOrWhiteSpace(record.Report) ? null : record.Report;
    }
}
