using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/StdioProcess.swift (B3 52bc7926), :1-184.
public sealed class StdioProcess
{
    private readonly object gate = new();
    private readonly StdioFramer framer;
    private readonly StdioFramer.Framing framing;
    private readonly AsyncStream<JSONValue>.Continuation continuation;
    // ED-194 (B3 amendment of ED-047 (b) and ED-126): every StdioProcess child now gets a Job Object at spawn, with
    // no limits and no KILL_ON_JOB_CLOSE (ProcessLauncher.Spawn calls CreateJobObjectW alone, never
    // SetInformationJobObject), so ProcessControl.KillTree() can end the tree at quit. Stop still goes through
    // Terminate()/KillProcess() below, the unchanged ED-126 ladder: the job is never used there.
    private readonly ProcessLauncher process = new() { UsesJobObject = true };
    private readonly ProcessPipe stdin = new();
    private readonly ProcessPipe stdout = new();
    private readonly ProcessPipe stderr = new();
    private readonly SerialQueue writeQueue = new("droppycode.stdio.write");
    private readonly List<byte> errorBuffer = [];
    private readonly List<TaskCompletionSource<int>> exitWaiters = [];
    private int? exitStatus;

    // :13, :32-45
    public StdioProcess(string executable, IReadOnlyList<string> arguments, string directory,
        IReadOnlyDictionary<string, string> environment, StdioFramer.Framing framing = StdioFramer.Framing.Lines)
    {
        (Messages, continuation) = AsyncStream<JSONValue>.MakeStream();
        this.framing = framing;
        framer = new StdioFramer(framing);
        process.ExecutableURL = executable;
        process.Arguments = arguments;
        process.CurrentDirectoryURL = directory;
        process.Environment = environment;
        process.StandardInput = stdin;
        process.StandardOutput = stdout;
        process.StandardError = stderr;
    }

    public AsyncStream<JSONValue> Messages { get; }

    // :47
    public bool IsRunning => process.IsRunning;

    // :48 (B3, R3-41, ED-194)
    public int ProcessIdentifier => process.ProcessIdentifier;

    // PORT-ONLY (ED-194): no Swift member. The launcher as an IProcessControl handle, so a caller such as
    // MCPLiveProcesses (A15) can track this child and KillTree() it at quit; never used by Terminate() below.
    public IProcessControl ProcessControl => process;

    /// <summary>
    /// PORT-ONLY (Dextra AS-5, ED-NEW-dextra-as5-5): a mission agent's kill-on-close job; the process and everything it
    /// starts run in it. Set before Start.
    /// </summary>
    public JobObject? Job
    {
        get => process.OuterJob;
        set => process.OuterJob = value;
    }

    // :50-53 (B3)
    public string ErrorTail
    {
        get
        {
            lock (gate)
            {
                var start = Math.Max(0, errorBuffer.Count - 4_000);
                return Encoding.UTF8.GetString(errorBuffer.Skip(start).ToArray());
            }
        }
    }

    // :55-88 (B3)
    public void Start()
    {
        var weak = new WeakReference<StdioProcess>(this);
        var output = stdout;
        // DEVIATION ED-NEW-planlimits-hang-2: native I/O must root its pending stream/read
        // continuation. Weak owner callbacks alone let a suspended async flow be collected.
        var completion = continuation;
        stdout.ReadabilityHandler = data =>
        {
            if (!weak.TryGetTarget(out var owner))
            {
                output.ReadabilityHandler = null;
                completion.Finish();
                return;
            }
            if (data.Length == 0)
            {
                output.ReadabilityHandler = null;
                owner.FinishOutput();
            }
            else owner.Consume(data);
        };
        var error = stderr;
        stderr.ReadabilityHandler = data =>
        {
            if (!weak.TryGetTarget(out var owner) || data.Length == 0)
            {
                error.ReadabilityHandler = null;
                return;
            }
            owner.AppendError(data);
        };
        process.TerminationHandler = exited =>
        {
            if (weak.TryGetTarget(out var owner)) owner.DidExit(exited.TerminationStatus);
        };
        try { process.Run(); }
        catch
        {
            stdout.ReadabilityHandler = null;
            stderr.ReadabilityHandler = null;
            continuation.Finish();
            stdin.CloseWriting();
            stdout.CloseWriting();
            stderr.CloseWriting();
            throw;
        }
    }

