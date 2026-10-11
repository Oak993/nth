// PORT-ONLY: Ignis Advisor, the hosted route (ED-NEW-ignisadvisor-2). How ask_advisor reaches a CLI provider: an MCP
// server entry that launches nth's own executable as a stdio relay (as the skills relay, SkillsMcpBridge, does) into one
// process-wide named pipe this process serves. Each advised session registers once at launch and gets its own token; the
// relay sends the token as its first line, so the pipe knows the chat and the advisor. A session whose chat has no advisor
// registers nothing and gets no entry, so the tool is listed only in advised chats.
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Advisor;

public static class AdvisorMcpBridge
{
    public const string Command = "--marrow-advisor-mcp";
    private const string Prefix = "MarrowAdvisor-";
    private static readonly ConcurrentDictionary<string, AdvisorRegistration> registrations = new(StringComparer.Ordinal);
    private static readonly Lazy<string> pipeName = new(StartServing, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The executable the relay entry launches: the app's own (its Main hands <see cref="Command"/> to <see cref="RunClient"/>).</summary>
    public static Ambient<Func<string>> Executable { get; } = new(new Func<string>(() => AppExecutable.Path));

    /// <summary>
    /// The advisor server for a session, registered for its chat; null when the chat has no advisor (AdvisorSeams.Choice
    /// is null for it), so nothing is added to the session's MCP servers.
    /// </summary>
    public static MCPResolvedServer? Server(SessionConfiguration configuration)
    {
        if (AdvisorTool.Registration(configuration) is not { } registration) return null;
        var token = Guid.NewGuid().ToString("N");
        registrations[token] = registration;
        return new MCPResolvedServer(AdvisorMcpServer.ServerName, AdvisorMcpServer.ServerName)
        {
            Command = Executable.Current(),
            Args = new ValueList<string>([Command, pipeName.Value, token]),
        };
    }

    /// <summary>The registration a token stands for; null when it stands for none.</summary>
    public static AdvisorRegistration? Registration(string? token) =>
        token is not null && registrations.TryGetValue(token, out var registration) ? registration : null;

    /// <summary>The ACP form of an MCP server entry ({name, command, args, env: []}), as the skills relay's.</summary>
    public static JSONValue AcpEntry(MCPResolvedServer server) => JSONValue.FromDictionaryLiteral(
        ("name", server.Id), ("command", server.Command ?? ""),
        ("args", new JSONValue.Array(new ValueList<JSONValue>(server.Args.Select(a => (JSONValue)new JSONValue.String(a))))),
        ("env", new JSONValue.Array(ValueList<JSONValue>.Empty)));

    private static string StartServing()
    {
        var name = Prefix + Guid.NewGuid().ToString("N");
        _ = Task.Run(() => Serve(name));
        return name;
    }

    private static async Task Serve(string name)
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

    private static async Task ServeClient(NamedPipeServerStream pipe)
    {
        await using var owned = pipe.ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var writing = new SemaphoreSlim(1, 1);
        using var closed = new CancellationTokenSource();
        var running = new List<Task>();
        try
        {
            var server = new AdvisorMcpServer(Registration(await reader.ReadLineAsync().ConfigureAwait(false)));
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 1024 * 1024) throw new IOException("MCP request is too large.");
                // A long ask_advisor never holds up a ping or a second call: each line is answered on its own.
                running.Add(Task.Run(async () =>
                {
                    if (await server.Handle(line, closed.Token).ConfigureAwait(false) is not { } response) return;
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

    /// <summary>The relay child: sends its token, then copies stdio to and from the pipe. Runs before any of the app's start-up.</summary>
    public static async Task<int> RunClient(string[] args, Stream input, Stream output)
    {
        if (args.Length != 3 || args[0] != Command || !args[1].StartsWith(Prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(args[1][Prefix.Length..], "N", out _) || !Guid.TryParseExact(args[2], "N", out _)) return 1;
        try
        {
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000).ConfigureAwait(false);
            var hello = Encoding.UTF8.GetBytes(args[2] + "\n");
            await pipe.WriteAsync(hello).ConfigureAwait(false);
            var incoming = input.CopyToAsync(pipe);
            var outgoing = pipe.CopyToAsync(output);
            var finished = await Task.WhenAny(incoming, outgoing).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException) { return 1; }
    }
}
