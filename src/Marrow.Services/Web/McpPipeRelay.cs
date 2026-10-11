// PORT-ONLY: web surfaces W12 (ED-NEW-web-w12-1). How one of nth's built-in MCP servers reaches a CLI provider: the
// provider launches nth's own executable as a stdio relay, which connects to one process-wide named pipe this process
// serves and sends the session's token as its first line; every later line is a JSON-RPC message the server answers on
// its own, so a slow call never holds up a ping. The same shape as the advisor's relay (AdvisorMcpBridge), made reusable
// for nth-ui (and nth-pc's web tools later). The pipe is the current user's only. No Swift counterpart.
using System.IO.Pipes;
using System.Text;

namespace Marrow.Services.Web;

/// <summary>Answers one JSON-RPC line of a connection; null for a notification.</summary>
public delegate Task<string?> McpLineHandler(string line, CancellationToken cancellationToken);

public sealed class McpPipeRelay
{
    private const int MaxLine = 16 * 1024 * 1024; // an artifact_create of a 5 MB version, JSON-escaped, fits
    private readonly string prefix;
    private readonly Func<string?, McpLineHandler> connect;
    private readonly Lazy<string> pipeName;

    /// <param name="prefix">The pipe name's prefix; a random id follows it.</param>
    /// <param name="connect">The handler for a connection, from the token its relay sent (null when it sent none).</param>
    public McpPipeRelay(string prefix, Func<string?, McpLineHandler> connect)
    {
        this.prefix = prefix;
        this.connect = connect;
        pipeName = new Lazy<string>(StartServing, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The pipe's name; the first read starts serving it.</summary>
    public string PipeName => pipeName.Value;

    private string StartServing()
    {
        var name = prefix + Guid.NewGuid().ToString("N");
        _ = Task.Run(() => Serve(name));
        return name;
    }

    private async Task Serve(string name)
    {
        while (true)
        {
            var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync().ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(250).ConfigureAwait(false);
                continue;
            }
            _ = Task.Run(() => ServeClient(pipe));
        }
    }

    private async Task ServeClient(NamedPipeServerStream pipe)
    {
        await using var owned = pipe.ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var writing = new SemaphoreSlim(1, 1);
        using var closed = new CancellationTokenSource();
        var running = new List<Task>();
        try
        {
            var handler = connect(await reader.ReadLineAsync().ConfigureAwait(false));
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > MaxLine) throw new IOException("MCP request is too large.");
                running.RemoveAll(task => task.IsCompleted); // a long session's finished calls are not kept
                running.Add(Task.Run(async () =>
                {
                    if (await handler(line, closed.Token).ConfigureAwait(false) is not { } response) return;
                    await writing.WaitAsync(closed.Token).ConfigureAwait(false);
                    try { await writer.WriteLineAsync(response).ConfigureAwait(false); }
                    finally { writing.Release(); }
                }));
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            await closed.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(running).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// The relay child: checks its arguments ([command, pipe, token]), sends the token, then copies stdio to and from the
    /// pipe. Runs before any of the app's start-up; 1 when the arguments are not its own or the pipe is gone.
    /// </summary>
    public static async Task<int> RunClient(string command, string prefix, string[] args, Stream input, Stream output)
    {
        if (args.Length != 3 || args[0] != command || !args[1].StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(args[1][prefix.Length..], "N", out _) || !Guid.TryParseExact(args[2], "N", out _)) return 1;
        try
        {
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000).ConfigureAwait(false);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(args[2] + "\n")).ConfigureAwait(false);
            var incoming = input.CopyToAsync(pipe);
            var outgoing = pipe.CopyToAsync(output);
            await (await Task.WhenAny(incoming, outgoing).ConfigureAwait(false)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException) { return 1; }
    }
}
