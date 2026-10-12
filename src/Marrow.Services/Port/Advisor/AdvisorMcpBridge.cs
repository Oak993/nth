// PORT-ONLY: Ignis Advisor, the hosted route (ED-NEW-ignisadvisor-2). How ask_advisor reaches a CLI provider: an MCP
// server entry that launches nth's own executable as a stdio relay (as the skills relay, SkillsMcpBridge, does) into one
// process-wide named pipe this process serves. Each advised session registers once at launch and gets its own token; the
// relay sends the token as its first line, so the pipe knows the chat and the advisor. A session whose chat has no advisor
// registers nothing and gets no entry, so the tool is listed only in advised chats.
// ED-NEW-lean-18b: a registration lives while its session does. The session's relay holds the token's pipe connection;
// when the last one closes (the CLI ended and took its relay with it) the registration is dropped after a short grace,
// which a relay reconnecting with the same token cancels.
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
    private static readonly ConcurrentDictionary<string, Entry> registrations = new(StringComparer.Ordinal);

    /// <summary>How long a registration outlives its last relay connection.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    /// <summary>The clock the grace runs on (a test sets a manual one).</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Raised with the token when its last relay connection has closed and the grace has started.</summary>
    internal static event Action<string>? RelayEnded;
    private static readonly Lazy<string> pipeName = new(StartServing, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The executable the relay entry launches: the app's own (its Main hands <see cref="Command"/> to <see cref="RunClient"/>).</summary>
    public static Ambient<Func<string>> Executable { get; } = new(new Func<string>(() => Path.Combine(AppContext.BaseDirectory, "Marrow.App.exe")));

    /// <summary>
    /// The advisor server for a session, registered for its chat; null when the chat has no advisor (AdvisorSeams.Choice
    /// is null for it), so nothing is added to the session's MCP servers.
    /// </summary>
    public static MCPResolvedServer? Server(SessionConfiguration configuration) =>
        AdvisorTool.Registration(configuration) is { } registration ? Register(registration) : null;

    /// <summary>Registers one advised session under a new token and returns its relay entry.</summary>
    internal static MCPResolvedServer Register(AdvisorRegistration registration)
    {
        var token = Guid.NewGuid().ToString("N");
        registrations[token] = new Entry(registration);
        return new MCPResolvedServer(AdvisorMcpServer.ServerName, AdvisorMcpServer.ServerName)
        {
            Command = Executable.Current(),
            Args = new ValueList<string>([Command, pipeName.Value, token]),
        };
    }

    /// <summary>The registration a token stands for; null when it stands for none.</summary>
    public static AdvisorRegistration? Registration(string? token) =>
        token is not null && registrations.TryGetValue(token, out var entry) ? entry.Registration : null;

    /// <summary>Whether the bridge still holds the registration of a token.</summary>
    internal static bool Holds(string token) => registrations.ContainsKey(token);

    /// <summary>A relay connected with this token: the registration it stands for, with any pending drop cancelled.</summary>
    private static Entry? Attach(string? token)
    {
        if (token is null || !registrations.TryGetValue(token, out var entry)) return null;
        lock (entry)
        {
            entry.Relays++;
            entry.Drop?.Dispose();
            entry.Drop = null;
        }
        return entry;
    }

    /// <summary>A relay connection closed; the last one starts the grace after which the registration is dropped.</summary>
    private static void Detach(string token, Entry entry)
    {
        lock (entry)
        {
            if (--entry.Relays > 0) return;
            entry.Drop?.Dispose();
            entry.Drop = Clock.CreateTimer(_ => Drop(token, entry), null, Grace, Timeout.InfiniteTimeSpan);
        }
        RelayEnded?.Invoke(token);
    }

    private static void Drop(string token, Entry entry)
    {
        lock (entry)
        {
            if (entry.Relays > 0) return;
            entry.Drop?.Dispose();
            entry.Drop = null;
            registrations.TryRemove(new KeyValuePair<string, Entry>(token, entry));
        }
    }

    /// <summary>
    /// The wait before listening again after the pipe failed: 250 ms, doubling on each failure in a row up to 4 s, which
    /// stays inside the relay's 5 s connect window; a connection resets it.
    /// </summary>
    internal static TimeSpan RetryDelay(int failures) => TimeSpan.FromMilliseconds(Math.Min(4000, 250 << Math.Clamp(failures - 1, 0, 4)));

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
        var failures = 0;
        while (true)
        {
            var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync().ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(RetryDelay(++failures)).ConfigureAwait(false);
                continue;
            }
            failures = 0;
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
        string? token = null;
        Entry? entry = null;
        try
        {
            token = await reader.ReadLineAsync().ConfigureAwait(false);
            entry = Attach(token);
            var server = new AdvisorMcpServer(entry?.Registration);
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
            // Only the token lookup goes, and only after the grace: a relay that reconnects with it keeps its registration.
            if (entry is not null) Detach(token!, entry);
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

    /// <summary>One registration and its live relay connections (guarded by the entry itself).</summary>
    private sealed class Entry(AdvisorRegistration registration)
    {
        public AdvisorRegistration Registration { get; } = registration;
        public int Relays { get; set; }
        public ITimer? Drop { get; set; }
    }
}
