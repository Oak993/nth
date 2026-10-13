// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-10; DESIGN.md 3.3). How a CLI or ACP provider
// reaches the nth-pc tools: an MCP server entry that launches nth's own executable as a stdio relay
// (`--marrow-pc-mcp <pipe> <token>`, as the advisor's and the skills' relays) into one process-wide named pipe this process
// serves, CurrentUserOnly. A session gets the entry only while Tactus is on and the app has bound its parts; its token
// stands for the chat (thread, provider, model, whether the model takes images), and the relay sends it as its first line.
// One engine serves each chat thread, made on first use from the app's shared parts, so approvals, the pill, Stop and the
// log stay in one place. Lines of one connection are answered concurrently, replies written one at a time.
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.PcControl;

public static class PcControlBridge
{
    public const string Command = "--marrow-pc-mcp";
    private const string Prefix = "MarrowPc-";

    /// <summary>The longest JSON-RPC line a connection accepts.</summary>
    public const int MaxLineChars = 1024 * 1024;

    private static readonly ConcurrentDictionary<string, PcRegistration> registrations = new(StringComparer.Ordinal);
    // ED-NEW-mission-pc-2: an ordinary engine cannot lend its grants to a seal. Unsealed chats retain the old thread-only key.
    private static readonly ConcurrentDictionary<(string Thread, Guid? Mission, string? Head, string? Model, bool Vision), PcControlEngine> engines = new();
    private static readonly Lazy<string> pipeName = new(StartServing, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The executable the relay entry launches: the app's own (its Main hands <see cref="Command"/> to <see cref="RunClient"/>). The
    /// published app is nth.exe and a build keeps the SDK's apphost name, so the name is never built by hand: the running process's
    /// own path, as the advisor's and the skills' relays take it (<see cref="AppExecutable"/>, ED-NEW-inst-u7-1; ED-NEW-inst-rimerge-1).
    /// </summary>
    public static Ambient<Func<string>> Executable { get; } = new(new Func<string>(() => AppExecutable.Path));

    /// <summary>The parts every chat's engine drives, bound by the app; null until bound, and then no session gets the entry.</summary>
    public static Ambient<Func<PcEngineParts?>> Parts { get; } = new(new Func<PcEngineParts?>(() => null));

    /// <summary>
    /// A chat thread's parts (its own audit log; ED-NEW-pcctl-40); the shared <see cref="Parts"/> until the app binds it.
    /// </summary>
    public static Ambient<Func<string, PcEngineParts?>> PartsFor { get; } = new(new Func<string, PcEngineParts?>(_ => Parts.Current()));

    /// <summary>
    /// Whether a Hydra head's run is attended now (critique C14); the app binds the real answer. Attended until bound. The
    /// lead chat is never limited by it (DESIGN.md 2.10), so it is asked only for heads.
    /// </summary>
    public static Ambient<Func<string, bool>> Attended { get; } = new(new Func<string, bool>(_ => true));

    /// <summary>
    /// The nth-pc server for a session, registered for its chat; null while Tactus is off or unbound, so nothing is added
    /// to the session's MCP servers.
    /// </summary>
    public static MCPResolvedServer? Server(SessionConfiguration configuration)
    {
        if (Registration(configuration) is not { } registration) return null;
        var token = Guid.NewGuid().ToString("N");
        registrations[token] = registration;
        return new MCPResolvedServer(PcControlMcpServer.ServerName, PcControlMcpServer.ServerName)
        {
            Command = Executable.Current(),
            Args = new ValueList<string>([Command, pipeName.Value, token]),
        };
    }

    /// <summary>Tactus is bound and its switch is on (an nth-pc server may also be given for Visus alone, W11).</summary>
    public static bool TactusOn => Parts.Current() is { } parts && parts.Settings().Enabled;

    /// <summary>
    /// The chat a session serves, when Tactus is on and bound, or (PORT-ONLY, web surfaces W11, ED-NEW-web-w11-1) when Visus model
    /// access is on; null otherwise.
    /// </summary>
    public static PcRegistration? Registration(SessionConfiguration configuration)
    {
        // ED-NEW-mission-pc-1 supersedes ED-NEW-secfinal-1: every sealed adapter waits on Dextra before executing.
        if (!TactusOn && !Marrow.Services.Web.WebAgentHub.Shared.Current.Offered) return null;
        var thread = configuration.IgnisThreadID is { } id ? id.ToString("D") : "session-" + Guid.NewGuid().ToString("N");
        return new PcRegistration(thread, configuration.Provider, configuration.Model, configuration.Provider.SupportsImages)
        {
            Head = configuration.IsHydraHead ? "a Hydra head" : configuration.Sealed is not null ? "a mission chat" : null,
            MissionId = configuration.Sealed?.MissionId,
            MissionHost = configuration.Sealed is not null ? Marrow.Services.Assistant.AssistantMcpBridge.Host.Current : null,
            ImageFolder = PcProviderWiring.ImageFolder(configuration.Provider, thread),
        };
    }

    /// <summary>The registration a token stands for; null when it stands for none.</summary>
    public static PcRegistration? Registration(string? token) =>
        token is not null && registrations.TryGetValue(token, out var registration) ? registration : null;

    /// <summary>ED-NEW-mission-pc-1: an execution's own identity. Ordinary chats and unsealed Hydra heads keep their old path.</summary>
    public static PcRegistration ForCall(PcRegistration registration) => registration.MissionId is null || registration.CallId is not null
        ? registration : registration with { CallId = Guid.NewGuid().ToString("N") };

    /// <summary>The common sealed-call gate of MCP and API adapters. Every declared batch step is also decided, before the batch acts.</summary>
    public static async Task<bool> ApproveMission(PcRegistration registration, string tool, System.Text.Json.Nodes.JsonObject arguments, CancellationToken cancellationToken)
    {
        if (registration.MissionId is null) return true;
        var host = registration.MissionHost ?? Marrow.Services.Assistant.AssistantMcpBridge.Host.Current;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await host.ApprovePc(registration, tool, (System.Text.Json.Nodes.JsonObject)arguments.DeepClone(), cancellationToken).ConfigureAwait(false)) return false;
            if (!Attended.Current(registration.Thread)) return false;
            if (tool == Marrow.Core.Port.PcControl.PcToolSchemas.Batch)
            {
                if (arguments["actions"] is not System.Text.Json.Nodes.JsonArray steps || steps.Count is < 2 or > Marrow.Core.Port.PcControl.PcToolSchemas.MaxBatchSteps) return false;
                foreach (var node in steps)
                {
                    if (node is not System.Text.Json.Nodes.JsonObject step || step["tool"] is not System.Text.Json.Nodes.JsonValue value
                        || !value.TryGetValue<string>(out var name) || !Marrow.Core.Port.PcControl.PcToolSchemas.BatchSteps.Contains(name)) return false;
                    var input = (System.Text.Json.Nodes.JsonObject)step.DeepClone();
                    input.Remove("tool");
                    input.Remove("bind");
                    if (!input.ContainsKey("window")) input["window"] = arguments["window"]?.DeepClone();
                    if (!await ApproveMission(registration with { CallId = Guid.NewGuid().ToString("N") }, name, input, cancellationToken).ConfigureAwait(false)) return false;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException)) { return false; }
    }

