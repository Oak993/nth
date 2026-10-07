// PORT-ONLY: OD-0116, OD-0122, ED-439. The run-time check that the connected `opencode serve` offers the "no reply" message, and the cache of the verdict per executable build.
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
                if (path.Value.ValueKind != JsonValueKind.Object || !path.Value.TryGetProperty("post", out var post)
                    || post.ValueKind != JsonValueKind.Object || !post.TryGetProperty("requestBody", out var body)
                    || body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Object || !content.TryGetProperty("application/json", out var json)
                    || json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("schema", out var schema)
                    || schema.ValueKind != JsonValueKind.Object) continue;
                if (schema.TryGetProperty("$ref", out var reference))
                {
                    if (reference.ValueKind != JsonValueKind.String || reference.GetString() is not { } value) continue;
                    var match = SchemaReference().Match(value);
                    if (!match.Success || match.Value != value || !root.TryGetProperty("components", out var components)
                        || components.ValueKind != JsonValueKind.Object || !components.TryGetProperty("schemas", out var schemas)
                        || schemas.ValueKind != JsonValueKind.Object || !schemas.TryGetProperty(match.Groups[1].Value, out schema)
                        || schema.ValueKind != JsonValueKind.Object) continue;
                }
                if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
                    && properties.TryGetProperty("noReply", out var noReply) && noReply.ValueKind == JsonValueKind.Object
                    && (!noReply.EnumerateObject().Any()
                        || noReply.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "boolean"))
                    return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
