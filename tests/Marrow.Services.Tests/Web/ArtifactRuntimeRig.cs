using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

/// <summary>
/// W13 test rig: the web rig over the fake engine, an artifact store in a throwaway threads folder (R-TEST-9), the
/// runtime over the vendored libraries copied beside the tests, and fakes for the pane, the model and the tools. Replies
/// to pages are read from the fake view's log.
/// </summary>
internal sealed class ArtifactRuntimeRig : IDisposable
{
    public static readonly Guid Chat = Guid.Parse("5d2c7a10-3b4e-4f6a-8c9d-0e1f2a3b4c5d");
    public static readonly Guid OtherChat = Guid.Parse("5d2c7a10-3b4e-4f6a-8c9d-0e1f2a3b4c5e");
    public static readonly ArtifactAuthor Model = new("claude", "sonnet", null);

    private DateTimeOffset now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public ArtifactRuntimeRig()
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Threads = Path.Combine(data, "ArtifactRuntime", Guid.NewGuid().ToString("N"), "threads");
        Directory.CreateDirectory(Threads);
        Store = new ArtifactStore(() => Threads, () => now = now.AddSeconds(1));
        Runtime = new ArtifactRuntime(Store, Libraries, Host, ModelClient, Tools, Web.Time);
    }

    public static ArtifactLibraryFiles Libraries { get; } = new(ArtifactLibraryFiles.DefaultFolder);

    public WebRig Web { get; } = new();

    public string Threads { get; }

    public ArtifactStore Store { get; }

    public ArtifactRuntime Runtime { get; }

    public FakeArtifactBridgeHost Host { get; } = new();

    public FakeArtifactModelClient ModelClient { get; } = new();

    public FakeArtifactToolClient Tools { get; } = new();

    public ArtifactSnapshot Create(ArtifactKind kind, string content, string title = "Weather", string? id = null, Guid? chat = null, string? language = null) =>
        Store.Create(chat ?? Chat, new ArtifactDraft { Kind = kind, Title = title, Content = content, Id = id, Language = language }, Model, null);

    public ArtifactSnapshot CreateProject(string entry, params (string Path, string Content)[] files) =>
        Store.Create(Chat, new ArtifactDraft { Kind = ArtifactKind.Project, Title = "Orbit site", Entry = entry, Files = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal) }, Model, null);

    /// <summary>A live artifact view, mounted on the artifact.</summary>
    public (ArtifactMount Mount, FakeWebView View) Mount(ArtifactSnapshot snapshot)
    {
        var view = Web.OpenLive(WebSurfaceKind.Artifact, url: null);
        var mount = Runtime.Mount(view, snapshot.Artifact.Thread, snapshot.Artifact.Id);
        WebRig.Fake(view).RaiseNavigationCompleted(mount.Address);
        return (mount, WebRig.Fake(view));
    }

    public static WebResourceResponse Get(FakeWebView view, string url, string method = "GET") =>
        view.Request(url, WebResourceKind.Document, method) ?? throw new InvalidOperationException(url + " was let through to the network.");

    public static string Body(WebResourceResponse response) => System.Text.Encoding.UTF8.GetString(response.Body);

    /// <summary>Sends a bridge message from the page (its own origin unless given).</summary>
    public static void Send(ArtifactMount mount, FakeWebView view, object message, string? origin = null) =>
        view.RaiseMessage(origin ?? mount.Origin, JsonSerializer.Serialize(message));

    /// <summary>Every message nth posted to the page, oldest first.</summary>
    public static List<JsonObject> Posts(FakeWebView view) =>
        view.Log.Where(l => l.StartsWith("Post ", StringComparison.Ordinal)).Select(l => JsonNode.Parse(l[5..])!.AsObject()).ToList();

    /// <summary>The final reply to a request id (not its deltas).</summary>
    public static JsonObject Reply(FakeWebView view, long id) =>
        Posts(view).Last(p => p["id"]?.GetValue<long>() == id && p["delta"] is null);

    public void Advance(TimeSpan time) => Web.Time.Advance(time);

    public void Dispose() => Web.Dispose();
}
