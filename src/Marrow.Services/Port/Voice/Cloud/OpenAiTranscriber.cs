// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): voice-SPEC V-18.1 to V-18.5. The cloud recogniser: one
// POST per transcription with the session audio. No OpenAI SDK; HttpClient with a 60 s timeout over a handler the caller supplies (the
// seam: a loopback or in-process handler in tests, and no default, so nothing here can reach the network on its own).
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Services.Port.Voice.Cloud;

/// <summary><see cref="ISpeechEngine"/> over OpenAI's audio transcription endpoint.</summary>
public sealed class OpenAiTranscriber : IProcessingSpeechEngine, IDisposable
{
    /// <summary>V-18.1: the endpoint.</summary>
    public static readonly Uri Endpoint = new("https://api.openai.com/v1/audio/transcriptions");

    /// <summary>V-18.1 (Q-V-15): the recommended cloud model.</summary>
    public const string DefaultModel = "gpt-transcribe";

    /// <summary>V-18.5: the request's timeout.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>V-18.3: the largest file OpenAI takes, 25 MB.</summary>
    public const long MaxFileBytes = 25L * 1024 * 1024;

    private readonly VoiceKeyStore keys;
    private readonly Uri endpoint;
    private readonly HttpClient client;
    private readonly TimeSpan timeout;
    private string model = DefaultModel;

    public OpenAiTranscriber(VoiceKeyStore keys, HttpMessageHandler handler, TimeSpan? timeout = null, Uri? endpoint = null)
    {
        this.keys = keys;
        this.endpoint = endpoint ?? Endpoint;
        this.timeout = timeout ?? DefaultTimeout;
        client = new HttpClient(handler, disposeHandler: false) { Timeout = this.timeout };
    }

    /// <summary>
    /// Word operations as the streamed text arrives (V-18.1): one batch per delta, and a last one at the end that makes every word solid.
    /// Raised on the thread that reads the reply, during the call.
    /// </summary>
    public event Action<IReadOnlyList<WordOp>>? OpsProduced;

    /// <summary>The model the next call asks for.</summary>
    public string Model => model;

    /// <summary>
    /// The cloud engine has no file to load (<see cref="SpeechModel.Path"/> is null): it takes the model id and checks there is a key
    /// (V-17.6 "cloud without a key"), so a session without one stops before it records.
    /// </summary>
    public Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReady();
        if (!string.IsNullOrWhiteSpace(model.Id)) this.model = model.Id;
        return Task.CompletedTask;
    }

    public void ValidateReady()
    {
        if (keys.Read() is null) throw new OpenAiTranscriptionException(CloudNote.KeyMissing);
    }

    /// <summary>V-18.1: stream true where the model supports it. ED-311: the gpt- models do; whisper-1 does not.</summary>
    public static bool SupportsStreaming(string model) => model.StartsWith("gpt-", StringComparison.Ordinal);

    /// <summary>
    /// ED-NEW-voicefix-4: the endpoint has no language detection of its own to ask; the cloud engine is processing-only and its one request transcribes (never translates).
    /// </summary>
    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken) =>
        Task.FromResult<LanguageOdds?>(null);

    public async Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken)
    {
        var key = keys.Read() ?? throw new OpenAiTranscriptionException(CloudNote.KeyMissing);
        var wav = PcmWav.Encode(audio16k.Span);
        if (wav.Length > MaxFileBytes) throw new OpenAiTranscriptionException(CloudNote.EngineFailure);
        var stream = SupportsStreaming(model);
        using var body = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        body.Add(file, "file", "audio.wav");
        body.Add(new StringContent(model), "model");
        if (!string.IsNullOrEmpty(request.Language) && !string.Equals(request.Language, "auto", StringComparison.Ordinal))
            body.Add(new StringContent(request.Language), "language");            // V-18.1 "language when forced"; V-7.13: "auto" is not a code
        body.Add(new StringContent("json"), "response_format");
        if (stream) body.Add(new StringContent("true"), "stream");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = body };
        message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        var text = await Send(message, cancellationToken);
        return Spread(text, audio16k.Length / (double)PcmWav.SampleRate);
    }

    private async Task<string> Send(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        // HttpClient.Timeout ends at the reply's headers (the body is read as it streams), so the same time also runs here over the body: a reply that has gone quiet for that long
        // (no byte of a JSON body, no event of a stream) ends as the timed-out network failure instead of holding the session in Processing. Windows review.
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        quiet.CancelAfter(timeout);
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, quiet.Token);
            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode);
            var streamed = response.Content.Headers.ContentType?.MediaType == "text/event-stream";
            await using var content = await response.Content.ReadAsStreamAsync(quiet.Token);
            return streamed ? await ReadEvents(content, quiet) : await ReadJson(content, quiet.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException e)
        {
            throw new OpenAiTranscriptionException(CloudNote.NetworkFailure, timedOut: true, inner: e);   // HttpClient's own timeout
        }
        catch (HttpRequestException e)
        {
            throw new OpenAiTranscriptionException(CloudNote.NetworkFailure, inner: e);
        }
        catch (IOException e)
        {
            throw new OpenAiTranscriptionException(CloudNote.NetworkFailure, inner: e);
        }
    }

    /// <summary>V-18.4: 401 is a refused key, 429 quota or rate, anything else (5xx included) an engine failure; ED-311.</summary>
    private static OpenAiTranscriptionException Failure(HttpStatusCode status) => new(
        status switch { HttpStatusCode.Unauthorized => CloudNote.KeyRefused, HttpStatusCode.TooManyRequests => CloudNote.QuotaOrRate, _ => CloudNote.EngineFailure },
        (int)status);

    private async Task<string> ReadEvents(Stream content, CancellationTokenSource quiet)
    {
        var ops = new OpenAiDeltaOps();
        string? whole = null;
        await foreach (var e in OpenAiSseReader.Read(content, quiet.Token))
        {
            quiet.CancelAfter(timeout);
            if (e.Done) { whole = e.Text; OpsProduced?.Invoke(ops.Done(whole)); break; }
            if (ops.Delta(e.Text) is { Count: > 0 } batch) OpsProduced?.Invoke(batch);
        }
        if (whole is null)
        {
            // the stream ended without its done event: what the deltas said is the text, and its last word is finished
            whole = ops.Text;
            if (ops.Done(whole) is { Count: > 0 } last) OpsProduced?.Invoke(last);
        }
        return whole;
    }

    private async Task<string> ReadJson(Stream content, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                throw new OpenAiTranscriptionException(CloudNote.EngineFailure);
            var whole = text.GetString()!;
            var ops = new OpenAiDeltaOps().Done(whole);
            if (ops.Count > 0) OpsProduced?.Invoke(ops);
            return whole;
        }
        catch (JsonException e)
        {
            throw new OpenAiTranscriptionException(CloudNote.EngineFailure, inner: e);
        }
    }

    /// <summary>
    /// A reply carries no word times, and the transcriber's final pass places words by them (V-7.8: a word wholly in silence is dropped).
    /// ED-311: the words are spread evenly over the audio's length.
    /// </summary>
    private static Hypothesis Spread(string text, double seconds)
    {
        var words = OpenAiDeltaOps.Split(text, finished: true);
        if (words.Count == 0) return Hypothesis.Empty;
        var step = seconds / words.Count;
        return new Hypothesis(words.Select((w, i) => new HypothesisWord(w.Text, i * step, (i + 1) * step)).ToList());
    }

    public void Dispose() => client.Dispose();
}
