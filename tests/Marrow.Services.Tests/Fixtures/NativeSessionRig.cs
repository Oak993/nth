using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture (SK-11f, R-TEST-10). Drives one real OpenAICompatibleSession through its whole turn loop against a scripted
// answerer on 127.0.0.1: each request the session sends is recorded as raw bytes, and each is answered with the next committed
// transcript of Fixtures/skills-native (OpenAI chat-completions SSE, the format the session's own parser reads). No network, no real
// provider, no real user folder: the storage root is a throwaway folder, the clock is the test runtime's.
internal sealed class NativeSessionRig : IAsyncDisposable
{
    private readonly FakeUpstreamServer server;
    private readonly TestRuntime runtime = new();
    private readonly StorageScratchFolder scratch = new();
    private readonly Queue<string> transcripts;
    private readonly List<byte[]> bodies = [];

    private NativeSessionRig(IEnumerable<string> transcriptFiles, RuntimeMode runtimeMode, InteractionMode interactionMode,
        Action<OpenAICompatibleSession>? configure)
    {
        transcripts = new Queue<string>(transcriptFiles.Select(Transcript));
        server = new FakeUpstreamServer { Handler = Answer };
        Session = new OpenAICompatibleSessionCapturingSeam(
            MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration(runtimeMode, interactionMode)).Wired();
        Session.SeedMcpToolsForTests([]);
        configure?.Invoke(Session);
    }

    public OpenAICompatibleSessionCapturingSeam Session { get; }
    public TestRuntime Runtime => runtime;
    public string StorageRoot => scratch.Root;
    public IReadOnlyList<byte[]> RequestBodies => bodies;
    public IReadOnlyList<ProviderEvent> Events => Session.Events;

    // Synchronous on purpose: TestRuntime and StorageScratchFolder bind ambients for the calling async flow, and an async factory would
    // hand the caller back its own flow without them.
    public static NativeSessionRig Create(string[] transcriptFiles, RuntimeMode runtimeMode = RuntimeMode.FullAccess,
        InteractionMode interactionMode = InteractionMode.Build, Action<OpenAICompatibleSession>? configure = null) =>
        new(transcriptFiles, runtimeMode, interactionMode, configure);

    public async Task<NativeSessionRig> Begin()
    {
        await Session.Start();
        return this;
    }

    public async Task Turn(string text, RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build)
    {
        var task = Session.Send(new TurnInput
        {
            Text = text,
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
            IsFinalReport = false,
        });
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    public string Body(int index) => System.Text.Encoding.UTF8.GetString(bodies[index]);

    public JSONValue Json(int index) => JSONValue.Parse(bodies[index])!;

    public static string Transcript(string file) => File.ReadAllText(Path.Combine(FixtureDirectory(), "skills-native", file));

    public static string GoldenPath(string file) => Path.Combine(FixtureDirectory(), "skills-native", file);

    private static string FixtureDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private FakeResponse Answer(FakeRequest request)
    {
        lock (bodies) bodies.Add(request.Body);
        var next = transcripts.Count == 0 ? throw new InvalidOperationException("Test bug: no transcript left for this request.") : transcripts.Dequeue();
        return new FakeResponse(200, [("Content-Type", "text/event-stream")], [], [next]);
    }

    public static OpenAICompatibleSession.Config MakeConfig(Uri chatUrl) => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = chatUrl,
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

    public static SessionConfiguration MakeSessionConfiguration(RuntimeMode runtimeMode = RuntimeMode.FullAccess,
        InteractionMode interactionMode = InteractionMode.Build) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
        ApiKey = "sk-test",
    };

    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
        scratch.Dispose();
        runtime.Dispose();
    }
}
