using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// ED-NEW-lean-4 (lean pass, unit L4): search_text's project walk runs off the main actor, like NativeFileTools'
// read, list, write and edit (R-ISO-2), so a big project search never holds the UI. The walk is held mid-file
// through the session's SearchFileProbeForTests seam while the test proves the main loop still runs work.
// Before the fix the walk ran inline on the main actor (the probe saw MainActor.IsIsolated, so the first
// assertion failed); after it, the walk runs on a pool thread.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-lean-4")]
public sealed class SearchTextOffMainTests
{
    private static OpenAICompatibleSession.Config MakeConfig() => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = new Uri("https://api.deepseek.com/chat/completions"),
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

    // The scheme:path-rootless form, as OpenAICompatibleSessionFourthTests explains.
    private static SwiftURL WorkingDirectoryURL(string root) => SwiftURL.Create("file:" + root.Replace('\\', '/'))!;

    private static OpenAICompatibleSession Session(string root)
    {
        var session = new OpenAICompatibleSession(MakeConfig(), new SessionConfiguration
        {
            Provider = ProviderKind.Deepseek,
            WorkingDirectory = WorkingDirectoryURL(root),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
            ApiKey = "sk-test",
        });
        session.OnEvent = _ => { };
        return session;
    }

    [Fact]
    public async Task The_walk_runs_off_the_main_actor_and_main_stays_free_while_a_file_read_is_held()
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        File.WriteAllText(folder.Child("a.txt"), "needle one\nnot this\nneedle two");
        var session = Session(folder.Root);
        var mainThread = Environment.CurrentManagedThreadId;
        int? walkThread = null;
        bool? walkOnMain = null;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.SearchFileProbeForTests = _ =>
        {
            walkThread = Environment.CurrentManagedThreadId;
            walkOnMain = MainActor.IsIsolated;
            entered.Set();
            // An inline walk is the main loop itself: holding it would leave nothing to release it.
            if (walkOnMain == true) return;
            release.Wait(TimeSpan.FromSeconds(30));
        };

        Task<string>? task = null;
        runtime.Loop.Enqueue(() => task = session.ExecuteToolForTests(new("call-1", "search_text", "{\"pattern\":\"needle\"}")));
        runtime.RunUntilIdle();

        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)), "The walk never reached the file.");
        Assert.False(walkOnMain, "The walk ran on the main actor.");
        Assert.NotEqual(mainThread, walkThread);

        // The walk is held mid-file; the main actor still runs posted work and the search is still open.
        var mainRan = false;
        runtime.OnMain(() => mainRan = true);
        Assert.True(mainRan);
        Assert.NotNull(task);
        Assert.False(task.IsCompleted);

        release.Set();
        runtime.Loop.RunUntil(() => task!.IsCompleted);
        Assert.Equal("a.txt:1:needle one\na.txt:3:needle two", await task);
    }
}
