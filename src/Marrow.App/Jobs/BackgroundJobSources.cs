using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Downloads;

// PORT-ONLY: web surfaces W5, the app's sources of Background tasks (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.10;
// ED-NEW-web-w5-1). No Swift counterpart. Each source reads one place the app already keeps work and hands its jobs to
// Core's BackgroundJobs as snapshots; none of them keeps a second list. Tactus streams, Visus agent tabs, workflows and
// the assistant's missions have no place to read yet: they publish through AppModel.PublishedJobs (a PublishedJobSource).

namespace Marrow.App.Jobs;

/// <summary>Hydra heads (the panel's heads) of every thread, live from their runtimes.</summary>
public sealed class HydraHeadJobSource(AppModel model) : IBackgroundJobSource
{
    public const string Prefix = "hydra:";

    public string Name => "Hydra heads";

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
    {
        var jobs = new List<BackgroundJob>();
        foreach (var thread in model.Threads)
        {
            if (!thread.IsInPanel || thread.IsArchived || thread.Hydra is not { } stored || thread.ParentThreadID is not { } parent) continue;
            if (stored.IsFinished && stored.FinishedAt is { } over && now - over >= BackgroundJobText.DoneLifetime) continue; // long gone from the list: not worth building
            var runtime = model.ExistingRuntime(thread.Id);
            var info = HydraLiveInfo.Shown(stored, runtime);
            var running = info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running;
            var title = BackgroundJobText.OneLine(info.Task);
            jobs.Add(new BackgroundJob
            {
                Id = Prefix + thread.Id.ToString("N"),
                Kind = BackgroundJobKind.HydraHead,
                Title = title.Length > 0 ? title : info.DisplayName,
                KindLabel = "Hydra head",
                Subject = info.DisplayName,
                ThreadId = parent,
                State = StateOf(info.HydraHeadInfoStatus),
                StartedAt = info.StartedAt,
                FinishedAt = info.FinishedAt,
                Tokens = Math.Max(info.Tokens, running ? runtime?.HydraTokens ?? 0 : 0),
                ToolUses = info.ToolCalls,
                Activity = running ? (BackgroundJobText.OneLine(info.Activity, 60) is { Length: > 0 } activity ? activity : "Working") : null,
                TranscriptThreadId = thread.Id,
                CanStop = running && info.CanStop,
            });
        }
        return jobs;
    }

    internal static BackgroundJobState StateOf(HydraHeadInfo.Status status) => status switch
    {
        HydraHeadInfo.Status.Running => BackgroundJobState.Running,
        HydraHeadInfo.Status.Completed => BackgroundJobState.Done,
        HydraHeadInfo.Status.Failed => BackgroundJobState.Failed,
        _ => BackgroundJobState.Stopped,
    };

    private Guid? ThreadOf(string jobId) =>
        jobId.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(jobId.AsSpan(Prefix.Length), "N", out var id) ? id : null;

    public bool Stop(string jobId)
    {
        if (ThreadOf(jobId) is not { } id || model.Thread(id) is not { Hydra: { IsFinished: false } }) return false;
        model.StopHydraHead(id);
        return true;
    }

    public bool Pause(string jobId) => false;

    public bool Resume(string jobId) => false;

    public void Dismiss(string jobId)
    {
        if (ThreadOf(jobId) is not { } id || model.Thread(id) is not { Hydra.IsFinished: true }) return;
        model.UpdateThread(id, thread => thread with { IsInPanel = false });
        model.ReleaseHydraCopy(id);
    }
}

/// <summary>Helper chats and the panel's subagent: the child threads that are not Hydra heads, while they run and for ten minutes after.</summary>
public sealed class HelperJobSource(AppModel model) : IBackgroundJobSource
{
    public const string Prefix = "helper:";

