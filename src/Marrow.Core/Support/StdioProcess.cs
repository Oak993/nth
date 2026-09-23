using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/StdioProcess.swift (B2 1fbf81e), :1-183.
public sealed class StdioProcess
{
    private readonly object gate = new();
    private readonly StdioFramer framer;
    private readonly StdioFramer.Framing framing;
    private readonly AsyncStream<JSONValue>.Continuation continuation;
    private readonly ProcessLauncher process = new() { UsesJobObject = false };
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

    // :49-52
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

    // :54-87
    public void Start()
    {
        var weak = new WeakReference<StdioProcess>(this);
        var output = stdout;
        stdout.ReadabilityHandler = data =>
        {
            if (!weak.TryGetTarget(out var owner))
            {
                output.ReadabilityHandler = null;
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

    // :89-103
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

    // :105-111
    public void Write(byte[] data) => writeQueue.Async(() =>
    {
        try { stdin.Write(data); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    });

    // :113-117
    public void CloseInput() => writeQueue.Async(() =>
    {
        try { stdin.CloseWriting(); }
        catch (IOException) { }
    });

    // :119-128. ED-126 supplies the Windows CTRL_BREAK and pid-only force kill.
    public void Terminate() => TerminationLadder.Stdio(process, CloseInput);

    // :131-139
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

    // :141-144
    private void Consume(byte[] data)
    {
        IReadOnlyList<byte[]> frames;
        lock (gate) frames = framer.Append(data);
        foreach (var frame in frames) Deliver(frame);
    }

    // :146-151
    private void Deliver(byte[] frame)
    {
        var count = frame.Length;
        while (count > 0 && (frame[count - 1] == 0x0D || frame[count - 1] == 0x20)) count--;
        if (count == 0) return;
        var message = JSONValue.Parse(frame.AsSpan(0, count).ToArray());
        if (message is not null) continuation.Yield(message);
    }

    // :153-156
    private void FinishOutput()
    {
        byte[]? remainder;
        lock (gate) remainder = framer.Finish();
        if (remainder is not null) Deliver(remainder);
        continuation.Finish();
    }

    // :158-165
    private void AppendError(byte[] data)
    {
        lock (gate)
        {
            errorBuffer.AddRange(data);
            if (errorBuffer.Count > 64_000) errorBuffer.RemoveRange(0, errorBuffer.Count - 16_000);
        }
    }

    // :167-182
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
