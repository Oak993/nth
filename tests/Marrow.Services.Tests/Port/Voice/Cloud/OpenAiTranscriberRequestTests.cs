using System.Buffers.Binary;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, voice-SPEC V-18.1 and V-T110: the request is a multipart POST of the session audio as a 16 kHz mono PCM16 WAV, the model, the
// language only when forced, response_format json, stream true where the model supports it. The handler is the seam (V-18.5): an
// in-process one that answers for api.openai.com and never opens a socket.
[Trait("Spec", "V-T110")]
public sealed class OpenAiTranscriberRequestTests
{
    private static async Task<(InProcessRequest Request, IReadOnlyList<MultipartPart> Parts)> Send(string language = "auto", string? model = null, int seconds = 1)
    {
        var (keys, _) = CloudFixtures.KeyStore();
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, "{\"text\":\"hello world\"}"));
        using var engine = new OpenAiTranscriber(keys, handler);
        if (model is not null) await engine.LoadAsync(new SpeechModel(model, null), new EngineLoadOptions(), CancellationToken.None);
        await engine.TranscribeAsync(CloudFixtures.Audio(seconds), CloudFixtures.Request(language), CancellationToken.None);
        var request = Assert.Single(handler.Received);
        return (request, CloudFixtures.Multipart(request));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T110_it_posts_to_the_transcriptions_endpoint_with_the_key_as_a_bearer_token()
    {
        var (request, _) = await Send();
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", request.Url.AbsoluteUri);
        Assert.Equal("Bearer " + CloudFixtures.Key, request.Header("Authorization"));
        Assert.StartsWith("multipart/form-data; boundary=", request.Header("Content-Type"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T110_the_fields_are_the_file_the_model_json_and_stream_and_no_language_when_auto()
    {
        var (_, parts) = await Send();
        Assert.Equal(["file", "model", "response_format", "stream"], parts.Select(p => p.Name));
        Assert.Equal("gpt-transcribe", parts.Single(p => p.Name == "model").Text);
        Assert.Equal("json", parts.Single(p => p.Name == "response_format").Text);
        Assert.Equal("true", parts.Single(p => p.Name == "stream").Text);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T110_the_file_is_the_audio_as_a_16_kHz_mono_PCM16_WAV()
    {
        var audio = CloudFixtures.Audio(1);
        var (_, parts) = await Send();
        var file = parts.Single(p => p.Name == "file");
        Assert.Equal("audio.wav", file.FileName);
        Assert.Equal("audio/wav", file.ContentType);
        Assert.Equal(PcmWav.Encode(audio), file.Data);
        Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(file.Data.AsSpan(24)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(file.Data.AsSpan(22)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(file.Data.AsSpan(34)));
        Assert.Equal(44 + 2 * 16000, file.Data.Length);
    }

    [Theory]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("de")]
    [InlineData("hi")]
    public async Task V_T110_a_forced_language_is_sent_as_its_code(string code)
    {
        var (_, parts) = await Send(code);
        Assert.Equal(code, parts.Single(p => p.Name == "language").Text);
        Assert.Equal(["file", "model", "language", "response_format", "stream"], parts.Select(p => p.Name));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T110_the_model_loaded_is_the_model_asked_for_and_a_model_without_streaming_gets_no_stream_field()
    {
        var (_, parts) = await Send(model: "whisper-1");
        Assert.Equal("whisper-1", parts.Single(p => p.Name == "model").Text);
        Assert.DoesNotContain(parts, p => p.Name == "stream");
        Assert.True(OpenAiTranscriber.SupportsStreaming("gpt-transcribe"));
        Assert.False(OpenAiTranscriber.SupportsStreaming("whisper-1"));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T110_no_prompt_and_no_other_field_is_sent()
    {
        var (_, parts) = await Send("de");
        var request = new DecodeRequest("de", "committed text so far", DecodeMode.Final, TimeSpan.Zero);
        var (keys, _) = CloudFixtures.KeyStore();
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, "{\"text\":\"x\"}"));
        using var engine = new OpenAiTranscriber(keys, handler);
        await engine.TranscribeAsync(CloudFixtures.Audio(1), request, CancellationToken.None);
        Assert.DoesNotContain(CloudFixtures.Multipart(handler.Received[0]), p => p.Name == "prompt");
        Assert.Equal(parts.Select(p => p.Name), CloudFixtures.Multipart(handler.Received[0]).Select(p => p.Name));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T110_a_file_over_25_MB_is_not_sent()
    {
        var (keys, _) = CloudFixtures.KeyStore();
        var handler = CloudFixtures.Answering(_ => InProcessHttpHandler.Reply(200, "{\"text\":\"x\"}"));
        using var engine = new OpenAiTranscriber(keys, handler);
        var tooLong = new float[(int)(OpenAiTranscriber.MaxFileBytes / 2) + 1];          // 44 header bytes more than 25 MB
        var error = await Assert.ThrowsAsync<OpenAiTranscriptionException>(() => engine.TranscribeAsync(tooLong, CloudFixtures.Request(), CancellationToken.None));
        Assert.Equal(CloudNote.EngineFailure, error.Note);
        Assert.Empty(handler.Received);
    }
}
