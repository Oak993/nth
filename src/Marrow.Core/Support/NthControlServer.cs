using System.IO.Pipes;

namespace Marrow.Core.Support;

/// <summary>
/// What nth decided about one request: the answer, and what to do once the answer is on the pipe (a quit goes ahead only
/// after the installer has read "ok"; an idle quit can prepare and return a final Busy/refusal on the same bounded connection).
/// </summary>
/// <param name="Response">The answer to write.</param>
/// <param name="AfterReply">Runs once, after the answer was written and the client acknowledged reading it (<see cref="NthControl.AckByte"/>); never runs when the other side left first or did not acknowledge.</param>
public sealed record NthControlAnswer(NthControlResponse Response, Action? AfterReply = null,
    Func<CancellationToken, Task<NthControlAnswer>>? PrepareCommit = null, Action? Abort = null,
    Func<CancellationToken, Task<NthControlResponse?>>? Commit = null);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). The part of nth that decides what a
/// request on the control pipe means. <see cref="NthControlServer"/> only moves bytes; the app's implementation
/// (<c>Marrow.App.Port.NthControlHost</c>) moves to the main actor and reads the threads.
/// </summary>
public interface INthControlHost
{
    /// <summary>Decides one request. Called on a pool thread, never twice at once for one connection.</summary>
    Task<NthControlAnswer> HandleAsync(NthControlRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). Listens on nth's control pipe
/// (<see cref="NthControl.PipeName"/>) for the signed-in user alone. One request per connection, with acknowledged quit phases
/// (<see cref="NthControl.Frame"/>); a connection that sends nothing within <see cref="RequestTimeout"/>, sends a length that is
/// not 1 byte to 64 KiB, or leaves is dropped without touching the host. A request that is not version 1 gets
/// <c>refused</c> with the reason <c>unsupported</c>. Each connection has its own task, so a slow or silent client never holds
/// up the next. The first instance of the name must be this one (<c>PipeOptions.FirstPipeInstance</c>): a second nth in the
/// machine does not answer for the first.
/// </summary>
public sealed class NthControlServer : IDisposable
{
    /// <summary>How long a connection may take to send its request.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The deadline for the complete request, host, response and acknowledgement connection.</summary>
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How long the server waits for the acknowledgement of an answer that a quit follows.</summary>
    public static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(2);

    private readonly string pipeName;
    private readonly INthControlHost host;
    private readonly TimeSpan requestTimeout;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly HashSet<Task> connections = [];
    private readonly Task loop;
    private int disposed;

    private NthControlServer(string pipeName, INthControlHost host, TimeSpan requestTimeout, NamedPipeServerStream first)
    {
        this.pipeName = pipeName;
        this.host = host;
        this.requestTimeout = requestTimeout;
        loop = Task.Run(() => Run(first));
    }

    /// <summary>The pipe's name (without <c>\\.\pipe\</c>).</summary>
    public string Name => pipeName;

    /// <summary>
    /// Starts listening; the pipe exists when this returns. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the name is taken by another process.
    /// </summary>
    public static NthControlServer Start(string pipeName, INthControlHost host, TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(host);
        return new NthControlServer(pipeName, host, requestTimeout ?? RequestTimeout, CreatePipe(pipeName, first: true));
    }

    /// <summary>Like <see cref="Start"/>, but null when the pipe cannot be made: nth still runs, the installer then falls back to asking before it ends nth.</summary>
    public static NthControlServer? TryStart(string pipeName, INthControlHost host, TimeSpan? requestTimeout = null)
    {
        try
        {
            return Start(pipeName, host, requestTimeout);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static NamedPipeServerStream CreatePipe(string name, bool first) => new(
        name,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
        inBufferSize: 4096,
        outBufferSize: 4096);

    private async Task Run(NamedPipeServerStream first)
    {
        var pipe = first;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // A client that connected and left before it was accepted: make a fresh instance and carry on.
                    pipe.Dispose();
                    pipe = CreatePipe(pipeName, first: false);
                    continue;
                }
                var accepted = pipe;
                pipe = CreatePipe(pipeName, first: false); // the next client finds an instance at once
                Track(Serve(accepted));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // The pipe cannot be made again: nth goes on without answering (the installer asks before it ends nth).
        }
        finally
        {
            pipe.Dispose();
        }
    }

    private void Track(Task connection)
    {
        lock (gate) connections.Add(connection);
        _ = connection.ContinueWith(done =>
        {
            lock (gate) connections.Remove(done);
        }, TaskScheduler.Default);
    }

    private async Task Serve(NamedPipeServerStream pipe)
    {
        await using (pipe.ConfigureAwait(false))
        using (var disconnected = new CancellationTokenSource())
        using (var watch = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            var monitor = NthControlConnection.WatchDisconnectAsync(pipe, disconnected, watch.Token);
            try
            {
                await NthControlConnection.ServeAsync(pipe, host, requestTimeout, ConnectionTimeout,
                    stop.Token, disconnected.Token).ConfigureAwait(false);
            }
            finally
            {
                watch.Cancel();
                await monitor.ConfigureAwait(false);
            }
        }
    }

    internal static async Task<NthControlAnswer> HandleConnectedAsync(INthControlHost host,
        NthControlRequest request, CancellationToken serverStop, CancellationToken connection)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(serverStop, connection);
        var pending = host.HandleAsync(request, connection);
        try { return await pending.WaitAsync(window.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
            // A non-cooperative host cannot retain the pipe/task. Observe its completion and release any late reservation.
            _ = pending.ContinueWith(done =>
            {
                if (done.IsCompletedSuccessfully)
                {
                    try { done.Result.Abort?.Invoke(); }
                    catch (Exception) { /* cleanup failure must not retain the disconnected reservation/task */ }
                }
                else _ = done.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>Stops listening; connections in flight are dropped.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        Task[] open;
        lock (gate) open = [.. connections];
        try
        {
            // A short wait only: a request that was waiting for a main loop that has already ended cannot finish, and nth is on its way out.
            Task.WaitAll([loop, .. open], TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Each task has already handled its own failures.
        }
    }
}