    /// <summary>
    /// PORT-ONLY (ED-NEW-perf-ui-thread-2). <see cref="Start"/> with CreateProcessW (pipes, job object, a virus scan of the executable: tens of milliseconds, more on a cold disk) on the pool,
    /// so a session started from the UI thread's turn does not hold a frame for it. Start only sets handlers and runs the launcher, none of which needs the caller's thread; a failure
    /// to start reaches the awaiting caller as the same exception.
    /// </summary>
    public Task StartAsync() => LaunchRunner.Current(this, Start);

    /// <summary>
    /// PORT-ONLY test seam (ED-NEW-perf-ui-thread-7). How <see cref="StartAsync"/> schedules the launch: the pool by default. A test overrides it for its own flow to hold the launch,
    /// to see which thread makes it, or to place a Stop inside the window in which the child does not exist yet. It gets the process and the launch itself.
    /// </summary>
    public static Ambient<Func<StdioProcess, Action, Task>> LaunchRunner { get; } = new(static (_, launch) => Task.Run(launch));

    // :90-104 (B3)
    public void Send(JSONValue message)
    {
        writeQueue.Async(() =>
        {
            var body = message.Data();
            byte[] payload;
            if (framing == StdioFramer.Framing.Lines)
            {
                payload = new byte[body.Length + 1];
                body.CopyTo(payload, 0);
                payload[^1] = 0x0A;
            }
            else
            {
                var header = Encoding.UTF8.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
                payload = new byte[header.Length + body.Length];
                header.CopyTo(payload, 0);
                body.CopyTo(payload, header.Length);
            }
            try { stdin.Write(payload); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        });
    }

    // :106-112 (B3)
    public void Write(byte[] data)
    {
        var snapshot = (byte[])data.Clone(); // Swift Data is a value: the queued closure keeps the bytes as they were at the call
        writeQueue.Async(() =>
        {
            WriteSnapshot(snapshot);
        });
    }

    private void WriteSnapshot(byte[] data)
    {
        try { stdin.Write(data); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    // :114-118 (B3)
    public void CloseInput() => writeQueue.Async(() =>
    {
        try { stdin.CloseWriting(); }
        catch (IOException) { }
    });

    // :120-129 (B3). ED-126 supplies the Windows CTRL_BREAK and pid-only force kill.
    public void Terminate() => TerminationLadder.Stdio(process, CloseInput);

    // :132-140 (B3)
    public Task<int> WaitForExit()
    {
        lock (gate)
        {
            if (exitStatus is { } status) return Task.FromResult(status);
            var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            exitWaiters.Add(waiter);
            return waiter.Task;
        }
    }

    // :142-145 (B3)
    private void Consume(byte[] data)
    {
        IReadOnlyList<byte[]> frames;
        lock (gate) frames = framer.Append(data);
        foreach (var frame in frames) Deliver(frame);
    }

    // :147-152 (B3)
    private void Deliver(byte[] frame)
    {
        var count = frame.Length;
        while (count > 0 && (frame[count - 1] == 0x0D || frame[count - 1] == 0x20)) count--;
        if (count == 0) return;
        var message = JSONValue.Parse(frame.AsSpan(0, count).ToArray());
        if (message is not null) continuation.Yield(message);
    }

    // :154-157 (B3)
    private void FinishOutput()
    {
        byte[]? remainder;
        lock (gate) remainder = framer.Finish();
        if (remainder is not null) Deliver(remainder);
        continuation.Finish();
    }

    // :159-166 (B3)
    private void AppendError(byte[] data)
    {
        lock (gate)
        {
            errorBuffer.AddRange(data);
            if (errorBuffer.Count > 64_000) errorBuffer.RemoveRange(0, errorBuffer.Count - 16_000);
        }
    }

    // :168-183 (B3)
    private void DidExit(int status)
    {
        TaskCompletionSource<int>[] waiters;
        lock (gate)
        {
            exitStatus = status;
            waiters = [.. exitWaiters];
            exitWaiters.Clear();
        }
        foreach (var waiter in waiters) waiter.TrySetResult(status);
        var handle = stdout;
        var weak = new WeakReference<StdioProcess>(this);
        GlobalQueue.AsyncAfter(TimeSpan.FromSeconds(1), () =>
        {
            handle.ReadabilityHandler = null;
            if (weak.TryGetTarget(out var owner)) owner.FinishOutput();
        });
    }
}
