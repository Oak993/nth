using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the audit log's keyed chain (DESIGN.md 12.5, unit AS-3): each line's HMAC-SHA256 over the line and the
// previous line's HMAC verifies; an edit, a removed, added or moved line, a missing day and a forged line each fail at
// the first line they touch; a chain recomputed as plain SHA-256 (or under any other key) is detected; and the key is
// never on disk. Clock: ManualTimeProvider (UTC as the local zone), so the day files are fixed.
[Trait("Decision", "ED-NEW-dextra-as3-3")]
[Trait("Feature", "AS-3")]
public sealed class AuditLogChainTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero));

    public void Dispose() => temp.Dispose();

    private string DayFile(string day) => Path.Combine(temp.AuditDirectory, day + ".jsonl");

    private static AuditEntry Action(string tool, string result) => new()
    {
        Assistant = "dextra",
        Mission = "m-1",
        Agent = "thread-7",
        Tool = tool,
        ActionClass = "Run",
        Target = "E:/work/repo",
        Inputs = [AuditInput.Plain("command", "dotnet test"), AuditInput.Numeric("attempt", 2), AuditInput.Boolean("dry", false)],
        Result = result,
        CostUsd = 0.0125m,
        Tokens = 1840,
        AllowedBy = "grant:g-12",
    };

    private void WriteLines(int count)
    {
        using var log = AuditLog.Open(temp.Roots, temp.Keys, clock);
        for (var i = 1; i <= count; i++) log.Append(Action("shell", "ok " + i));
    }

    private static byte[] Joined(byte[] first, byte[] second) => [.. first, .. second];

    private AuditVerification Verify() => AuditVerifier.Verify(temp.Roots, temp.Keys);

    private List<string> Lines(string day) => File.ReadAllText(DayFile(day)).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    private void Rewrite(string day, IEnumerable<string> lines) => File.WriteAllText(DayFile(day), string.Concat(lines.Select(l => l + "\n")));

    [Fact]
    public void Every_line_records_the_action_and_the_whole_chain_verifies()
    {
        WriteLines(3);
        var lines = Lines("2026-10-12");
        Assert.Equal(3, lines.Count);
        var first = JsonNode.Parse(lines[0])!.AsObject();
        Assert.Equal(1, first["seq"]!.GetValue<long>());
        Assert.Equal("2026-10-12T09:30:00.000Z", first["time"]!.GetValue<string>());
        Assert.Equal("dextra", first["assistant"]!.GetValue<string>());
        Assert.Equal("m-1", first["mission"]!.GetValue<string>());
        Assert.Equal("thread-7", first["agent"]!.GetValue<string>());
        Assert.Equal("shell", first["tool"]!.GetValue<string>());
        Assert.Equal("Run", first["class"]!.GetValue<string>());
        Assert.Equal("E:/work/repo", first["target"]!.GetValue<string>());
        Assert.Equal("dotnet test", first["inputs"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("ok 1", first["result"]!.GetValue<string>());
        Assert.Equal(0.0125m, first["costUsd"]!.GetValue<decimal>());
        Assert.Equal(1840, first["tokens"]!.GetValue<long>());
        Assert.Equal("grant:g-12", first["allowedBy"]!.GetValue<string>());
        Assert.Equal(new string('0', 64), first["prev"]!.GetValue<string>());
        for (var i = 1; i < 3; i++)
            Assert.Equal(JsonNode.Parse(lines[i - 1])!["hmac"]!.GetValue<string>(), JsonNode.Parse(lines[i])!["prev"]!.GetValue<string>());
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Verified, result.Status);
        Assert.Equal(3, result.LinesVerified);
        Assert.Equal("Verified", result.Summary);
    }

    [Fact]
    public void The_hmac_is_the_keyed_hash_of_the_line_and_the_previous_hmac()
    {
        WriteLines(2);
        var second = Lines("2026-10-12")[1];
        var hmac = JsonNode.Parse(second)!["hmac"]!.GetValue<string>();
        var prev = JsonNode.Parse(second)!["prev"]!.GetValue<string>();
        var core = second[..second.IndexOf(",\"hmac\":\"", StringComparison.Ordinal)] + "}";
        var key = temp.Keys.ReadKey()!;
        var expected = HMACSHA256.HashData(key, Joined(Encoding.UTF8.GetBytes(core), Convert.FromHexString(prev)));
        Assert.Equal(Convert.ToHexStringLower(expected), hmac);
    }

    [Fact]
    public void An_edited_line_fails_at_that_line()
    {
        WriteLines(4);
        var lines = Lines("2026-10-12");
        lines[2] = lines[2].Replace("\"ok 3\"", "\"no 3\"", StringComparison.Ordinal);
        Rewrite("2026-10-12", lines);
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Failed, result.Status);
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 3, 3), result.FirstFailure);
        Assert.Equal(2, result.LinesVerified);
        Assert.Contains("HMAC does not match", result.Reason, StringComparison.Ordinal);
        Assert.StartsWith("Line 3 of 2026-10-12.jsonl fails: ", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chain_recomputed_as_plain_sha256_is_detected()
    {
        WriteLines(3);
        // Anything that can write the folder rewrites line 2 and reseals every line from it with a plain hash chain.
        var lines = Lines("2026-10-12");
        var previous = JsonNode.Parse(lines[0])!["hmac"]!.GetValue<string>();
        for (var i = 1; i < lines.Count; i++)
        {
            var fields = JsonNode.Parse(lines[i])!.AsObject();
            fields.Remove("hmac");
            if (i == 1) fields["result"] = "rewritten";
            fields["prev"] = previous;
            var core = fields.ToJsonString();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Joined(Encoding.UTF8.GetBytes(core), Convert.FromHexString(previous))));
            lines[i] = core[..^1] + ",\"hmac\":\"" + hash + "\"}";
            previous = hash;
        }
        Rewrite("2026-10-12", lines);
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Failed, result.Status);
        Assert.Equal(2, result.FirstFailure!.Line);
    }

    [Fact]
    public void A_chain_resealed_under_any_other_key_is_detected()
    {
        WriteLines(3);
        var lines = Lines("2026-10-12");
        var guessed = RandomNumberGenerator.GetBytes(32);
        var previous = new string('0', 64);
        for (var i = 0; i < lines.Count; i++)
        {
            var core = lines[i][..lines[i].IndexOf(",\"hmac\":\"", StringComparison.Ordinal)] + "}";
            var fields = JsonNode.Parse(core)!.AsObject();
            fields["prev"] = previous;
            core = fields.ToJsonString();
            var mac = Convert.ToHexStringLower(HMACSHA256.HashData(guessed, Joined(Encoding.UTF8.GetBytes(core), Convert.FromHexString(previous))));
            lines[i] = core[..^1] + ",\"hmac\":\"" + mac + "\"}";
            previous = mac;
        }
        Rewrite("2026-10-12", lines);
        Assert.Equal(1, Verify().FirstFailure!.Line);
    }

    [Fact]
    public void A_removed_line_fails_at_the_line_after_it()
    {
        WriteLines(4);
        var lines = Lines("2026-10-12");
        lines.RemoveAt(1);
        Rewrite("2026-10-12", lines);
        var result = Verify();
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 2, 3), result.FirstFailure);
        Assert.Contains("numbered 3, but line 2 comes next", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_lines_swapped_fail_at_the_first_of_them()
    {
        WriteLines(4);
        var lines = Lines("2026-10-12");
        (lines[1], lines[2]) = (lines[2], lines[1]);
        Rewrite("2026-10-12", lines);
        Assert.Equal(2, Verify().FirstFailure!.Line);
    }

    [Fact]
    public void A_line_added_without_the_key_fails_at_that_line()
    {
        WriteLines(2);
        var lines = Lines("2026-10-12");
        var forged = JsonNode.Parse(lines[1])!.AsObject();
        forged["seq"] = 3;
        forged["prev"] = forged["hmac"]!.GetValue<string>();
        forged["hmac"] = new string('a', 64);
        lines.Add(forged.ToJsonString());
        Rewrite("2026-10-12", lines);
        Assert.Equal(3, Verify().FirstFailure!.Line);
    }

    [Fact]
    public void The_chain_runs_across_day_files_and_a_missing_middle_day_is_detected()
    {
        using (var log = AuditLog.Open(temp.Roots, temp.Keys, clock))
        {
            log.Append(Action("shell", "day one"));
            clock.Advance(TimeSpan.FromDays(1));
            log.Append(Action("shell", "day two"));
            clock.Advance(TimeSpan.FromDays(1));
            log.Append(Action("shell", "day three"));
        }
        Assert.True(File.Exists(DayFile("2026-10-13")));
        Assert.Equal(JsonNode.Parse(Lines("2026-10-12")[0])!["hmac"]!.GetValue<string>(), JsonNode.Parse(Lines("2026-10-13")[0])!["prev"]!.GetValue<string>());
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
        File.Delete(DayFile("2026-10-13"));
        var result = Verify();
        Assert.Equal(new AuditLineRef("2026-10-14.jsonl", 1, 3), result.FirstFailure);
    }

    [Fact]
    public void The_key_is_never_on_disk_in_the_clear()
    {
        WriteLines(5);
        using (var store = AssistantStore.Open(temp.Roots)) store.SaveMission("m-1", new JsonObject { ["state"] = "working" });
        var key = temp.Keys.ReadKey()!;
        var forms = new[]
        {
            key,
            Encoding.UTF8.GetBytes(Convert.ToHexStringLower(key)),
            Encoding.UTF8.GetBytes(Convert.ToHexString(key)),
            Encoding.UTF8.GetBytes(Convert.ToBase64String(key)),
        };
        foreach (var path in Directory.EnumerateFiles(temp.Folder, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(path);
            foreach (var form in forms) Assert.Equal(-1, bytes.AsSpan().IndexOf(form));
        }
    }

    [Fact]
    public void Lines_without_a_held_key_are_reported_as_unverifiable_not_as_verified()
    {
        WriteLines(2);
        temp.Credentials.Remove(Marrow.Services.Port.SecretStore.TargetName("nth.dextra.audit.test", "hmac-key"));
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.KeyMissing, result.Status);
        Assert.Equal("Not verified: the log's key is missing", result.Summary);
    }

    [Fact]
    public void An_empty_log_says_so_and_rows_read_back_in_order()
    {
        Assert.Equal(AuditVerificationStatus.Empty, Verify().Status);
        WriteLines(3);
        var rows = AuditVerifier.ReadLines(temp.AuditDirectory);
        Assert.Equal([1L, 2L, 3L], rows.Select(r => r.At.Sequence!.Value));
        Assert.Equal("ok 2", rows[1].Fields["result"]!.GetValue<string>());
    }
}