    public string Name => "Helpers";

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
    {
        var jobs = new List<BackgroundJob>();
        foreach (var thread in model.Threads)
        {
            if (thread.ParentThreadID is not { } parent || thread.IsHydraHead || thread.IsArchived || thread.IsSideChat) continue; // a side chat is the person's own conversation (web W6), not a task
            var runtime = model.ExistingRuntime(thread.Id);
            var running = runtime?.IsRunning == true;
            if (!running && thread.LastStatus is null) continue; // a helper that never ran is a chat, not a task
            if (!running && now - thread.UpdatedAt >= BackgroundJobText.DoneLifetime) continue; // finished long ago: not worth building
            var title = BackgroundJobText.OneLine(thread.Title);
            var job = new BackgroundJob
            {
                Id = Prefix + thread.Id.ToString("N"),
                Kind = BackgroundJobKind.Helper,
                Title = title.Length > 0 ? title : "Helper",
                KindLabel = thread.IsInPanel ? "Subagent" : "Helper",
                ThreadId = parent,
                StartedAt = running ? runtime!.TurnStartedAt ?? thread.UpdatedAt : thread.CreatedAt,
                TranscriptThreadId = thread.Id,
            };
            if (running)
            {
                var (tokens, tools, activity) = Live(runtime!);
                job = job with { State = BackgroundJobState.Running, Tokens = tokens, ToolUses = tools, Activity = activity };
            }
            else
            {
                job = job with
                {
                    State = thread.LastStatus switch
                    {
                        TurnStatus.Completed => BackgroundJobState.Done,
                        TurnStatus.Failed => BackgroundJobState.Failed,
                        _ => BackgroundJobState.Stopped,
                    },
                    FinishedAt = thread.UpdatedAt,
                    CanStop = false,
                };
            }
            jobs.Add(job);
        }
        return jobs;
    }

    // The turn running now (the newest one): its tokens, the tool uses since it started, and the newest tool still running ("Reading foo.cs").
    private static (long Tokens, long Tools, string Activity) Live(ThreadRuntime runtime)
    {
        long tokens = 0;
        if (runtime.Turns.Count > 0) tokens = runtime.Turns[^1].TokenSpends?.Sum(spend => spend.TotalTokens) ?? 0;
        var since = runtime.TurnStartedAt;
        long tools = 0;
        string? current = null;
        var entries = runtime.Entries;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Item is not { TimelineItemContent: TimelineItem.Content.Tool tool } item) continue;
            if (since is { } start && item.Date < start) break;
            tools++;
            if (current is null && tool.Value.ToolCallStatus == ToolCall.Status.Running) current = ToolPresentation.Label(tool.Value);
        }
        return (tokens, tools, BackgroundJobText.OneLine(current, 60) is { Length: > 0 } label ? label : "Working");
    }

    private Guid? ThreadOf(string jobId) =>
        jobId.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(jobId.AsSpan(Prefix.Length), "N", out var id) ? id : null;

    public bool Stop(string jobId)
    {
        if (ThreadOf(jobId) is not { } id || model.ExistingRuntime(id) is not { IsRunning: true } runtime) return false;
        runtime.Interrupt();
        return true;
    }

    public bool Pause(string jobId) => false;

    public bool Resume(string jobId) => false;

    public void Dismiss(string jobId)
    {
    }
}

/// <summary>
/// Terminals running something: a session started with a command (a project script, a dev server), and a plain shell while a
/// program runs in it. A session whose process ended stays for ten minutes as Done (or Stopped when the person ended it).
/// </summary>
public sealed class TerminalJobSource : IBackgroundJobSource
{
    public const string Prefix = "terminal:";

    private static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(1);

    private readonly AppModel model;
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, (bool? Busy, DateTimeOffset At, bool Probing)> probes = [];

    public TerminalJobSource(AppModel model) => this.model = model;

    /// <summary>
    /// The address a dev server in a session answers on, or null (a plain terminal). Dev-server detection (W9) sets this; until it does,
    /// no terminal is flagged. Called on the main actor, at most 4 times a second per session.
    /// </summary>
    public static Func<TerminalSession, string?> DevServerUrlOf { get; set; } = _ => null;

