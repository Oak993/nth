using System.Text;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

/// <summary>One part of a multipart/form-data body.</summary>
internal sealed record MultipartPart(string Name, string? FileName, string? ContentType, byte[] Data)
{
    public string Text => Encoding.UTF8.GetString(Data);
}

/// <summary>Shared pieces of the VT-16 tests: an in-memory key store, a second of audio, a multipart reader and server-sent events.</summary>
internal static class CloudFixtures
{
    public const string Key = "sk-test-0123456789abcdef";

    public static (VoiceKeyStore Keys, InMemoryCredentialBackend Backend) KeyStore(string? key = Key)
    {
        var backend = new InMemoryCredentialBackend();
        var keys = new VoiceKeyStore(new SecretStore(backend));
        if (key is not null) Assert.True(keys.Save(key));
        return (keys, backend);
    }

    /// <summary>A tone of <paramref name="seconds"/> at 16 kHz.</summary>
    public static float[] Audio(double seconds) =>
        Enumerable.Range(0, (int)(seconds * 16000)).Select(i => 0.5f * MathF.Sin(i * 0.05f)).ToArray();

    public static DecodeRequest Request(string language = "auto", DecodeMode mode = DecodeMode.Final) => new(language, "", mode, TimeSpan.Zero);

    /// <summary>The handler a test puts in the transcriber: it answers the one call it is given and records it.</summary>
    public static InProcessHttpHandler Answering(Func<InProcessRequest, HttpResponseMessage> answer) => new() { Answer = answer };

    public static IReadOnlyList<MultipartPart> Multipart(InProcessRequest request)
    {
        var contentType = request.Header("Content-Type")!;
        var boundary = contentType[(contentType.IndexOf("boundary=", StringComparison.Ordinal) + 9)..].Trim('"');
        var latin = Encoding.Latin1.GetString(request.Body);          // bytes as chars, so binary survives the split
        var parts = new List<MultipartPart>();
        foreach (var raw in latin.Split("--" + boundary, StringSplitOptions.None).Skip(1))
        {
            if (raw.StartsWith("--", StringComparison.Ordinal)) break;
            var body = raw[2..];                                       // the line break after the boundary
            var split = body.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var head = body[..split];
            var data = body[(split + 4)..];
            data = data[..^2];                                         // the line break before the next boundary
            string? Field(string text, string name)
            {
                // .NET writes a name that is a plain token unquoted (name=file) and any other quoted
                var match = System.Text.RegularExpressions.Regex.Match(text, "[; ]" + name + "=(?:\"([^\"]*)\"|([^;\\r\\n]*))", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                return !match.Success ? null : match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            }
            var type = head.Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))?["Content-Type:".Length..].Trim();
            parts.Add(new MultipartPart(Field(head, "name")!, Field(head, "filename"), type, Encoding.Latin1.GetBytes(data)));
        }
        return parts;
    }

    public static string Sse(params string[] events) => string.Concat(events.Select(e => "data: " + e + "\n\n"));

    public static string Delta(string text) => "{\"type\":\"transcript.text.delta\",\"delta\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}";

    public static string Done(string text) => "{\"type\":\"transcript.text.done\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}";
}
