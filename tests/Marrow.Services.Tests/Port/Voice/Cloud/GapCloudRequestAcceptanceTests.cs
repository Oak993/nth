using System.Buffers.Binary;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// GAP-2, V-T110: exercise the multipart request over the acceptance list's loopback socket, including independent WAV header expectations.
public sealed class GapCloudRequestAcceptanceTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("de")]
    [InlineData("hi")]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T110")]
    public async Task GAP_The_loopback_request_has_only_the_documented_fields_and_a_mono_PCM16_WAV(string language)
    {
        await using var server = new LoopbackHttpServer();
        var (keys, _) = CloudFixtures.KeyStore();
        using var engine = new OpenAiTranscriber(keys, new HttpClientHandler(), endpoint: server.Url("/v1/audio/transcriptions"));
        float[] audio = [0, .5f, -.5f, 1, -1];
        var call = engine.TranscribeAsync(audio, CloudFixtures.Request(language), CancellationToken.None);
        var exchange = await server.NextExchange();
        await exchange.Respond(200, "OK", "{\"text\":\"hello\"}"u8.ToArray(), ("Content-Type", "application/json"));
        await call.WaitAsync(LoopbackHttpServer.Guard);
        exchange.Finish();

        Assert.Equal("POST", exchange.Method);
        Assert.Equal("/v1/audio/transcriptions", exchange.Target);
        Assert.Equal("Bearer " + CloudFixtures.Key, Assert.Single(exchange.HeaderValues("Authorization")));
        var parts = CloudFixtures.Multipart(new InProcessRequest(exchange.Method, server.Url(exchange.Target), exchange.Headers, exchange.Body));
        Assert.Equal(language == "auto" ? ["file", "model", "response_format", "stream"] : new[] { "file", "model", "language", "response_format", "stream" }, parts.Select(p => p.Name));
        Assert.Equal("gpt-transcribe", parts.Single(p => p.Name == "model").Text);
        Assert.Equal("json", parts.Single(p => p.Name == "response_format").Text);
        Assert.Equal("true", parts.Single(p => p.Name == "stream").Text);
        if (language != "auto") Assert.Equal(language, parts.Single(p => p.Name == "language").Text);
        var file = parts.Single(p => p.Name == "file");
        Assert.Equal(("audio.wav", "audio/wav"), (file.FileName, file.ContentType));
        var wav = file.Data;
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
        Assert.Equal("fmt "u8.ToArray(), wav[12..16]);
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(32000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));
        Assert.Equal("data"u8.ToArray(), wav[36..40]);
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(54, wav.Length);
        Assert.Equal(46, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
    }
}
