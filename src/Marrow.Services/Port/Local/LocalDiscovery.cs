using System.Net.Http.Json;
using System.Text.Json;
using Marrow.Core.Port.Local;
using Marrow.Core.Port;

namespace Marrow.Services.Port.Local;

// PORT-ONLY: OD-0114 / ED-410. Only four loopback candidates are automatic. Other hosts are explicitly added.
// Metadata calls never load/pull a model or generate tokens. One short total deadline per server bounds all its calls.
public sealed class LocalDiscovery : IDisposable
{
    // Tests install an empty candidate factory at module load, before an unconfigured registry can probe a real server.
    // Explicit test instances still use fake loopback addresses. Production keeps the four loopback candidates.
    public static Ambient<Func<LocalDiscovery>> Factory { get; } = new(() => new LocalDiscovery());
    public static IReadOnlyList<Uri> DefaultAddresses { get; } = Array.AsReadOnly(new Uri[]
    {
        new("http://127.0.0.1:11434/"), new("http://127.0.0.1:1234/"),
        new("http://127.0.0.1:8080/"), new("http://127.0.0.1:8000/"),
    });

    private readonly HttpClient client;
    private readonly List<LocalServer> addresses = [];
    private readonly TimeSpan timeout;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim discoveryGate = new(1);
    public LocalDiscovery(IEnumerable<Uri>? loopbackAddresses = null, TimeSpan? probeTimeout = null, TimeProvider? timeProvider = null, HttpMessageHandler? httpHandler = null)
    {
        timeout = probeTimeout ?? TimeSpan.FromSeconds(1.5);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(probeTimeout));
        clock = timeProvider ?? TimeProvider.System;
        client = new HttpClient(httpHandler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var address in loopbackAddresses ?? DefaultAddresses) AddAddress(address.AbsoluteUri);
    }

    public IReadOnlyList<LocalServer> Addresses => addresses.ToArray();
    public IReadOnlyList<LocalEntry> Entries { get; private set; } = [];
    public long Revision { get; private set; }
    public LocalServer AddAddress(string address)
    {
        var server = new LocalServer(address);
        var existing = addresses.FirstOrDefault(item => item.Id == server.Id);
        if (existing is not null) return existing;
        addresses.Add(server);
        return server;
    }

    public LocalTarget? Resolve(string? selection)
    {
        foreach (var entry in Entries)
            if (entry.Models.FirstOrDefault(model => model.SelectionId == selection) is { } model)
                return new LocalTarget(entry.Server, model, entry.Models, entry.Kind);
        return null;
    }

    public async Task<IReadOnlyList<LocalEntry>> Discover(CancellationToken cancellationToken = default)
    {
        await discoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidates = Addresses;
            var found = new LocalEntry?[candidates.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, candidates.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
                async (index, token) => found[index] = await Probe(candidates[index], token).ConfigureAwait(false)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Entries = Array.AsReadOnly(found.OfType<LocalEntry>().ToArray());
            Revision++;
            return Entries;
        }
        finally { discoveryGate.Release(); }
    }

    private async Task<LocalEntry?> Probe(LocalServer server, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var token = linked.Token;
        var models = new List<LocalModel>();
        var kind = LocalServerKind.Unknown;
        try
        {
            var list = await Read(new Uri(server.BaseURL, "models"), token).ConfigureAwait(false);
            foreach (var row in Rows(list, "data"))
                if (Text(row, "id") is { Length: > 0 } id && models.All(model => model.Id != id))
                    models.Add(new LocalModel(server, id, Text(row, "name") ?? id, Metadata(row)));

            // /v1/models on Ollama is intentionally sparse. /api/tags identifies the native metadata adapter even when
            // the compatibility listing succeeds, including Ollama on a custom port or a user-chosen LAN host.
            var tags = await Read(server.NativeURL("api/tags"), token).ConfigureAwait(false);
            var ollamaRows = Rows(tags, "models").ToArray();
            foreach (var row in ollamaRows)
                if ((Text(row, "name") ?? Text(row, "model")) is { Length: > 0 } id && models.All(model => model.Id != id))
                    models.Add(new LocalModel(server, id, id, new LocalCapabilities()));
            if (models.Count == 0) return null;

            if (ollamaRows.Length > 0)
            {
                for (var index = 0; index < models.Count; index++)
                {
                    var model = models[index];
                    var show = await Read(server.NativeURL("api/show"), token, model.Id).ConfigureAwait(false);
                    if (show is { } info) models[index] = model with { Capabilities = OllamaMetadata(info, model.Capabilities) };
                }
            }
            else
            {
                var studio = await Read(server.NativeURL("api/v0/models"), token).ConfigureAwait(false);
                if (studio is { } studioReply) kind = LocalServerIdentity.FromStudioModelsReply(studioReply);
                var rows = Rows(studio, "data").ToArray();
                for (var index = 0; index < models.Count; index++)
                    if (rows.FirstOrDefault(row => Text(row, "id") == models[index].Id) is { ValueKind: JsonValueKind.Object } row)
                        models[index] = models[index] with { Capabilities = Metadata(row, models[index].Capabilities, native: true) };
                // llama.cpp reports its configured per-slot context in default_generation_settings, not model maximum.
                // PORT-ONLY (W17, ED-NEW-web-w17-1): a server not yet identified is asked for /props too, which names llama.cpp.
                if (kind == LocalServerKind.Unknown || models.Any(model => model.Capabilities.ToolCalling is null || !model.Capabilities.ContextReported))
                {
                    var props = await Read(server.NativeURL("props"), token).ConfigureAwait(false);
                    if (props is { } identity && kind == LocalServerKind.Unknown) kind = LocalServerIdentity.FromPropsReply(identity);
                    if (props is { } properties && models.Count == 1)
                    {
                        var caps = Metadata(properties, models[0].Capabilities);
                        if (Property(properties, "default_generation_settings") is { } generation && Positive(generation, "n_ctx") is { } context)
                            caps = caps with { ContextWindow = context, ContextReported = true };
                        models[0] = models[0] with { Capabilities = caps };
                    }
                }
            }
            if (kind == LocalServerKind.Unknown) kind = await ReadRootIdentity(server, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        // A server that listed models before its optional metadata timed out still has honest fallback capabilities.
        cancellationToken.ThrowIfCancellationRequested();
        return models.Count == 0 ? null : new LocalEntry(server, models.AsReadOnly(), kind);
    }

    private async Task<LocalServerKind> ReadRootIdentity(LocalServer server, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, server.NativeURL(""));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return LocalServerKind.Unknown;
            return LocalServerIdentity.FromRootReply(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        }
        catch (Exception error) when (error is HttpRequestException or IOException) { return LocalServerKind.Unknown; }
    }

    private async Task<JsonElement?> Read(Uri url, CancellationToken token, string? model = null)
    {
        try
        {
            using var request = new HttpRequestMessage(model is null ? HttpMethod.Get : HttpMethod.Post, url);
            if (model is not null) request.Content = JsonContent.Create(new { model });
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            return json.RootElement.Clone();
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException) { return null; }
    }

    private static JsonElement? Property(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) ? property : null;
    private static string? Text(JsonElement value, string key) => Property(value, key) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    private static int? Positive(JsonElement value, string key) =>
        Property(value, key) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var result) && result > 0 ? result : null;
    private static bool? Bool(JsonElement value, string key) => Property(value, key) is { } flag
        ? flag.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;
    private static IEnumerable<JsonElement> Rows(JsonElement? value, string key) => value is { } root
        && Property(root, key) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    private static LocalCapabilities Metadata(JsonElement row, LocalCapabilities? fallback = null, bool native = false)
    {
        var caps = fallback ?? new LocalCapabilities();
        var tools = Bool(row, "supports_tool_calling") ?? Bool(row, "supports_tools");
        if (Property(row, "capabilities") is { } capabilities)
        {
            if (capabilities.ValueKind == JsonValueKind.Array) tools ??= capabilities.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == "tools");
            else tools ??= Bool(capabilities, "function_calling") ?? Bool(capabilities, "tools");
        }
        if (Property(row, "supported_parameters") is { ValueKind: JsonValueKind.Array } parameters)
            tools ??= parameters.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == "tools");
        var context = (native ? Positive(row, "loaded_context_length") : null)
            ?? Positive(row, "context_length") ?? Positive(row, "max_model_len");
        return caps with { ToolCalling = tools ?? caps.ToolCalling, ContextWindow = context ?? caps.ContextWindow,
            ContextReported = context is not null || caps.ContextReported, Vision = Bool(row, "supports_vision") ?? caps.Vision };
    }

    private static LocalCapabilities OllamaMetadata(JsonElement show, LocalCapabilities fallback)
    {
        var caps = Metadata(show, fallback);
        // model_info.*.context_length is a theoretical maximum. Only num_ctx is a reported configured window.
        foreach (var line in (Text(show, "parameters") ?? "").Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] == "num_ctx" && int.TryParse(parts[1], out var context) && context > 0)
                caps = caps with { ContextWindow = context, ContextReported = true };
        }
        return caps;
    }

    public void Dispose() { client.Dispose(); discoveryGate.Dispose(); }
}