    /// <summary>Whether a program runs in a plain shell (a live child of the shell process). Replaceable; the default reads the process table off the main actor.</summary>
    public Func<int, bool?> ProcessIsBusy { get; set; } = pid => ProcessDescendants.HasLiveDescendant(pid);

    public string Name => "Terminals";

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
    {
        var jobs = new List<BackgroundJob>();
        var seen = new HashSet<Guid>();
        foreach (var thread in model.Threads)
        {
            foreach (var session in model.Terminals.Sessions(thread.Id))
            {
                seen.Add(session.Id);
                if (JobOf(session, now) is { } job) jobs.Add(job);
            }
        }
        lock (gate)
            foreach (var id in probes.Keys.Where(id => !seen.Contains(id)).ToList()) probes.Remove(id);
        return jobs;
    }

    private BackgroundJob? JobOf(TerminalSession session, DateTimeOffset now)
    {
        if (!session.IsRunning && session.EndedAt is { } ended && now - ended >= BackgroundJobText.DoneLifetime) return null; // finished long ago
        var scripted = session.Command is { Length: > 0 };
        var busy = session.IsRunning && !scripted && IsBusy(session, now);
        return Map(new TerminalFacts(session.Id, session.ThreadID, session.Title, scripted, session.IsRunning, busy,
            session.StartedAt, session.EndedAt, session.EndedByUser, session.IsRunning ? DevServerUrlOf(session) : null), now);
    }

    /// <summary>What the source knows of one terminal session: the facts <see cref="Map"/> turns into a job.</summary>
    internal readonly record struct TerminalFacts(
        Guid Id, Guid ThreadId, string Title, bool Scripted, bool IsRunning, bool Busy,
        DateTimeOffset StartedAt, DateTimeOffset? EndedAt, bool EndedByUser, string? DevServerUrl);

    /// <summary>
    /// The job of a terminal, or null when it is not a task: a plain shell at its prompt, or one that ended without ever running a
    /// command. A scripted session (a project script, a dev server) is a task while it runs and stays ten minutes after.
    /// </summary>
    internal static BackgroundJob? Map(in TerminalFacts facts, DateTimeOffset now)
    {
        if (facts.IsRunning && !facts.Scripted && !facts.Busy) return null; // a shell at its prompt is not a task
        if (!facts.IsRunning && !facts.Scripted) return null;
        var url = facts.IsRunning ? facts.DevServerUrl : null;
        var title = BackgroundJobText.OneLine(facts.Title);
        var job = new BackgroundJob
        {
            Id = Prefix + facts.Id.ToString("N"),
            Kind = BackgroundJobKind.Terminal,
            Title = title.Length > 0 ? title : "Terminal",
            KindLabel = url is null ? "Terminal" : "Dev server",
            Subject = url is null ? null : HostOf(url),
            ThreadId = facts.ThreadId,
            StartedAt = facts.StartedAt,
            DevServerUrl = url,
            CanStop = facts.IsRunning,
        };
        if (facts.IsRunning) return job with { State = BackgroundJobState.Running, Activity = "Running" };
        return job with
        {
            State = facts.EndedByUser ? BackgroundJobState.Stopped : BackgroundJobState.Done,
            FinishedAt = facts.EndedAt,
        };
    }

    internal static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : url;

    // The busy answer of a plain shell comes from a process-table read, so it is taken off the main actor, at most once a
    // second, and the last answer is used until the next one lands. A shell not yet asked about is not listed.
    private bool IsBusy(TerminalSession session, DateTimeOffset now)
    {
        bool start;
        bool? busy;
        lock (gate)
        {
            probes.TryGetValue(session.Id, out var probe);
            busy = probe.Busy;
            start = !probe.Probing && (probe.At == default || now - probe.At >= ProbeEvery);
            if (start) probes[session.Id] = (probe.Busy, probe.At, true);
        }
        if (start)
        {
            var pid = session.View.ProcessId;
            var read = ProcessIsBusy;
            var id = session.Id;
            _ = Task.Run(() =>
            {
                bool? answer;
                try { answer = read(pid); }
                catch (Exception) { answer = null; }
                lock (gate) probes[id] = (answer, now, false);
            });
        }
        return busy == true;
    }

