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

    private readonly List<ProviderEvent> events = [];

    private NativeSessionRig(IEnumerable<string> transcriptFiles, RuntimeMode runtimeMode, InteractionMode interactionMode,
        Action<OpenAICompatibleSession>? configure, Func<OpenAICompatibleSession.Config, SessionConfiguration, OpenAICompatibleSession>? factory,
        string? workingDirectory)
    {
        transcripts = new Queue<string>(transcriptFiles.Select(Transcript));
        server = new FakeUpstreamServer { Handler = Answer };
        Session = (factory ?? ((config, configuration) => new OpenAICompatibleSession(config, configuration)))(
            MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration(runtimeMode, interactionMode, workingDirectory));
        Session.OnEvent = events.Add;
        Session.SeedMcpToolsForTests([]);
        configure?.Invoke(Session);
    }

    public OpenAICompatibleSession Session { get; }
    public TestRuntime Runtime => runtime;
    public string StorageRoot => scratch.Root;
    public IReadOnlyList<byte[]> RequestBodies => bodies;
    public IReadOnlyList<ProviderEvent> Events => events;

    // Synchronous on purpose: TestRuntime and StorageScratchFolder bind ambients for the calling async flow, and an async factory would
    // hand the caller back its own flow without them.
    public static NativeSessionRig Create(string[] transcriptFiles, RuntimeMode runtimeMode = RuntimeMode.FullAccess,
        InteractionMode interactionMode = InteractionMode.Build, Action<OpenAICompatibleSession>? configure = null,
        Func<OpenAICompatibleSession.Config, SessionConfiguration, OpenAICompatibleSession>? factory = null, string? workingDirectory = null) =>
        new(transcriptFiles, runtimeMode, interactionMode, configure, factory, workingDirectory);

    public async Task<NativeSessionRig> Begin()
    {
        await Session.Start();
        return this;
    }

    public Task StartTurn(string text, RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build) =>
        Session.Send(new TurnInput
        {
            Text = text,
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
            IsFinalReport = false,
        });

    public void RunUntil(Func<bool> condition) => runtime.Loop.RunUntil(condition);

    public async Task Turn(string text, RuntimeMode runtimeMode = RuntimeMode.FullAccess, InteractionMode interactionMode = InteractionMode.Build)
    {
        var task = StartTurn(text, runtimeMode, interactionMode);
        RunUntil(() => task.IsCompleted);
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
        InteractionMode interactionMode = InteractionMode.Build, string? workingDirectory = null) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create(workingDirectory is null ? "file:///C:/work" : "file:" + workingDirectory.Replace('\\', '/'))!,
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
