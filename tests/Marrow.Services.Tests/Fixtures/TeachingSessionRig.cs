using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY test fixture (web surfaces W17, ED-NEW-web-w17-1; R-TEST-10). One real OpenAICompatibleSession through its whole turn
/// loop against a scripted answerer on 127.0.0.1, as NativeSessionRig does, but fed the recorded chat-completion streams of the teaching
/// fixtures as text instead of the skills-native files. Each request the session sends is kept as raw bytes. No network, no real
/// provider, a throwaway storage root.
/// </summary>
internal sealed class TeachingSessionRig : IAsyncDisposable
{
    private readonly FakeUpstreamServer server;
    private readonly TestRuntime runtime = new();
    private readonly StorageScratchFolder scratch = new();
    private readonly Queue<string> scripted;
    private readonly List<byte[]> bodies = [];
    private readonly List<ProviderEvent> events = [];

    // Synchronous on purpose: TestRuntime and StorageScratchFolder bind ambients for the calling async flow.
    public TeachingSessionRig(IEnumerable<string> streams, SessionConfiguration configuration, Action<OpenAICompatibleSession>? configure = null)
    {
        scripted = new Queue<string>(streams);
        server = new FakeUpstreamServer { Handler = Answer };
        Session = new OpenAICompatibleSession(NativeSessionRig.MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), configuration);
        Session.OnEvent = events.Add;
        Session.SeedMcpToolsForTests([]);
        configure?.Invoke(Session);
    }

    public OpenAICompatibleSession Session { get; }

    public IReadOnlyList<ProviderEvent> Events => events;

    public IReadOnlyList<JSONValue> Requests => [.. bodies.Select(body => JSONValue.Parse(body)!)];

    public async Task Turn(string text)
    {
        await Session.Start();
        var task = Session.Send(new TurnInput
        {
            Text = text,
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
            IsFinalReport = false,
        });
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    private FakeResponse Answer(FakeRequest request)
    {
        lock (bodies) bodies.Add(request.Body);
        var next = scripted.Count == 0 ? throw new InvalidOperationException("Test bug: no recorded stream left for this request.") : scripted.Dequeue();
        return new FakeResponse(200, [("Content-Type", "text/event-stream")], [], [next]);
    }

    public async ValueTask DisposeAsync()
    {
        Session.Stop();
        await server.DisposeAsync();
        scratch.Dispose();
        runtime.Dispose();
    }
}
