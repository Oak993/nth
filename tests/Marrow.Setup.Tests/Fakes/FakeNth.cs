using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>How the pretend nth's control pipe behaves.</summary>
public enum FakePipe
{
    /// <summary>It answers as the app does.</summary>
    Answers,

    /// <summary>There is no pipe (an older nth, or one still starting).</summary>
    Missing,

    /// <summary>The pipe is there and nobody answers (a hung nth).</summary>
    Silent,

    /// <summary>The pipe belongs to an elevated nth.</summary>
    NotAllowed,
}

/// <summary>
/// A pretend nth for the running-nth handshake's tests (nth installer PLAN.md 10, unit U4): it is both the control channel the setup talks
/// to and the process list it looks at, and it plays the app's part by the rules the real one follows (Core/Support/NthControl.cs and
/// Marrow.App.Port.NthControlHost): <c>status</c> changes nothing, <c>quit if-idle</c> is busy while work runs, <c>quit stop</c> and an idle
/// <c>if-idle</c> agree and the process leaves <see cref="ExitDelay"/> later (after a merge it is finishing), <c>withdraw</c> calls off a quit that
/// still waits on a merge. Time is whatever clock it is given: the test's fake clock (a 15 s hang then costs no real time) or the real one.
/// </summary>
public sealed class FakeNth : INthControlChannel, IRunningNthProcesses
{
    private readonly TimeProvider time;
    private readonly object gate = new();
    private readonly List<string> sent = [];
    private readonly List<NthControlRequest> requests = [];
    private readonly List<int> ended = [];
    private TimeSpan? exitAt;
    private List<RunningNthProcess> inRoot;
    private bool quitting;
    private readonly long begun;

    public FakeNth(TimeProvider time, string root, RunningNthProcess? mainProcess = null)
    {
        this.time = time;
        Root = root;
        begun = time.GetTimestamp();
        Main = mainProcess ?? new RunningNthProcess(4242, Path.Combine(root, "current", "nth.exe"), 1000);
        inRoot = [Main];
    }

    public string Root { get; }

    /// <summary>The time since the pretend nth began, on the clock it was given.</summary>
    private TimeSpan Since => time.GetElapsedTime(begun);

    /// <summary>The app's main process, inside the root.</summary>
    public RunningNthProcess Main { get; }

    /// <summary>Every nth program inside the root that runs (the main process, and whatever a test adds).</summary>
    public List<RunningNthProcess> InRoot
    {
        get
        {
            lock (gate)
            {
                Settle();
                return inRoot;
            }
        }
        set
        {
            lock (gate) inRoot = value;
        }
    }

    public FakePipe Pipe { get; set; } = FakePipe.Answers;

    /// <summary>The pipe appears this long after the test began (a starting nth); null means it is there from the start.</summary>
    public TimeSpan? PipeAppearsAfter { get; set; }

    /// <summary>The process id and file the answers name (the main process by default).</summary>
    public int? AnswerPid { get; set; }

    public string? AnswerExe { get; set; }

    /// <summary>What is running at this time since the test began; empty means idle.</summary>
    public Func<TimeSpan, IReadOnlyList<NthWorkItem>> Work { get; set; } = _ => [];

    /// <summary>A refusal word every quit gets (the app is elevated, or too old).</summary>
    public string? RefuseWith { get; set; }

    /// <summary>How long after it agreed (and after a merge) the process leaves; null means it agrees and then hangs.</summary>
    public TimeSpan? ExitDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>While the test's time is below this, a team merge is going out and a quit waits for it.</summary>
    public TimeSpan? MergeUntil { get; set; }

    /// <summary>False means Windows refuses to end it (an elevated nth).</summary>
    public bool CanBeEnded { get; set; } = true;

    /// <summary>True means it ignores the end request and stays (a stuck process).</summary>
    public bool SurvivesEnd { get; set; }

    public bool Running => InRoot.Count > 0;

    public bool Quitting
    {
        get
        {
            lock (gate)
            {
                Settle();
                return quitting;
            }
        }
    }

