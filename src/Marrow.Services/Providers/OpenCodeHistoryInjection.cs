// PORT-ONLY: OD-0116, OD-0122, ED-NEW-ma-engine-7. The run-time check that the connected `opencode serve` offers the "no reply" message, and the cache of the verdict per executable build.
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Port;

namespace Marrow.Services.Providers;

public static partial class OpenCodeHistoryInjection
{
    /// <summary>Offline seam: starts the server of this configuration's OpenCode. The default launches `opencode serve` on a free local port.</summary>
    public static Ambient<Func<SessionConfiguration, CancellationToken, Task<IOpenCodeServer>>> Server { get; } = new(OpenCodeServer.StartAsync);

    private static readonly ConcurrentDictionary<string, bool> ByExecutable = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(SwiftURL executable)
    {
        var info = new FileInfo(executable.Path);
        return info.Exists ? executable.Path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks : executable.Path + "|missing";
    }

    public static bool? Known(SwiftURL executable) => ByExecutable.TryGetValue(Key(executable), out var verdict) ? verdict : null;

    public static void Remember(SwiftURL executable, bool supported) => ByExecutable[Key(executable)] = supported;

    [GeneratedRegex(@"#/components/schemas/([A-Za-z0-9_.\-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaReference();

    /// <summary>
    /// Whether the document's `POST /session/{id}/message` accepts `noReply`, in its own request body or in a schema that body refers to.
    /// Anything that is not that document, or that does not say so, is false: the standby then falls back.
    /// </summary>
    public static bool OpenApiSupportsNoReply(string? document)
    {
        if (string.IsNullOrWhiteSpace(document)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(document);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object) return false;
            foreach (var path in paths.EnumerateObject())
            {
                if (!path.Name.StartsWith("/session/", StringComparison.Ordinal) || !path.Name.EndsWith("/message", StringComparison.Ordinal)) continue;
                if (path.Value.ValueKind != JsonValueKind.Object || !path.Value.TryGetProperty("post", out var post)) continue;
                var text = post.GetRawText();
                if (text.Contains("\"noReply\"", StringComparison.Ordinal)) return true;
                foreach (Match reference in SchemaReference().Matches(text))
                {
                    if (root.TryGetProperty("components", out var components) && components.TryGetProperty("schemas", out var schemas)
                        && schemas.TryGetProperty(reference.Groups[1].Value, out var schema) && schema.GetRawText().Contains("\"noReply\"", StringComparison.Ordinal))
                        return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
