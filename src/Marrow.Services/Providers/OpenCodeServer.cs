// PORT-ONLY: OD-0116, OD-0122, ED-439. The real `opencode serve`: a local-only HTTP server on a free port, used only to store a message without a reply.
// UNVERIFIED against a real OpenCode (the research could not confirm the v2 API): every failure here makes the standby fall back to a warm start.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Port;

namespace Marrow.Services.Providers;

internal sealed partial class OpenCodeServer : IOpenCodeServer
{
    private readonly ProcessLauncher process;
    private readonly HttpClient http;
    private bool stopped;

    private OpenCodeServer(ProcessLauncher process, HttpClient http)
    {
        this.process = process;
        this.http = http;
    }

    [GeneratedRegex(@"https?://(?:127\.0\.0\.1|localhost|\[::1\]):\d+", RegexOptions.CultureInvariant)]
    private static partial Regex ListeningAddress();

    public static async Task<IOpenCodeServer> StartAsync(SessionConfiguration configuration, CancellationToken ct)
    {
        if (configuration.Executable is not { } executable) throw new ProviderError(new ProviderError.Case.NotInstalled(configuration.Provider));
        var found = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var text = new StringBuilder();
        var gate = new object();
        void Read(byte[] data)
        {
            if (data.Length == 0) return;
            lock (gate)
            {
                text.Append(Encoding.UTF8.GetString(data));
                if (ListeningAddress().Match(text.ToString()) is { Success: true } match) found.TrySetResult(new Uri(match.Value));
            }
        }

        var output = new ProcessPipe { ReadabilityHandler = Read };
        var error = new ProcessPipe { ReadabilityHandler = Read };
        var launcher = new ProcessLauncher
        {
            ExecutableURL = executable.Path,
            Arguments = ["serve", "--hostname", "127.0.0.1", "--port", "0"],
            CurrentDirectoryURL = configuration.WorkingDirectory.Path,
            Environment = new Dictionary<string, string>(configuration.Environment, StringComparer.OrdinalIgnoreCase),
            StandardOutput = output,
            StandardError = error,
        };
        launcher.Run();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var address = await found.Task.WaitAsync(timeout.Token);
            return new OpenCodeServer(launcher, new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) });
        }
        catch
        {
            launcher.Terminate();
            throw;
        }
    }

    public async Task<string> OpenApiAsync(CancellationToken ct) => await http.GetStringAsync(new Uri("/doc", UriKind.Relative), ct);

    public async Task<string> CreateSessionAsync(CancellationToken ct)
    {
        using var response = await http.PostAsync(new Uri("/session", UriKind.Relative), Json("{}"), ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("OpenCode did not create a session.");
    }

    public async Task InjectAsync(string sessionId, string text, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { noReply = true, parts = new[] { new { type = "text", text } } });
        using var response = await http.PostAsync(new Uri("/session/" + Uri.EscapeDataString(sessionId) + "/message", UriKind.Relative), Json(body), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(await http.GetStringAsync(new Uri("/session/" + Uri.EscapeDataString(sessionId) + "/message", UriKind.Relative), ct));
        var texts = new List<string>();
        if (document.RootElement.ValueKind != JsonValueKind.Array) return texts;
        foreach (var message in document.RootElement.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in parts.EnumerateArray())
                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text) && text.GetString() is { } value) texts.Add(value);
        }

        return texts;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        using var response = await http.DeleteAsync(new Uri("/session/" + Uri.EscapeDataString(sessionId), UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();
    }

    public void Stop()
    {
        if (stopped) return;
        stopped = true;
        http.Dispose();
        process.Terminate();
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
