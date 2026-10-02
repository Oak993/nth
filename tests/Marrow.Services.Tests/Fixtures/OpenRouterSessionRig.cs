using System.Net;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test rig (ED-234, R-TEST-9, R-TEST-10): one native OpenRouter session over a throwaway working folder and a
// deterministic main loop, with https://openrouter.ai answered in this process through the HTTP client's test seam
// (URLSession.Transport, OD-0057). The models endpoint answers the made-up list; each chat request takes the next
// scripted reply, so the rig can show what the session sent and what it did with what came back. No socket is opened.
public sealed class OpenRouterSessionRig : IDisposable
{
    private readonly IDisposable transport;
    private readonly Queue<Func<InProcessRequest, HttpResponseMessage>> replies = new();

    public OpenRouterSessionRig(string? apiKey = OpenRouterBodies.Key)
    {
        Runtime = new TestRuntime();
        Scratch = new StorageScratchFolder();
        Root = Path.Combine(Path.GetDirectoryName(Scratch.Root)!, "work");
        Directory.CreateDirectory(Root);
        Handler = new InProcessHttpHandler { Answer = Route };
        transport = URLSession.Transport.Override(Handler);
        Session = new OpenRouterSession(OpenRouterBodies.Configuration(Root, apiKey)) { OnEvent = Events.Add };
        Session.SeedMcpToolsForTests([]); // no MCP server is started: MCPHub.Shared stays out of the test
        OpenRouterAPI.ForgetModelsForTests();
    }

    public TestRuntime Runtime { get; }
    public StorageScratchFolder Scratch { get; }
    public string Root { get; }
    public InProcessHttpHandler Handler { get; }
    public OpenRouterSession Session { get; }
    public List<ProviderEvent> Events { get; } = [];

    /// <summary>Queues the answer to the next chat request: a streamed body with the given data lines.</summary>
    public void ReplyWithStream(params string[] dataLines) =>
        replies.Enqueue(_ => InProcessHttpHandler.Reply(200, OpenRouterBodies.Sse(dataLines), "text/event-stream"));

    /// <summary>Queues a refusal for the next chat request.</summary>
    public void ReplyWithStatus(int status, string body) => replies.Enqueue(_ => InProcessHttpHandler.Reply(status, body));

    /// <summary>The chat requests received, in order (the models and key requests are not among them).</summary>
    public IReadOnlyList<InProcessRequest> ChatRequests =>
        [.. Handler.Received.Where(request => request.Url.AbsolutePath == "/api/v1/chat/completions")];

    /// <summary>The JSON body of the n-th chat request.</summary>
    public JSONValue ChatBody(int index) => JSONValue.Parse(ChatRequests[index].BodyText)!;

    /// <summary>Starts the session and runs one turn to its end.</summary>
    public async Task SendAsync(TurnInput input)
    {
        await Session.Start();
        var task = Session.Send(input);
        Runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    /// <summary>Fetches the made-up model list, so the session knows each model's window and image ability.</summary>
    public Task<ValueList<ModelOption>> LoadCatalogue() => OpenRouterAPI.ListModels(OpenRouterBodies.Key);

    private HttpResponseMessage Route(InProcessRequest request)
    {
        if (request.Url.AbsolutePath == "/api/v1/models") return InProcessHttpHandler.Reply(200, OpenRouterBodies.ModelsJson);
        if (request.Url.AbsolutePath == "/api/v1/chat/completions" && replies.Count > 0) return replies.Dequeue()(request);
        return InProcessHttpHandler.Reply((int)HttpStatusCode.NotFound, "{}");
    }

    public void Dispose()
    {
        OpenRouterAPI.ForgetModelsForTests();
        transport.Dispose();
        Handler.Dispose();
        Scratch.Dispose();
        Runtime.Dispose();
    }
}