    private TerminalSession? Find(string jobId)
    {
        if (!jobId.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(jobId.AsSpan(Prefix.Length), "N", out var id)) return null;
        foreach (var thread in model.Threads)
            foreach (var session in model.Terminals.Sessions(thread.Id))
                if (session.Id == id) return session;
        return null;
    }

    public bool Stop(string jobId)
    {
        if (Find(jobId) is not { IsRunning: true } session) return false;
        session.Terminate();
        return true;
    }

    public bool Pause(string jobId) => false; // a running process has no safe point to pause (the assistant's table)

    public bool Resume(string jobId) => false;

    public void Dismiss(string jobId)
    {
    }
}

/// <summary>Every download the app started (<see cref="DownloadJobRegistry"/>), with Pause, Resume and Stop wired to the job.</summary>
public sealed class DownloadJobSource(DownloadJobRegistry registry) : IBackgroundJobSource
{
    public const string Prefix = "download:";

    public string Name => "Downloads";

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
    {
        var jobs = new List<BackgroundJob>();
        foreach (var entry in registry.Snapshot(now))
        {
            var job = entry.Job;
            var state = job.State;
            var progress = job.Progress;
            var done = state is DownloadJobState.Succeeded;
            var fraction = progress.BytesTotal > 0 ? Math.Clamp((double)progress.BytesDone / progress.BytesTotal, 0, 1) : (done ? 1 : 0);
            jobs.Add(new BackgroundJob
            {
                Id = Prefix + entry.Id.ToString("N"),
                Kind = BackgroundJobKind.Download,
                Title = entry.Title,
                KindLabel = "Download",
                Subject = BackgroundJobText.BytesOf(done ? progress.BytesTotal : progress.BytesDone, progress.BytesTotal),
                State = state switch
                {
                    DownloadJobState.Running => BackgroundJobState.Running,
                    DownloadJobState.Paused => BackgroundJobState.Paused,
                    DownloadJobState.Succeeded => BackgroundJobState.Done,
                    DownloadJobState.Failed => BackgroundJobState.Failed,
                    _ => BackgroundJobState.Stopped,
                },
                StartedAt = entry.StartedAt,
                FinishedAt = state is DownloadJobState.Running or DownloadJobState.Paused ? null : entry.FinishedAt,
                Progress = fraction,
                Activity = state == DownloadJobState.Running && job.IsWaitingToRetry ? "Waiting to retry" : null,
                CanPause = state is DownloadJobState.Running or DownloadJobState.Paused,
                CanStop = state is DownloadJobState.Running or DownloadJobState.Paused,
            });
        }
        return jobs;
    }

    private DownloadJob? Find(string jobId)
    {
        if (!jobId.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(jobId.AsSpan(Prefix.Length), "N", out var id)) return null;
        return registry.Snapshot(SwiftClock.Provider.Current.GetUtcNow()).FirstOrDefault(entry => entry.Id == id)?.Job;
    }

    public bool Stop(string jobId)
    {
        if (Find(jobId) is not { State: DownloadJobState.Running or DownloadJobState.Paused } job) return false;
        job.Cancel();
        return true;
    }

    public bool Pause(string jobId)
    {
        if (Find(jobId) is not { State: DownloadJobState.Running } job) return false;
        job.Pause();
        return true;
    }

    public bool Resume(string jobId)
    {
        if (Find(jobId) is not { State: DownloadJobState.Paused } job) return false;
        job.Resume();
        return true;
    }

    public void Dismiss(string jobId)
    {
    }
}
