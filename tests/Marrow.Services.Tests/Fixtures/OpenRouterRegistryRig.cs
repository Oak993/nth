using System.Net;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test rig (ED-234, R-TEST-9, R-TEST-10): one ProviderRegistry over in-memory settings, a deterministic main
// loop and clock, and https://openrouter.ai answered in this process through the HTTP client's test seam
// (URLSession.Transport, OD-0057). The models and key endpoints answer with the statuses and bodies a test sets; no socket
// is opened and no key outside the test exists. A second registry over the same settings (Reopen) is the next launch.
public sealed class OpenRouterRegistryRig : IDisposable
{
    private readonly IDisposable transport;

    public OpenRouterRegistryRig()
    {
        Runtime = new TestRuntime();
        Settings = new FakeProviderSettings();
        Handler = new InProcessHttpHandler { Answer = Answer };
        transport = URLSession.Transport.Override(Handler);
        OpenRouterAPI.ForgetModelsForTests();
        Registry = new ProviderRegistry(Settings);
    }

    public TestRuntime Runtime { get; }
    public FakeProviderSettings Settings { get; }
    public InProcessHttpHandler Handler { get; }
    public ProviderRegistry Registry { get; private set; }

    public int ModelsStatus { get; set; } = 200;
    public string ModelsBody { get; set; } = OpenRouterBodies.ModelsJson;
    public int KeyStatus { get; set; } = 200;

    /// <summary>The requests that reached the stand-in, by path.</summary>
    public int Count(string path) => Handler.Received.Count(request => request.Url.AbsolutePath == path);

    /// <summary>The next launch: a new registry that starts from what the first one persisted.</summary>
    public ProviderRegistry Reopen() => Registry = new ProviderRegistry(Settings);

    /// <summary>Runs one registry call on the main loop and pumps until it completes (background thread, so the I/O can finish).</summary>
    public Task Run(Func<Task> body) => Task.Run(() => Runtime.Loop.Run(body, TimeSpan.FromSeconds(30)));

    /// <summary>The persisted catalogue blob decoded to provider raw value -> model ids.</summary>
    public Dictionary<string, string[]> StoredCatalogueIds()
    {
        var stored = Assert.IsType<byte[]>(Runtime.Defaults.Object("providerModelCatalogs"));
        using var document = JsonDocument.Parse(stored);
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(row => row.GetProperty("id").GetString()!).ToArray());
    }

    /// <summary>The persisted delivery dates decoded to provider raw value -> seconds since 1970.</summary>
    public Dictionary<string, double> StoredDates()
    {
        if (Runtime.Defaults.Object("providerModelCatalogDates") is not byte[] stored) return [];
        using var document = JsonDocument.Parse(stored);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetDouble());
    }

    private HttpResponseMessage Answer(InProcessRequest request) => request.Url.AbsolutePath switch
    {
        "/api/v1/models" => InProcessHttpHandler.Reply(ModelsStatus, ModelsBody),
        "/api/v1/key" => InProcessHttpHandler.Reply(KeyStatus, "{}"),
        _ => InProcessHttpHandler.Reply((int)HttpStatusCode.NotFound, "{}"),
    };

    public void Dispose()
    {
        OpenRouterAPI.ForgetModelsForTests();
        transport.Dispose();
        Handler.Dispose();
        Runtime.Dispose();
    }
}