    /// <summary>Every request it was sent, as "status", "quit:if-idle", "quit:stop" or "withdraw".</summary>
    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (gate) return [.. sent];
        }
    }

    /// <summary>The process ids it was asked to end.</summary>
    public IReadOnlyList<int> Ended
    {
        get
        {
            lock (gate) return [.. ended];
        }
    }

    public IReadOnlyList<NthControlRequest> Requests { get { lock (gate) return [.. requests]; } }

    public int Count(string request) => Sent.Count(item => item == request);

    /// <summary>The person closes nth themselves this long after the test began (a point on the clock, so a fast-forwarded test cannot jump over a look at it).</summary>
    public void CloseAfter(TimeSpan sinceStart)
    {
        lock (gate) exitAt = sinceStart;
    }

    public void Close()
    {
        lock (gate)
        {
            inRoot = [];
            quitting = false;
            exitAt = null;
        }
    }

    // The exit is a point on the clock, not a timer of its own: the only timers a test's clock holds are the handshake's, so a test that moves time
    // from one timer to the next can never jump past what the handshake is about to ask, and the exit is seen exactly when it is looked at.
    private void Settle()
    {
        if (exitAt is not { } at || Since < at) return;
        inRoot = [];
        quitting = false;
        exitAt = null;
    }

    public Task<NthControlExchange> SendAsync(NthControlRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            sent.Add(Name(request));
            requests.Add(request);
            Settle();
            if (inRoot.Count == 0) return Task.FromResult(NthControlExchange.Failed(NthControlFailure.NoPipe));
            if (Pipe == FakePipe.Missing || (PipeAppearsAfter is { } after && Since < after))
                return Task.FromResult(NthControlExchange.Failed(NthControlFailure.NoPipe));
            if (Pipe == FakePipe.Silent) return Task.FromResult(NthControlExchange.Failed(NthControlFailure.NoAnswer));
            if (Pipe == FakePipe.NotAllowed) return Task.FromResult(NthControlExchange.Failed(NthControlFailure.NotAllowed));
            return Task.FromResult(NthControlExchange.Answered(Answer(request) with { Id = request.Id }));
        }
    }

    private NthControlResponse Answer(NthControlRequest request)
    {
        var pid = AnswerPid ?? Main.ProcessId;
        var exe = AnswerExe ?? Main.ExecutablePath;
        var work = CurrentWork();
        NthControlResponse Reply(NthControlReply kind, IReadOnlyList<NthWorkItem> list, NthWorkKind? deferred = null) => new(kind, pid, exe, list, list.Count, deferred);
        switch (request.Command)
        {
            case NthControlCommand.Status:
                return Reply(NthControlReply.Status, work);
            case NthControlCommand.Withdraw:
                if (quitting && MergeUntil is { } until && Since < until)
                {
                    exitAt = null;
                    quitting = false; // the merge's wait is called off
                }
                return Reply(NthControlReply.Ok, []);
            default:
                if (RefuseWith is { } reason) return NthControlResponse.Refused(pid, exe, reason);
                if (request.Mode == NthQuitMode.IfIdle && work.Count > 0 && !quitting) return Reply(NthControlReply.Busy, work);
                var merging = MergeUntil is { } mergeEnd && Since < mergeEnd;
                AgreeToQuit();
                return Reply(NthControlReply.Ok, [], merging ? NthWorkKind.Merge : null);
        }
    }

    private IReadOnlyList<NthWorkItem> CurrentWork()
    {
        var items = Work(Since).ToList();
        if (MergeUntil is { } until && Since < until) items.Add(new NthWorkItem("Ana", NthWorkKind.Merge, 30));
        return items;
    }

    private void AgreeToQuit()
    {
        if (quitting) return;
        quitting = true;
        if (ExitDelay is not { } delay) return; // agrees, then hangs
        exitAt = Since + (MergeUntil is { } until && Since < until ? until - Since : TimeSpan.Zero) + delay;
    }

    public IReadOnlyList<RunningNthProcess> FindInside(string installRoot)
    {
        lock (gate)
        {
            Settle();
            return string.Equals(installRoot, Root, StringComparison.OrdinalIgnoreCase) ? [.. inRoot] : [];
        }
    }

    public bool IsRunning(RunningNthProcess process)
    {
        lock (gate)
        {
            Settle();
            return inRoot.Any(item => item.ProcessId == process.ProcessId);
        }
    }

    public ProcessEndResult End(RunningNthProcess process)
    {
        lock (gate)
        {
            ended.Add(process.ProcessId);
            Settle();
            if (!inRoot.Any(item => item.ProcessId == process.ProcessId)) return ProcessEndResult.Gone;
            if (!CanBeEnded) return ProcessEndResult.AccessDenied;
            if (SurvivesEnd) return ProcessEndResult.Failed;
            inRoot = inRoot.Where(item => item.ProcessId != process.ProcessId).ToList();
            return ProcessEndResult.Ended;
        }
    }

    private static string Name(NthControlRequest request) => request.Command switch
    {
        NthControlCommand.Status => "status",
        NthControlCommand.Withdraw => "withdraw",
        _ => request.Mode == NthQuitMode.Stop ? "quit:stop" : "quit:if-idle",
    };
}
