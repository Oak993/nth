using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>An offline `opencode serve`: an OpenAPI document, one session and the parts stored in it.</summary>
public sealed class FakeOpenCodeServer(string openApi) : IOpenCodeServer
{
    public const string WithNoReply = "{\"paths\":{\"/session/{id}/message\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"properties\":{\"noReply\":{\"type\":\"boolean\"},\"parts\":{\"type\":\"array\"}}}}}}}}}}";
    public const string ViaReference = "{\"paths\":{\"/session/{id}/message\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/PromptInput\"}}}}}}},\"components\":{\"schemas\":{\"PromptInput\":{\"properties\":{\"noReply\":{\"type\":\"boolean\"}}}}}}";
    public const string WithoutNoReply = "{\"paths\":{\"/session/{id}/message\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"properties\":{\"parts\":{\"type\":\"array\"}}}}}}}}}}";

    public bool StoresNothing { get; init; }
    public bool InjectFails { get; init; }
    public int Stops { get; private set; }
    public List<string> Created { get; } = [];
    public List<string> Injected { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<string> Calls { get; } = [];

    public Task<string> OpenApiAsync(CancellationToken ct)
    {
        Calls.Add("doc");
        return Task.FromResult(openApi);
    }

    public Task<string> CreateSessionAsync(CancellationToken ct)
    {
        Calls.Add("create");
        Created.Add("ses_standby");
        return Task.FromResult("ses_standby");
    }

    public Task InjectAsync(string sessionId, string text, CancellationToken ct)
    {
        Calls.Add("inject");
        if (InjectFails) throw new HttpRequestException("500");
        if (!StoresNothing) Injected.Add(text);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct)
    {
        Calls.Add("read");
        return Task.FromResult<IReadOnlyList<string>>(Injected);
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        Calls.Add("delete");
        Deleted.Add(sessionId);
        return Task.CompletedTask;
    }

    public void Stop() => Stops++;
}
