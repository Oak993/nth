using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the audit log's single writer and its anchors (DESIGN.md 12.5, unit AS-3): a second writer is refused,
// nothing else can open the day's file for writing, the head anchor in Credential Manager and each day's final HMAC
// (printed in the digest) match the files, a cut log is recorded in the chain and never heals, a line a crash cut off is
// repaired in the chain, a lost key restarts the chain visibly, and a refused key fails closed.
[Trait("Decision", "ED-NEW-dextra-as3-3")]
[Trait("Feature", "AS-3")]
public sealed class AuditLogWriterTests : IDisposable
{
    private const string Service = "nth.dextra.audit.test";
    private readonly AssistantTempRoot temp = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero));

    public void Dispose() => temp.Dispose();

    private string Today => Path.Combine(temp.AuditDirectory, "2026-10-12.jsonl");

    private static AuditEntry Action(string result) => new() { Assistant = "dextra", Tool = "shell", ActionClass = "Run", Result = result };

    private AuditLog Open() => AuditLog.Open(temp.Roots, temp.Keys, clock);

    private AuditVerification Verify(IReadOnlyDictionary<DateOnly, string>? printed = null) =>
        AuditVerifier.Verify(temp.Roots, temp.Keys, printed);

    private static string Event(string line) => JsonNode.Parse(line)!["event"]?.GetValue<string>() ?? "";

    // Read as the Activity view reads: shared, since a writer may hold the file.
    private static List<string> Lines(string path) =>
        Encoding.UTF8.GetString(AuditLine.ReadShared(path)).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    [Fact]
    public void A_second_writer_is_refused_and_nothing_else_can_write_the_days_file()
    {
        using var log = Open();
        log.Append(Action("one"));
        Assert.Throws<AuditLogBusyException>(() => AuditLog.Open(temp.Roots, temp.Keys, clock));
        Assert.ThrowsAny<IOException>(() => new FileStream(Today, FileMode.Append, FileAccess.Write, FileShare.ReadWrite).Dispose());
        Assert.ThrowsAny<IOException>(() => File.Delete(Today));
        using (var reader = new FileStream(Today, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) Assert.True(reader.Length > 0);
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status); // the Activity view reads while the writer holds it
    }

    [Fact]
    public void A_new_writer_after_the_first_closes_carries_the_chain_on()
    {
        using (var log = Open()) log.Append(Action("one"));
        using (var log = Open())
        {
            Assert.Equal(1, log.HeadSequence);
            Assert.Empty(log.OpenRecords);
            Assert.Equal(2, log.Append(Action("two")).Sequence);
        }
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
        Assert.Equal(2, Verify().LinesVerified);
    }

    [Fact]
    public void The_head_anchor_is_copied_after_each_batch_and_matches_the_last_line()
    {
        using var log = Open();
        var anchorTarget = SecretStore.TargetName(Service, "head");
        var before = temp.Credentials.Operations.Count(o => o == "write " + anchorTarget);
        var receipts = log.AppendBatch([Action("a"), Action("b"), Action("c"), Action("d"), Action("e")]);
        Assert.Equal(before + 1, temp.Credentials.Operations.Count(o => o == "write " + anchorTarget));
        Assert.Equal([1L, 2L, 3L, 4L, 5L], receipts.Select(r => r.Sequence));
        var anchor = temp.Keys.ReadAnchor()!;
        Assert.Equal(new AuditAnchor(5, receipts[^1].Hmac, "2026-10-12.jsonl"), anchor);
        Assert.Equal(JsonNode.Parse(Lines(Today)[^1])!["hmac"]!.GetValue<string>(), anchor.Hmac);
        Assert.Equal(log.HeadHmac, anchor.Hmac);
        Assert.False(log.AnchorBehind);
    }

    [Fact]
    public void Lines_cut_from_the_end_no_longer_reach_the_anchor_and_a_reopen_records_the_cut_for_good()
    {
        using (var log = Open()) log.AppendBatch([Action("a"), Action("b"), Action("c")]);
        var lines = Lines(Today);
        File.WriteAllText(Today, lines[0] + "\n");
        var cut = Verify();
        Assert.Equal(AuditVerificationStatus.Failed, cut.Status);
        Assert.Contains("lines after 1 are missing: the anchor in Credential Manager names line 3", cut.Reason, StringComparison.Ordinal);
        using (var log = Open())
        {
            Assert.Equal(["logCut"], log.OpenRecords);
            log.Append(Action("after the cut"));
        }
        var after = Lines(Today);
        Assert.Equal("logCut", Event(after[1]));
        Assert.Equal(3, JsonNode.Parse(after[1])!["anchorSeq"]!.GetValue<long>());
        Assert.Equal(1, JsonNode.Parse(after[1])!["foundSeq"]!.GetValue<long>());
        var result = Verify(); // the anchor now matches the new head, but the cut stays in the chain
        Assert.Equal(AuditVerificationStatus.Failed, result.Status);
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 2, 2), result.FirstFailure);
        Assert.Contains("the log was cut: line 3 had been written, but the files ended at line 1", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_log_deleted_is_recorded_as_a_cut_when_the_writer_opens()
    {
        using (var log = Open()) log.AppendBatch([Action("a"), Action("b")]);
        File.Delete(Today);
        Assert.Equal(AuditVerificationStatus.Failed, Verify().Status);
        using (var log = Open()) Assert.Equal(["logCut"], log.OpenRecords);
        Assert.Equal(AuditVerificationStatus.Failed, Verify().Status);
    }

    [Fact]
    public void Each_days_final_hmac_matches_and_a_day_rewritten_with_the_key_fails_the_printed_anchor()
    {
        using (var log = Open())
        {
            log.Append(Action("late on the 12th"));
            clock.Advance(TimeSpan.FromHours(2));
            log.Append(Action("early on the 13th"));
        }
        var day12 = new DateOnly(2026, 10, 12);
        var printed = new Dictionary<DateOnly, string> { [day12] = AuditVerifier.DayFinal(temp.AuditDirectory, day12)! };
        Assert.Equal(JsonNode.Parse(Lines(Today)[^1])!["hmac"]!.GetValue<string>(), printed[day12]);
        Assert.Equal(AuditVerificationStatus.Verified, Verify(printed).Status);
        // The stated limit (12.5): something holding the key can reseal the whole chain and move the head anchor too.
        var key = temp.Keys.ReadKey()!;
        var previous = new string('0', 64);
        var files = new[] { Today, Path.Combine(temp.AuditDirectory, "2026-10-13.jsonl") };
        string last = "";
        foreach (var path in files)
        {
            var resealed = new List<string>();
            foreach (var line in Lines(path))
            {
                var fields = JsonNode.Parse(line)!.AsObject();
                fields.Remove("hmac");
                fields["result"] = "rewritten";
                fields["prev"] = previous;
                var core = fields.ToJsonString();
                var mac = AuditLine.Mac(key, core, previous);
                resealed.Add(AuditLine.Seal(core, mac));
                previous = mac;
                last = path;
            }
            File.WriteAllText(path, string.Concat(resealed.Select(l => l + "\n")));
        }
        temp.Keys.WriteAnchor(new AuditAnchor(2, previous, Path.GetFileName(last)));
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
        var result = Verify(printed); // but no longer ends the 12th as the morning digest printed it
        Assert.Equal(AuditVerificationStatus.Failed, result.Status);
        Assert.Equal("2026-10-12.jsonl", result.FirstFailure!.File);
        Assert.Contains("the digest printed for 2026-10-12", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_cut_off_by_a_crash_is_repaired_in_the_chain_and_the_log_verifies()
    {
        using (var log = Open()) log.AppendBatch([Action("a"), Action("b")]);
        var piece = "{\"v\":1,\"seq\":3,\"time\":\"2026-10-12T23:00:00.000Z\",\"assistant\":\"dex";
        File.AppendAllText(Today, piece);
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status); // a piece being written is not counted
        using (var log = Open())
        {
            Assert.Equal(["tornLine"], log.OpenRecords);
            Assert.Equal(4, log.Append(Action("c")).Sequence);
        }
        var lines = Lines(Today);
        Assert.Equal(piece, lines[2]);
        Assert.Equal("tornLine", Event(lines[3]));
        Assert.Equal(AuditLine.Sha256(Encoding.UTF8.GetBytes(piece)), JsonNode.Parse(lines[3])!["torn"]!.GetValue<string>());
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Verified, result.Status);
        Assert.Equal([new AuditLineRef("2026-10-12.jsonl", 3, null)], result.Recovered);
        Assert.Equal(4, result.LinesVerified);
    }

    [Fact]
    public void A_line_cut_just_before_its_newline_is_repaired_and_never_counted_twice()
    {
        AuditReceipt first;
        using (var log = Open())
        {
            first = log.Append(Action("a"));
            log.Append(Action("b"));
        }
        var lines = Lines(Today);
        // The crash came as the second line's newline was due, so the line never reached the anchor either.
        File.WriteAllText(Today, lines[0] + "\n" + lines[1]);
        temp.Keys.WriteAnchor(new AuditAnchor(1, first.Hmac, first.File));
        using (var log = Open()) Assert.Equal(["tornLine"], log.OpenRecords);
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Verified, result.Status);
        Assert.Single(result.Recovered);
        Assert.Equal(2, result.LinesVerified);
    }

    [Fact]
    public void A_cut_record_cannot_be_forged_without_the_key()
    {
        using (var log = Open()) log.AppendBatch([Action("a"), Action("b"), Action("c")]);
        var lines = Lines(Today);
        var fake = JsonNode.Parse(lines[1])!.AsObject();
        fake["result"] = "edited";
        lines[1] = fake.ToJsonString();
        var forged = new JsonObject
        {
            ["v"] = 1, ["seq"] = 2, ["time"] = "2026-10-12T23:00:00.000Z", ["event"] = "tornLine",
            ["torn"] = AuditLine.Sha256(Encoding.UTF8.GetBytes(lines[1])), ["assistant"] = "nth", ["tool"] = "audit",
            ["class"] = "Audit", ["inputs"] = new JsonArray(), ["prev"] = JsonNode.Parse(lines[0])!["hmac"]!.GetValue<string>(),
            ["hmac"] = new string('b', 64),
        };
        lines.Insert(2, forged.ToJsonString());
        File.WriteAllText(Today, string.Concat(lines.Select(l => l + "\n")));
        Assert.Equal(2, Verify().FirstFailure!.Line);
    }

    [Fact]
    public void A_lost_key_starts_a_new_chain_visibly_and_the_earlier_lines_no_longer_verify()
    {
        using (var log = Open()) log.AppendBatch([Action("a"), Action("b")]);
        temp.Credentials.Remove(SecretStore.TargetName(Service, "hmac-key"));
        using (var log = Open())
        {
            Assert.Equal(["keyReplaced"], log.OpenRecords);
            log.Append(Action("c"));
        }
        var lines = Lines(Today);
        Assert.Equal("keyReplaced", Event(lines[2]));
        Assert.Equal(new string('0', 64), JsonNode.Parse(lines[2])!["prev"]!.GetValue<string>());
        Assert.Equal(3, JsonNode.Parse(lines[2])!["seq"]!.GetValue<long>());
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Failed, result.Status);
        Assert.Equal(1, result.FirstFailure!.Line);
        Assert.Contains("written under an earlier key that is gone (a new key starts at line 3", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_anchor_copy_keeps_the_lines_and_the_next_batch_copies_it_again()
    {
        using var log = Open();
        log.Append(Action("a"));
        var anchorTarget = SecretStore.TargetName(Service, "head");
        temp.Credentials.BeforeWrite = c => c.TargetName == anchorTarget ? InMemoryCredentialBackend.ErrorAccessDenied : 0;
        var receipt = log.Append(Action("b"));
        Assert.True(log.AnchorBehind);
        Assert.Equal(2, receipt.Sequence);
        Assert.Equal(1, temp.Keys.ReadAnchor()!.Sequence);
        temp.Credentials.BeforeWrite = null;
        log.Append(Action("c"));
        Assert.False(log.AnchorBehind);
        Assert.Equal(3, temp.Keys.ReadAnchor()!.Sequence);
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
    }

    [Fact]
    public void A_key_credential_manager_refuses_fails_closed_and_leaves_the_folder_free()
    {
        temp.Credentials.BeforeWrite = _ => InMemoryCredentialBackend.ErrorAccessDenied;
        Assert.Throws<AuditKeyUnavailableException>(Open);
        Assert.Empty(Directory.EnumerateFiles(temp.AuditDirectory, "*.jsonl"));
        temp.Credentials.BeforeWrite = null;
        using var log = Open();
        Assert.Equal(1, log.Append(Action("a")).Sequence);
    }

    [Fact]
    public void A_clock_that_steps_back_over_midnight_stays_on_the_later_day_file()
    {
        using (var log = Open())
        {
            clock.Advance(TimeSpan.FromHours(2)); // 01:00 on the 13th
            log.Append(Action("on the 13th"));
        }
        var stepped = new ManualTimeProvider(new DateTimeOffset(2026, 10, 12, 23, 59, 0, TimeSpan.Zero));
        using (var log = AuditLog.Open(temp.Roots, temp.Keys, stepped)) log.Append(Action("clock stepped back"));
        Assert.False(File.Exists(Today));
        Assert.Equal(2, Lines(Path.Combine(temp.AuditDirectory, "2026-10-13.jsonl")).Count);
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
    }

    [Fact]
    public void A_disposed_writer_refuses_more_lines()
    {
        var log = Open();
        log.Dispose();
        Assert.Throws<ObjectDisposedException>(() => log.Append(Action("late")));
    }
}
