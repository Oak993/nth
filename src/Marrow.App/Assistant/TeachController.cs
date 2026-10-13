// PORT-ONLY: Dextra (DESIGN.md rev 5, 10, 12.5, 12.6, 15.3, 16.2; unit AS-19; ED-NEW-dextra-as19-1, -4 to -8, -11). No Swift twin.
// Teach mode as the app holds it: the owner clicks Teach (the home menu, or says "Watch me do this"), a recording runs behind the strip, and
// afterwards it is drafted, its questions go to the decision queue, the draft is dry-run, tried with the owner watching, and saved as an own
// skill; the raw recording is then deleted unless he keeps it. This is the one object the home, the strip and the voice command talk to. It
// is the Assistant's stop scope for a recording (Stop all ends it), writes only ids and counts to the activity log, and holds no window.
// Split by what it does: this file records, TeachController.Draft the draft and its questions, .Run the dry run and the trial, .Save the end.
using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.App.Assistant;

/// <summary>What one recording needs of the PC. A new set for each recording, so the hooks of one are never the hooks of the next.</summary>
public sealed record TeachPlatform(ITeachInput Input, IElementProbe Probe, IFrameSource Frames)
{
    /// <summary>Makes the narration (the microphone and the local speech engine); null: no narration.</summary>
    public Func<Func<TimeSpan>, Action<TeachNarrationLine>, Action<string>, ITeachNarration>? Narration { get; init; }

    /// <summary>Visus's browser steps; null until Visus lands (16.2).</summary>
    public IBrowserSteps? Browser { get; init; }

    public TeachSource Source { get; init; } = TeachSource.Direct;
}

public sealed class TeachControllerOptions
{
    public required AssistantStore Store { get; init; }

    /// <summary>Dextra's own id: the recordings are its.</summary>
    public required string AssistantId { get; init; }

    public required Func<TeachPlatform> Platform { get; init; }

    public required DecisionQueue Queue { get; init; }

    /// <summary>The model that writes a draft up; null: drafts come straight from the recording.</summary>
    public ITeachDraftModel? Model { get; init; }

    public GlobalStop? StopKey { get; init; }

    /// <summary>The own-skill service. Null: a skill cannot be saved here (the draft says so).</summary>
    public ICustomAddBackend? Skills { get; init; }

    /// <summary>The own skills now, to settle a save that was cut short.</summary>
    public Func<IReadOnlyList<OwnItem>> OwnSkills { get; init; } = () => [];

    /// <summary>Called after a skill was added: the Skills page lists it.</summary>
    public Action? SkillsChanged { get; init; }

    /// <summary>Does a plain step of a trial on the PC. Null until Tactus lands: the owner follows the draft himself.</summary>
    public ITeachStepExecutor? Executor { get; init; }

    public Action<AuditEntry>? Audit { get; init; }

    public Action<string>? Faulted { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

public sealed partial class TeachController : IStopScope, IDisposable
{
    private readonly TeachControllerOptions o;
    private readonly TeachRecordings recordings;
    private readonly TeachDrafter drafter;
    private readonly object gate = new();
    private TeachRecorder? recorder;
    private bool disposed;

    public TeachController(TeachControllerOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        recordings = new TeachRecordings(o.Store, o.AssistantId);
        drafter = new TeachDrafter(o.Model);
    }

    /// <summary>The Assistant's stop scope (Stop all, 12.6): a recording ends with what it has.</summary>
    string IStopScope.Name => "teach";

    void IStopScope.Stop() => Active?.RequestStop(TeachStopReason.StopAll);

    /// <summary>Raised when a recording starts, changes or ends, a draft changes, or a trial moves: the views read the controller again. On any thread.</summary>
    public event Action? Changed;

    public TeachRecordings Recordings => recordings;

    /// <summary>The recording that is running (or paused), if any.</summary>
    public TeachRecorder? Active
    {
        get
        {
            lock (gate) return recorder is { State: TeachRecordingState.Recording or TeachRecordingState.Paused } running ? running : null;
        }
    }

    // ---- recording ----

    /// <summary>
    /// "Teach" / "Watch me do this". Begins a recording, or says why not (the stop chord is taken, Windows refused the hooks, one is running, a
    /// trial is in front of the owner). Nothing is recorded until this returns a started result.
    /// </summary>
    public TeachStartResult Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Active is not null) return new TeachStartResult(null, "A recording is already running. Stop it first.");
            if (CurrentRun() is not null) return new TeachStartResult(null, "A trial is in front of you. Finish it or stop it first.");
        }
        var platform = o.Platform();
        var made = new TeachRecorder(new TeachRecorderOptions
        {
            Recordings = recordings,
            Input = platform.Input,
            Probe = platform.Probe,
            Frames = platform.Frames,
            Time = o.Time,
            StopKey = o.StopKey,
            Narration = platform.Narration,
            Browser = platform.Browser,
            Source = platform.Source,
            Ended = Ended,
            Faulted = message => o.Faulted?.Invoke(message),
        });
        made.Changed += _ => Changed?.Invoke();
        var started = made.Start();
        if (!started.Started)
        {
            made.Dispose();
            return started;
        }
        lock (gate) recorder = made;
        Log("start", $"recording {started.Info!.Id}");
        Changed?.Invoke();
        return started;
    }

    public void Pause() => Active?.Pause();

    public void Resume() => Active?.Resume();

    /// <summary>The owner stops it. The recording is kept either way; this returns its manifest once it is written.</summary>
    public async Task<TeachRecordingInfo?> Stop()
    {
        var running = Active;
        return running is null ? null : await running.Stop().ConfigureAwait(false);
    }

    private void Ended(TeachRecordingInfo info)
    {
        Log("stop", $"recording {info.Id}: {info.Ended}, {info.Events} events, {info.Frames} frames, {info.Narration} lines, {(int)info.Elapsed.TotalSeconds}s");
        Changed?.Invoke();
    }

    // ---- what is there ----

    /// <summary>The recordings, newest first, and the ids whose manifest does not read.</summary>
    public IReadOnlyList<TeachRecordingInfo> List(out IReadOnlyList<string> unreadable) => recordings.List(out unreadable);

    /// <summary>
    /// What happens when nth starts: a recording a crash left running becomes Interrupted, one that was saved and not kept is deleted the rest of
    /// the way, one nobody saved within two weeks is deleted, a save cut short is settled, and staging folders a crash left are removed.
    /// </summary>
    public TeachSweep Settle()
    {
        o.Store.RemoveTeachTempFiles();
        SettleSaves();
        o.Store.RemoveTeachStagings(o.AssistantId);
        var sweep = recordings.Sweep(o.Time.GetUtcNow(), Active?.Id);
        foreach (var (id, reason) in sweep.Deleted) Log("delete", $"recording {id}: {reason}");
        foreach (var id in sweep.Interrupted) Log("interrupted", $"recording {id}");
        if (sweep.Deleted.Count + sweep.Interrupted.Count > 0) Changed?.Invoke();
        return sweep;
    }

    // ---- the activity log: ids and counts, never what was recorded ----

    private void Log(string action, string result)
    {
        if (o.Audit is not { } audit) return;
        try
        {
            audit(new AuditEntry { Assistant = o.AssistantId, Tool = "nth-dextra/teach-" + action, ActionClass = "Work", Result = result });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            o.Faulted?.Invoke("teach audit: " + error.GetType().Name);
        }
    }

    public void Dispose()
    {
        TeachRecorder? running;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            running = recorder;
        }
        StopTrial();
        running?.Dispose();
    }
}
