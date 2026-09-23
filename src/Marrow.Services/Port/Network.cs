// PORT-ONLY: Network.framework's NWListener and NWConnection as MCPOAuth's callback server uses them (MCPOAuth.swift:409-570), over loopback sockets (R-NET-2).
using System.Net;
using System.Net.Sockets;
using Marrow.Core.Port;

namespace Marrow.Services.Port;

/// <summary>
/// Swift <c>NWListener</c> for a TCP listener whose parameters set only <c>requiredLocalEndpoint</c>
/// (MCPOAuth.swift:409-410): it binds that address when started, reports its states on the queue passed to
/// <see cref="Start"/>, and hands each accepted connection to <see cref="NewConnectionHandler"/> on that queue.
/// <c>cancel()</c> closes the listening socket and reports <see cref="State.Cancelled"/> once; an accepted connection
/// is independent of the listener and outlives its cancellation, as an NWConnection does.
/// </summary>
public sealed class NWListener
{
    /// <summary>
    /// The listener states MCPOAuth switches on (MCPOAuth.swift:423-445). NWListener.State's other cases (setup,
    /// waiting) are never reported here; its failed case carries an NWError the caller ignores (:439).
    /// </summary>
    public enum State { Ready, Failed, Cancelled }

    private readonly IPEndPoint requiredLocalEndpoint;
    private readonly Socket socket;
    private readonly Mutex<(bool Started, bool Cancelled, SerialQueue? Queue)> status = new((false, false, null));

    /// <summary>
    /// <c>try NWListener(using: parameters)</c> with <c>requiredLocalEndpoint</c> (:410-413). Creating the socket can
    /// throw, as the Swift initializer can; binding happens at <see cref="Start"/>.
    /// </summary>
    public NWListener(IPEndPoint requiredLocalEndpoint)
    {
        this.requiredLocalEndpoint = requiredLocalEndpoint;
        socket = new Socket(requiredLocalEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    }

    public Action<State>? StateUpdateHandler { get; set; }

    public Action<NWConnection>? NewConnectionHandler { get; set; }

    /// <summary>Swift <c>listener.port</c>: the bound port once ready, nil before.</summary>
    public int? Port { get; private set; }

    /// <summary><c>listener.start(queue:)</c> (:451).</summary>
    public void Start(SerialQueue queue)
    {
        var cancelledBeforeStart = status.WithLock((ref (bool Started, bool Cancelled, SerialQueue? Queue) s) =>
        {
            s.Started = true;
            s.Queue = queue;
            return s.Cancelled;
        });
        if (cancelledBeforeStart)
        {
            queue.Async(() => StateUpdateHandler?.Invoke(State.Cancelled));
            return;
        }
        try
        {
            socket.Bind(requiredLocalEndpoint);
            socket.Listen();
            Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
            if (!IsCancelled) queue.Async(() => StateUpdateHandler?.Invoke(State.Failed));
            return;
        }
        queue.Async(() => StateUpdateHandler?.Invoke(State.Ready));
        _ = AcceptLoop(queue);
    }

    /// <summary><c>listener.cancel()</c>: idempotent; the cancelled state is reported once, on the listener's queue.</summary>
    public void Cancel()
    {
        var (first, queue) = status.WithLock((ref (bool Started, bool Cancelled, SerialQueue? Queue) s) =>
        {
            var wasCancelled = s.Cancelled;
            s.Cancelled = true;
            return (!wasCancelled, s.Started ? s.Queue : null);
        });
        if (!first) return;
        socket.Close();
        queue?.Async(() => StateUpdateHandler?.Invoke(State.Cancelled));
    }

    private bool IsCancelled => status.WithLock((ref (bool Started, bool Cancelled, SerialQueue? Queue) s) => s.Cancelled);

    private async Task AcceptLoop(SerialQueue queue)
    {
        while (true)
        {
            Socket accepted;
            try
            {
                accepted = await socket.AcceptAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is SocketException or ObjectDisposedException)
            {
                // A closed socket is the cancellation; anything else is the failed state.
                if (!IsCancelled) queue.Async(() => StateUpdateHandler?.Invoke(State.Failed));
                return;
            }
            var connection = new NWConnection(accepted);
            queue.Async(() =>
            {
                if (NewConnectionHandler is { } handler) handler(connection);
                else connection.Cancel();
            });
        }
    }
}

/// <summary>
/// Swift <c>NWConnection</c> for an accepted TCP connection: <c>receive(minimumIncompleteLength:maximumLength:)</c>,
/// <c>send(content:completion: .contentProcessed)</c> and <c>cancel()</c> (MCPOAuth.swift:470, :569, :472). Each
/// completion runs on the queue passed to <see cref="Start"/>. A connection nobody cancels stays open until it is
/// collected, as an NWConnection that is never cancelled does.
/// </summary>
public sealed class NWConnection
{
    private readonly Socket socket;
    private readonly Mutex<(bool Cancelled, SerialQueue? Queue)> status = new((false, null));

    internal NWConnection(Socket socket) => this.socket = socket;

    /// <summary><c>connection.start(queue:)</c> (:448).</summary>
    public void Start(SerialQueue queue) =>
        status.WithLock((ref (bool Cancelled, SerialQueue? Queue) s) => s.Queue = queue);

    /// <summary>
    /// One <c>receive</c>: at least one byte and at most <paramref name="maximumLength"/>. The completion gets the data
    /// (nil when none), whether the peer finished sending (isComplete) and the error, if any.
    /// </summary>
    public void Receive(int minimumIncompleteLength, int maximumLength, Action<byte[]?, bool, Exception?> completion)
    {
        _ = minimumIncompleteLength; // A socket read returns as soon as one byte is available, which is 1 here.
        _ = ReceiveOnce(maximumLength, completion);
    }

    /// <summary><c>connection.send(content:completion: .contentProcessed { error in })</c>.</summary>
    public void Send(byte[] content, Action<Exception?> completion) => _ = SendAll(content, completion);

    /// <summary><c>connection.cancel()</c>: closes the socket at once; idempotent.</summary>
    public void Cancel()
    {
        var first = status.WithLock((ref (bool Cancelled, SerialQueue? Queue) s) =>
        {
            var wasCancelled = s.Cancelled;
            s.Cancelled = true;
            return !wasCancelled;
        });
        if (!first) return;
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
            // Already reset by the peer.
        }
        socket.Close();
    }

    private SerialQueue Queue =>
        status.WithLock((ref (bool Cancelled, SerialQueue? Queue) s) => s.Queue)
        ?? throw SwiftTraps.Fatal("NWConnection used before start(queue:).");

    private async Task ReceiveOnce(int maximumLength, Action<byte[]?, bool, Exception?> completion)
    {
        var queue = Queue;
        var buffer = new byte[maximumLength];
        int count;
        Exception? error = null;
        try
        {
            count = await socket.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is SocketException or ObjectDisposedException)
        {
            count = 0;
            error = failure;
        }
        var data = count > 0 ? buffer[..count] : null;
        var isComplete = count == 0 && error is null;
        queue.Async(() => completion(data, isComplete, error));
    }

    private async Task SendAll(byte[] content, Action<Exception?> completion)
    {
        var queue = Queue;
        Exception? error = null;
        try
        {
            var sent = 0;
            while (sent < content.Length)
            {
                sent += await socket.SendAsync(content.AsMemory(sent), SocketFlags.None).ConfigureAwait(false);
            }
        }
        catch (Exception failure) when (failure is SocketException or ObjectDisposedException)
        {
            error = failure;
        }
        queue.Async(() => completion(error));
    }
}
