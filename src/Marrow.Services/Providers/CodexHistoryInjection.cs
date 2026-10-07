// PORT-ONLY: OD-0116, OD-0122, ED-439. The run-time check that the connected `codex app-server` can load history without a model turn (`thread/inject_items`).
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Providers;

public static partial class CodexHistoryInjection
{
    /// <summary>The first release verified to document and test `thread/inject_items` (research 2026-10-06, codex-cli 0.159.3).</summary>
    public static readonly (int Major, int Minor, int Patch) MinimumVersion = (0, 159, 3);

    /// <summary>Offline seam: whether the CLI's own generated schema lists `thread/inject_items`. The default runs `codex app-server generate-json-schema` into a temporary folder.</summary>
    public static Ambient<Func<SwiftURL, IReadOnlyDictionary<string, string>, Task<bool>>> SchemaProbe { get; } = new(Probe);

    private static readonly ConcurrentDictionary<string, bool> Verified = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> ByExecutable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The executable's identity (path, size, write time): a new CLI build is a new key, so a cached verdict never outlives an update.</summary>
    private static string ExecutableKey(SwiftURL executable)
    {
        var info = new FileInfo(executable.Path);
        return info.Exists ? executable.Path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks : executable.Path + "|missing";
    }

    /// <summary>The verdict already reached for this exact executable, or null when it has not been checked.</summary>
    public static bool? Known(SwiftURL executable) => ByExecutable.TryGetValue(ExecutableKey(executable), out var verdict) ? verdict : null;

    [GeneratedRegex(@"^[^/\s]+(?: [^/\s]+)*/(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex UserAgentVersion();

    /// <summary>The initialize reply's `userAgent` is `originator/version (os; arch) ...`; originators such as Codex Desktop contain spaces. The version is the CLI's own build.</summary>
    public static bool VersionSupportsInjection(string? userAgent)
    {
        if (userAgent is null || UserAgentVersion().Match(userAgent) is not { Success: true } match) return false;
        if (!int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch)) return false;
        return (major, minor, patch).CompareTo(MinimumVersion) >= 0;
    }

    /// <summary>The generated ClientRequest schema names every request method; the stable schema carries `thread/inject_items`, so no experimental capability is needed.</summary>
    public static bool SchemaListsInjection(string? schema) => !string.IsNullOrEmpty(schema) && schema.Contains("\"thread/inject_items\"", StringComparison.Ordinal);

    /// <summary>The version gate, then the schema check once per executable and CLI version.</summary>
    public static async Task<bool> SupportedAsync(SwiftURL executable, IReadOnlyDictionary<string, string> environment, string? userAgent)
    {
        if (!VersionSupportsInjection(userAgent))
        {
            ByExecutable[ExecutableKey(executable)] = false;
            return false;
        }
        var key = ExecutableKey(executable) + "|" + userAgent!;
        if (!Verified.TryGetValue(key, out var listed))
        {
            try { listed = await SchemaProbe.Current(executable, environment); }
            catch { return false; }
            Verified[key] = listed;
        }
        ByExecutable[ExecutableKey(executable)] = listed;
        return listed;
    }

    private static async Task<bool> Probe(SwiftURL executable, IReadOnlyDictionary<string, string> environment)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? Path.GetTempPath();
        var folder = Path.Combine(root, "marrow-codex-schema-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var run = await Shell.Run(executable.Path, ["app-server", "generate-json-schema", "--out", folder],
                environment: CodexChildEnvironment.Apply(environment), timeout: 60);
            if (!run.Succeeded) return false;
            var path = Path.Combine(folder, "ClientRequest.json");
            return File.Exists(path) && SchemaListsInjection(await File.ReadAllTextAsync(path));
        }
        catch { return false; }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch { /* A leftover temporary folder is harmless. */ }
        }
    }
}
