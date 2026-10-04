// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): voice-SPEC V-18.1, "transcript.text.delta events ...;
// transcript.text.done ends it".
using System.Text;
using System.Text.Json;

namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>Reads the streaming reply's events as they arrive.</summary>
public static class OpenAiSseReader
{
    public const string DeltaType = "transcript.text.delta";
    public const string DoneType = "transcript.text.done";

    /// <summary>
    /// The deltas and the end, in order. An event is the `data:` lines up to a blank line; its JSON `type` says which it is; any other
    /// type (and a `data:` that is not JSON) is skipped.
    /// </summary>
    public static async IAsyncEnumerable<OpenAiEvent> Read(Stream body, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var data = new StringBuilder();
        string? line;
        while (true)
        {
            line = await reader.ReadLineAsync(cancellationToken);
            if (line is null || line.Length == 0)
            {
                if (data.Length > 0 && Parse(data.ToString()) is { } parsed) yield return parsed;
                data.Clear();
                if (line is null) yield break;
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var value = line.AsSpan(5);
            if (value.Length > 0 && value[0] == ' ') value = value[1..];
            if (data.Length > 0) data.Append('\n');
            data.Append(value);
        }
    }

    private static OpenAiEvent? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return null;
            switch (type.GetString())
            {
                case DeltaType when root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String:
                    return new OpenAiEvent(false, delta.GetString()!);
                case DoneType when root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String:
                    return new OpenAiEvent(true, text.GetString()!);
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
