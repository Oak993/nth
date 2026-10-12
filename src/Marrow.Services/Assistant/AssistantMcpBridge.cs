// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.5 "Transport"; unit AS-4; ED-NEW-dextra-as4-3). No Swift twin.
// How the nth-dextra tools reach a CLI provider, on the advisor relay pattern (Port/Advisor/AdvisorMcpBridge.cs): an MCP
// server entry that launches nth's own executable as a stdio relay into one process-wide, current-user-only named pipe
// this process serves. Each Dextra chat's session registers when it launches and gets its own token; the relay sends the
// token as its first line, so the pipe knows the chat. A token that is unknown, or that Forget has revoked, lists no
// tools and runs none. A chat the host does not know as Dextra's registers nothing and gets no entry.
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Assistant;

/// <summary>One launch of a Dextra chat: its token, its chat, and the host its calls go to.</summary>
public sealed record AssistantRegistration(string Token, AssistantChat Chat, IAssistantToolHost Host)
{
    /// <summary>Whether the token still stands for this chat (Forget revokes it).</summary>
    public bool IsLive => AssistantMcpBridge.Registration(Token) is { } current && ReferenceEquals(current, this);
}

public static class AssistantMcpBridge
{
    public const string Command = "--marrow-dextra-mcp";
    private const string Prefix = "MarrowDextra-";
    private static readonly ConcurrentDictionary<string, AssistantRegistration> registrations = new(StringComparer.Ordinal);
    private static readonly Lazy<string> pipeName = new(StartServing, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The controller behind the tools. AS-5 binds it with SetProcessDefault; until then it knows no chat, so no session
    /// is given the tools. A registration keeps the host it was made with, so a later rebinding never reaches it.
    /// </summary>
    public static Ambient<IAssistantToolHost> Host { get; } = new(NoAssistantToolHost.Instance);

    /// <summary>The executable the relay entry launches: the app's own (its Main hands <see cref="Command"/> to <see cref="RunClient"/>).</summary>
    public static Ambient<Func<string>> Executable { get; } = new(new Func<string>(() => Path.Combine(AppContext.BaseDirectory, "Marrow.App.exe")));

    /// <summary>
    /// Registers a session's chat when the host knows it as Dextra's; null otherwise (no thread, an unknown chat, or a
    /// host that throws). Each call makes a new token for the chat.
    /// </summary>
    public static AssistantRegistration? Register(SessionConfiguration configuration)
    {
        if (configuration.IgnisThreadID is not { } thread) return null;
        var host = Host.Current;
        AssistantChatIdentity? identity;
        try { identity = host.Identify(thread); }
        catch (Exception error) when (error is not OperationCanceledException) { return null; }
        if (identity is null || string.IsNullOrWhiteSpace(identity.AssistantId)) return null;
        var chat = new AssistantChat(thread, identity.AssistantId, identity.MissionId, identity.Role, configuration.Provider, configuration.Model);
        var registration = new AssistantRegistration(Guid.NewGuid().ToString("N"), chat, host);
        registrations[registration.Token] = registration;
        return registration;
    }

    /// <summary>The nth-dextra server entry for a session, registered for its chat; null when the chat is not Dextra's.</summary>
    public static MCPResolvedServer? Server(SessionConfiguration configuration)
    {
        if (Register(configuration) is not { } registration) return null;
        return new MCPResolvedServer(AssistantMcpServer.ServerName, AssistantMcpServer.ServerName)
        {
            Command = Executable.Current(),
            Args = new ValueList<string>([Command, pipeName.Value, registration.Token]),
        };
    }

    /// <summary>The registration a token stands for; null when it stands for none.</summary>
    public static AssistantRegistration? Registration(string? token) =>
        token is not null && registrations.TryGetValue(token, out var registration) ? registration : null;

    /// <summary>The token an nth-dextra entry carries; null for any other entry.</summary>
    public static string? Token(MCPResolvedServer server) =>
        server.Id == AssistantMcpServer.ServerName && server.Args.Count == 3 && server.Args[0] == Command ? server.Args[2] : null;

    /// <summary>Revokes every token of a chat (its mission ended, it was deleted, or it stopped being Dextra's).</summary>
    public static void Forget(Guid thread)
    {
        foreach (var (token, registration) in registrations)
            if (registration.Chat.Thread == thread) registrations.TryRemove(new KeyValuePair<string, AssistantRegistration>(token, registration));
    }

    /// <summary>The ACP form of the entry ({name, command, args, env: []}), as the advisor's.</summary>
    public static JSONValue AcpEntry(MCPResolvedServer server) => Port.Advisor.AdvisorMcpBridge.AcpEntry(server);

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

    /// <summary>
    /// One relay's connection: its first line is its token, then one JSON-RPC line in, one out. Each line is answered on
    /// its own task (a long call never holds up a ping), and replies are written one at a time.
    /// </summary>
    public static async Task ServeClient(Stream pipe)
    {
        await using var owned = pipe.ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var writing = new SemaphoreSlim(1, 1);
        using var closed = new CancellationTokenSource();
        var running = new List<Task>();
        try
        {
            var server = new AssistantMcpServer(Registration(await reader.ReadLineAsync().ConfigureAwait(false)));
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > AssistantMcpServer.MaxLine) throw new IOException("MCP request is too large.");
                running.RemoveAll(t => t.IsCompleted);
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
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(args[2] + "\n")).ConfigureAwait(false);
            var incoming = input.CopyToAsync(pipe);
            var outgoing = pipe.CopyToAsync(output);
            var finished = await Task.WhenAny(incoming, outgoing).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException) { return 1; }
    }

    /// <summary>The pipe's name, starting the server on first use (for the relay round-trip test).</summary>
    internal static string PipeName => pipeName.Value;
}