    /// <summary>The response outcome is logged without any returned content. An unavailable audit never silently succeeds.</summary>
    public static bool CompletedMission(PcRegistration registration, string tool, System.Text.Json.Nodes.JsonObject arguments, string outcome)
    {
        if (registration.MissionId is null) return true;
        try { return (registration.MissionHost ?? Marrow.Services.Assistant.AssistantMcpBridge.Host.Current).PcCompleted(registration, tool, arguments, outcome); }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException)) { return false; }
    }

    public static Marrow.Core.Port.PcControl.PcResult MissionRefusal() => Marrow.Core.Port.PcControl.PcResult.Fail(
        Marrow.Core.Port.PcControl.PcErrorCode.NotApproved, "the user did not allow this step");

    public static Marrow.Core.Port.PcControl.PcResult MissionAuditFailure() => Marrow.Core.Port.PcControl.PcResult.Fail(
        Marrow.Core.Port.PcControl.PcErrorCode.NotApproved, "the activity log could not record the result; do not repeat the step");

    public static string Outcome(Marrow.Core.Port.PcControl.PcResult result) => result.Error is not null ? "failed"
        : result.Status == Marrow.Core.Port.PcControl.PcStatus.WaitingForUser ? "waiting_for_user"
        : result.Status == Marrow.Core.Port.PcControl.PcStatus.Unverified ? "unverified" : "done";

    /// <summary>The chat's engine, made on first use from the bound parts; null while unbound.</summary>
    public static PcControlEngine? Engine(PcRegistration registration)
    {
        var key = registration.MissionId is null ? (registration.Thread, (Guid?)null, (string?)null, (string?)null, false)
            : (registration.Thread, registration.MissionId, registration.Head, registration.Model, registration.Vision);
        if (engines.TryGetValue(key, out var engine)) return engine;
        var thread = registration.Thread;
        if (Parts.Current() is null || PartsFor.Current(thread) is not { } parts) return null;
        var head = registration.Head is not null;
        var made = new PcControlEngine(parts, new PcCaller(thread, () => !head || Attended.Current(thread), registration.Vision)
        {
            Head = registration.Head,
            Model = registration.Model,
        });
        var kept = engines.GetOrAdd(key, made);
        if (!ReferenceEquals(kept, made)) made.Dispose();
        return kept;
    }

    /// <summary>The chat threads with an engine now (the pill's Stop interrupts their turns).</summary>
    public static IReadOnlyList<string> Threads => [.. engines.Keys.Select(key => key.Thread).Distinct(StringComparer.Ordinal)];

    /// <summary>The chat's Stop: its queued steps are cancelled and no step starts again this turn.</summary>
    public static void StopTurn(string thread)
    {
        foreach (var entry in engines.Where(entry => entry.Key.Thread == thread)) entry.Value.StopTurn();
        Marrow.Services.Web.WebAgentHub.Shared.Current.StopTurn(thread); // + ED-NEW-web-w11-1: the web steps stop with it
    }

    /// <summary>The chat's turn ended: its queued steps are cancelled and its windows are released.</summary>
    public static void EndTurn(string thread)
    {
        foreach (var entry in engines.Where(entry => entry.Key.Thread == thread)) entry.Value.EndTurn();
        Marrow.Services.Web.WebAgentHub.Shared.Current.EndTurn(thread); // + ED-NEW-web-w11-1
    }

    /// <summary>The chat closed: its engine goes.</summary>
    public static void Forget(string thread)
    {
        foreach (var entry in engines.Where(entry => entry.Key.Thread == thread))
            if (engines.TryRemove(entry.Key, out var engine)) engine.Dispose();
        Marrow.Services.Web.WebAgentHub.Shared.Current.Forget(thread); // + ED-NEW-web-w11-1
    }

    /// <summary>The ACP form of the entry ({name, command, args, env: []}), as the advisor's.</summary>
    public static JSONValue AcpEntry(MCPResolvedServer server) => JSONValue.FromDictionaryLiteral(
        ("name", server.Id), ("command", server.Command ?? ""),
        ("args", new JSONValue.Array(new ValueList<JSONValue>(server.Args.Select(a => (JSONValue)new JSONValue.String(a))))),
        ("env", new JSONValue.Array(ValueList<JSONValue>.Empty)));

    /// <summary>
    /// Serves one connection: the first line is the token, then every line is handled on its own task, so a long call never
    /// holds up a ping or a second call; replies and progress notes are written one at a time. A closed pipe cancels every
    /// call of the connection.
    /// </summary>
    public static async Task Serve(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var writing = new SemaphoreSlim(1, 1);
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = new List<Task>();
        async Task Write(string line)
        {
            await writing.WaitAsync(closed.Token).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }
            finally
            {
                writing.Release();
            }
        }
        try
        {
            var registration = Registration(await reader.ReadLineAsync(closed.Token).ConfigureAwait(false));
            var server = new PcControlMcpServer(registration, Engine, () => Parts.Current()?.Settings() ?? PcSettings.Default, Write)
            {
                Web = Marrow.Services.Web.WebMcpTools.Shared, // + ED-NEW-web-w11-1
            };
            while (await reader.ReadLineAsync(closed.Token).ConfigureAwait(false) is { } line)
            {
                if (line.Length > MaxLineChars) throw new IOException("MCP request is too large.");
                running.RemoveAll(task => task.IsCompleted);
                running.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (await server.Handle(line, closed.Token).ConfigureAwait(false) is { } response) await Write(response).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
                    {
                    }
                }));
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            await closed.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    private static string StartServing()
    {
        var name = Prefix + Guid.NewGuid().ToString("N");
        _ = Task.Run(() => Listen(name));
        return name;
    }

    private static async Task Listen(string name)
    {
        while (true)
        {
            var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(250).ConfigureAwait(false);
                continue;
            }
            _ = Task.Run(async () =>
            {
                await using var owned = pipe.ConfigureAwait(false);
                await Serve(pipe, CancellationToken.None).ConfigureAwait(false);
            });
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
            await (await Task.WhenAny(incoming, outgoing).ConfigureAwait(false)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return 1;
        }
    }
}
