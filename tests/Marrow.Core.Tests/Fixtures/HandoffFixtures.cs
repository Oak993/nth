using System.Runtime.CompilerServices;
using System.Text.Json;
using Marrow.Core.Models;
using Xunit;

namespace Marrow.Core.Tests.Fixtures;

// Offline recordings in fixtures/handoff use Marrow's existing ThreadDocument storage schema.
public static class HandoffFixtures
{
    public const string ReadMiddle = "read-only middle detail 1234567";

    public static ThreadDocument Read(string name) =>
        JsonSerializer.Deserialize<ThreadDocument>(File.ReadAllText(Path.Combine(FixtureRoot(), name)))!;

    private static string FixtureRoot([CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, "../../fixtures/handoff");

    // Compare every leaf, including file paths, decisions, numbers, user instructions and buried tool details.
    // This checker is independent of summary selection. The negative control must fail when recovery is broken.
    public static void AssertFidelity(ThreadDocument original, string summary, ThreadDocument archive)
    {
        using var before = JsonDocument.Parse(JsonSerializer.Serialize(original));
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(archive));
        var reachable = Leaves(after.RootElement).ToHashSet(StringComparer.Ordinal);
        foreach (var detail in Leaves(before.RootElement))
            Assert.True(summary.Contains(detail, StringComparison.Ordinal) || reachable.Contains(detail), $"Lost detail: {detail}");
    }

    private static IEnumerable<string> Leaves(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Leaves(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Leaves),
        JsonValueKind.String => [element.GetString()!],
        _ => [element.GetRawText()],
    };
}
