// PORT-ONLY: SK-17. The stdio child is only a relay: the parent retains the trusted generation and verifies every read.
using System.IO.Pipes;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class SkillsMcpBridge : IDisposable
{
    public const string Command = "--marrow-skills-mcp";
    private const string Prefix = "MarrowSkills-";
    private readonly SkillsMcpServer server;
    private readonly CancellationTokenSource shutdown = new();
    private readonly object gate = new();
    private NamedPipeServerStream? active;
    private bool disposed;
    public SkillsMcpBridge(Generation generation, string executable)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An absolute nth MCP executable is required.", nameof(executable));
        server = new(generation);
        PipeName = Prefix + Guid.NewGuid().ToString("N");
        Configuration = JSONValue.FromDictionaryLiteral(("name", "marrow-skills-" + PipeName[Prefix.Length..]),
            ("command", executable), ("args", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String(Command), new JSONValue.String(PipeName)]))), ("env", new JSONValue.Array(ValueList<JSONValue>.Empty)));
        var token = shutdown.Token;
        Completion = Task.Run(() => Serve(token));
    }
    public string PipeName { get; }
    public JSONValue Configuration { get; }
    public Task Completion { get; }
    public string? AutomaticOption(JSONValue toolCall, IReadOnlyList<JSONValue> options)
    {
        lock (gate)
        {
            if (disposed || toolCall["kind"]?.JSONValueString != "other") return null;
            var name = Configuration["name"]!.JSONValueString!;
            var title = toolCall["title"]?.JSONValueString;
            if (title != name + "-list_skills: list_skills" && title != name + "-read_skill: read_skill") return null;
            return options.FirstOrDefault(o => o["kind"]?.JSONValueString == "allow_once")?["optionId"]?.JSONValueString;
        }
    }
    private async Task Serve(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            lock (gate) { if (disposed) return; active = pipe; }
            try
            {
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                while (!token.IsCancellationRequested && await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    if (line.Length > 1024 * 1024) throw new IOException("MCP request is too large.");
                    if (server.Handle(line) is { } response) await writer.WriteLineAsync(response.AsMemory(), token).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            finally { lock (gate) active = null; }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            server.Dispose();
            shutdown.Cancel();
            active?.Dispose();
            _ = Completion.ContinueWith(_ => shutdown.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    // Runs before the app's desktop/Velopack/single-instance/data prologue. No generation manifest is serialized to disk.
    public static async Task<int> RunClient(string[] args, Stream input, Stream output)
    {
        if (args.Length != 2 || args[0] != Command || !args[1].StartsWith(Prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(args[1][Prefix.Length..], "N", out _)) return 1;
        try
        {
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000).ConfigureAwait(false);
            var incoming = input.CopyToAsync(pipe);
            var outgoing = pipe.CopyToAsync(output);
            var finished = await Task.WhenAny(incoming, outgoing).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException) { return 1; }
    }
}
