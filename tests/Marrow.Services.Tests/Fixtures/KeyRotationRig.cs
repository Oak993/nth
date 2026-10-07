using System.Text;
using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// MA-6: a real <see cref="OpenAICompatibleSession"/> talking to a scripted server on 127.0.0.1 (R-TEST-10), with a key pool of three
/// made-up keys. The server answers each request with the next scripted status and records the bearer and body it saw.
/// </summary>
public sealed class KeyRotationRig : IAsyncDisposable
{
    public const string Ok = "ok";

    public static readonly Guid Thread = new("00000000-0000-0000-0000-0000000000a6");

    private readonly TestRuntime runtime = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "marrow-ma6-" + Guid.NewGuid().ToString("N"));
    private readonly IDisposable storage;
    private readonly LoopbackHttpServer server = new();
    private readonly Queue<string> script;
    private readonly double? reportedCost;

    public List<(string Bearer, string Body)> Requests { get; } = [];
    public List<ProviderEvent> Events { get; } = [];
    public InMemoryApiKeyStore Store { get; } = new();
    public InMemoryApiKeyCredits Credits { get; } = new();
    public InMemoryApiKeySpend Spend { get; } = new();
    public ApiKeyPool Pool { get; }
    public OpenAICompatibleSession Session { get; }
    public ManualTimeProvider Clock { get; } = new();
    public InMemoryKeyMovePolicy Moves { get; } = new();

    /// <param name="answers">One entry per request the server will see: a status ("402", "429", "429:7" for a Retry-After of 7, "500") or <see cref="Ok"/>.</param>
    public KeyRotationRig(IEnumerable<string> answers, bool rotate = true, bool autoMove = true, ProviderKind provider = ProviderKind.Deepseek, double? reportedCost = null)
    {
        this.reportedCost = reportedCost;
        script = new Queue<string>(answers);
        Directory.CreateDirectory(directory);
        storage = Storage.Locations.Override(StorageLocations.Create(directory, directory));
        foreach (var id in new[] { "a", "b", "c" }) Store.Add(new ApiKeyEntry(id, provider.Id, $"Key {id.ToUpperInvariant()}"), $"sk-{id}");
        Moves.Default = autoMove ? KeyMoveSetting.Allow : KeyMoveSetting.Unset; // OD-0116
        Pool = new ApiKeyPool(provider.Id, Store, Credits, Spend, Clock, moves: Moves);

        var config = new OpenAICompatibleSession.Config
        {
            Kind = provider,
            Title = provider.DisplayName,
            AgentIdentity = model => $"agent ({model})",
            DefaultModel = "deepseek-chat",
            ChatURL = server.Url("/v1/chat/completions"),
            ContextWindow = 64_000,
            IsVisionModel = _ => false,
        };
        var configuration = new SessionConfiguration
        {
            Provider = provider,
            WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
            ApiKey = rotate ? null : "sk-single",
            Transcript = ValueList<TranscriptMessage>.Empty,
        };
        Session = new OpenAICompatibleSession(config, configuration);
        if (rotate) Session.KeyRotation = Pool.ForThread(Thread);
        Session.OnEvent = Events.Add;
        _ = Serve();
    }

    private async Task Serve()
    {
        while (script.Count > 0)
        {
            var exchange = await server.NextExchange();
            var step = script.Dequeue();
            Requests.Add((exchange.HeaderValues("Authorization").FirstOrDefault() ?? "", Encoding.UTF8.GetString(exchange.Body)));
            if (step == Ok)
            {
                var cost = reportedCost is { } usd ? ",\"cost\":" + usd.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "";
                var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\n" +
                          "data: {\"usage\":{\"prompt_tokens\":60,\"completion_tokens\":40,\"total_tokens\":100" + cost + "}}\n\n" +
                          "data: [DONE]\n\n";
                await exchange.Respond(200, "OK", Encoding.UTF8.GetBytes(sse), ("Content-Type", "text/event-stream"));
                continue;
            }
            var parts = step.Split(':');
            var status = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            var body = Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"nope\"}}");
            var headers = parts.Length > 1 ? new[] { ("Retry-After", parts[1]), ("Content-Type", "application/json") } : [("Content-Type", "application/json")];
            await exchange.Respond(status, "Err", body, headers);
        }
    }

    /// <summary>Starts the session and runs one turn on the manual main loop, pumping while the real socket works. Returns the failure, if any.</summary>
    public async Task<Exception?> RunTurn()
    {
        await Session.Start();
        var send = Session.Send(new TurnInput
        {
            Text = "hello",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
        });
        runtime.Loop.RunUntil(() => send.IsCompleted);
        try { await send; return null; }
        catch (Exception error) { return error; }
    }

    /// <summary>Another turn on the session that is already started (the user answered a card and sent again).</summary>
    public async Task SendAgain()
    {
        var send = Session.Send(new TurnInput
        {
            Text = "again",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
        });
        runtime.Loop.RunUntil(() => send.IsCompleted);
        await send;
    }

    public IEnumerable<string> Notices() =>
        Events.OfType<ProviderEvent.Notice>().Select(n => n.Value.Message);

    public TestRuntime Runtime => runtime;

    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
        storage.Dispose();
        runtime.Dispose();
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
    }
}
