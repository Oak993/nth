using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// Files and rollouts for the TokenLedger load and fingerprint tests (DroppyCode/Services/Store/TokenLedger.swift:269-319,
// :359-377, :155-182). File names are the Swift ones: token-activity-live-v2.json (:356), token-activity-codex-v3.json (:274),
// -v2.json (:292) and the v1 token-activity-codex.json (:281).
public static class TokenLedgerFiles
{
    public const string LegacyCodexFilesKey = "droppycode.tokenActivity.codexFiles"; // :271
    public const string LegacyLiveDailyKey = "droppycode.tokenActivity.liveDaily"; // :85

    public static string LiveState(StorageScratchFolder scratch) => Path.Combine(scratch.Root, "token-activity-live-v2.json");

    public static string FingerprintsV3(StorageScratchFolder scratch) => Path.Combine(scratch.Root, "token-activity-codex-v3.json");

    public static string FingerprintsV2(StorageScratchFolder scratch) => Path.Combine(scratch.Root, "token-activity-codex-v2.json");

    public static string FingerprintsV1(StorageScratchFolder scratch) => Path.Combine(scratch.Root, "token-activity-codex.json");

    /// <summary>Writes UTF-8 text without a BOM, creating the root the way the app's launch does before a ledger loads.</summary>
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>A ledger built the way <c>static let shared</c> builds it (its initializer is private).</summary>
    public static TokenLedger NewLedger(TestRuntime runtime) =>
        runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);

    /// <summary>One named map of the persisted live state (:332-341), read back from the file.</summary>
    public static Dictionary<string, long> ReadMap(string path, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty(name).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt64(), StringComparer.Ordinal);
    }

    /// <summary>A rollout line whose final token_count event totals <paramref name="totalTokens"/> (the shape the scan reads, :523-526, :562).</summary>
    public static string TokenCountLine(long totalTokens, long lastTokenUsageTotal, string timestamp) =>
        "{\"timestamp\":\"" + timestamp + "\",\"payload\":{\"info\":{\"token_count\":true,\"total_token_usage\":{\"total_tokens\":" + totalTokens +
        "},\"last_token_usage\":{\"total_tokens\":" + lastTokenUsageTotal + "}}}}";

    /// <summary>Writes a rollout under the scratch home's <c>.codex/sessions</c> with the given last-write time.</summary>
    public static string WriteRollout(StorageScratchFolder scratch, string fileName, DateTimeOffset modified, string jsonl)
    {
        var path = Path.Combine(scratch.Home, ".codex", "sessions", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, jsonl);
        File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        return path;
    }
}
