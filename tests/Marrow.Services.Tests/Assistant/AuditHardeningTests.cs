using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the AS-3 security review's six findings, each pinned (DESIGN.md 12.5, 12.7, 15.3; ED-NEW-dextra-as3-6): a
// repeated JSON name is a damaged line, never an exception; a line planted without the key never becomes the chain's
// head; a Credential Manager read that fails for a moment never replaces the key; a card's fields, other digit joints,
// escaped JSON and a secret split by the scan cut are masked; a far-ahead day file never takes the lines; and no line is
// ever appended onto a piece a failed write left. Every value is made up; card-like numbers use an unissued 9999 prefix.
[Trait("Decision", "ED-NEW-dextra-as3-6")]
[Trait("Feature", "AS-3")]
public sealed class AuditHardeningTests : IDisposable
{
    private const string Service = "nth.dextra.audit.test";
    private const int NoSuchLogonSession = 1312;
    private readonly AssistantTempRoot temp = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero));

    public void Dispose() => temp.Dispose();

    private string DayFile(string day) => Path.Combine(temp.AuditDirectory, day + ".jsonl");

    private string Today => DayFile("2026-10-12");

    private static AuditEntry Action(string result) => new() { Assistant = "dextra", Tool = "shell", ActionClass = "Run", Result = result };

    private AuditLog Open() => AuditLog.Open(temp.Roots, temp.Keys, clock);

    private AuditVerification Verify() => AuditVerifier.Verify(temp.Roots, temp.Keys);

    private static string Event(string line) => JsonNode.Parse(line)!["event"]?.GetValue<string>() ?? "";

    private static List<string> Lines(string path) =>
        Encoding.UTF8.GetString(AuditLine.ReadShared(path)).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Lines on the 11th (a closed day) and then on the 12th.</summary>
    private void WriteTwoDays(int onThe11th, int onThe12th)
    {
        var yesterday = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        using (var log = AuditLog.Open(temp.Roots, temp.Keys, yesterday))
            for (var i = 0; i < onThe11th; i++) log.Append(Action("day 11 #" + i));
        using (var log = Open())
            for (var i = 0; i < onThe12th; i++) log.Append(Action("day 12 #" + i));
    }

    private static string WithLuhnDigit(string body)
    {
        for (var digit = 0; digit <= 9; digit++)
            if (AuditRedactor.PassesLuhn(body + digit)) return body + digit;
        throw new InvalidOperationException("no check digit");
    }

    // ---- 1. A repeated JSON name ----

    [Fact]
    public void An_audit_line_that_repeats_a_name_is_damaged_and_never_stops_the_writer_or_the_verifier()
    {
        WriteTwoDays(1, 1);
        var a = new string('a', 64);
        var zeros = new string('0', 64);
        File.AppendAllText(DayFile("2026-10-11"),
            "{\"seq\":1,\"seq\":1,\"hmac\":\"" + a + "\"}\n"
            + "{\"v\":1,\"seq\":3,\"inputs\":[{\"name\":\"x\",\"name\":\"y\"}],\"prev\":\"" + zeros + "\",\"hmac\":\"" + a + "\"}\n");
        using (var log = Open())
        {
            Assert.Equal(2, log.HeadSequence);
            Assert.Empty(log.OpenRecords);
            Assert.Equal(3, log.Append(Action("after")).Sequence);
        }
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Failed, result.Status);
        Assert.Equal(new AuditLineRef("2026-10-11.jsonl", 2, null), result.FirstFailure);
        Assert.Equal("it does not parse.", result.Reason);
        Assert.Equal(3, AuditVerifier.ReadLines(temp.AuditDirectory).Count); // the two planted lines are no rows
    }

    [Fact]
    public void A_store_file_that_repeats_a_name_is_damaged_and_never_stops_the_store()
    {
        var crashing = AssistantStore.Open(temp.Roots);
        crashing.SaveMission("m-1", new JsonObject { ["step"] = 1 });
        crashing.SaveMission("m-2", new JsonObject { ["step"] = 1 });
        crashing.SaveProfile("dextra", new JsonObject { ["name"] = "Dextra" });
        crashing.AbandonForTests();
        var missions = Path.Combine(temp.Store, "missions");
        File.AppendAllText(Path.Combine(missions, "m-1.journal"), "{\"seq\":2,\"seq\":2,\"mission\":{\"step\":2}}\n");
        File.AppendAllText(Path.Combine(missions, "m-2.journal"), "{\"seq\":2,\"mission\":{\"step\":2,\"step\":3}}\n");
        File.WriteAllText(Path.Combine(temp.Store, "assistants", "dextra", "profile.json"), "{\"name\":\"a\",\"name\":\"b\"}");
        var feed = Path.Combine(temp.Store, "feed");
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(feed, "2026-10-12.jsonl"), "{\"kind\":\"a\",\"kind\":\"b\"}\n{\"kind\":\"kept\"}\n");

        using var store = AssistantStore.Open(temp.Roots);
        Assert.Contains("m-1.journal:2", store.OpenReport.Damaged);
        Assert.Contains("m-2.journal:2", store.OpenReport.Damaged);
        Assert.Equal(1, store.Mission("m-1")!.Body["step"]!.GetValue<int>());
        Assert.Equal(1, store.Mission("m-2")!.Body["step"]!.GetValue<int>());
        Assert.Null(store.Profile("dextra"));
        Assert.Equal("kept", Assert.Single(store.ReadFeed(new DateOnly(2026, 10, 12)))["kind"]!.GetValue<string>());
    }

    // ---- 2. The head comes only from a line sealed under the key ----

    [Fact]
    public void A_line_planted_without_the_key_never_becomes_the_head_and_removing_it_heals_the_view()
    {
        WriteTwoDays(2, 1);
        var realHead = JsonNode.Parse(Lines(Today)[^1])!;
        var forged = JsonNode.Parse(Lines(Today)[^1])!.AsObject();
        forged["seq"] = 99;
        forged["result"] = "planted";
        var planted = forged.ToJsonString();
        File.AppendAllText(DayFile("2026-10-11"), planted + "\n");
        using (var log = Open())
        {
            Assert.Equal(3, log.HeadSequence);
            Assert.Equal(realHead["hmac"]!.GetValue<string>(), log.HeadHmac);
            Assert.Empty(log.OpenRecords);
            Assert.Equal(4, log.Append(Action("next")).Sequence);
        }
        Assert.Equal(realHead["hmac"]!.GetValue<string>(), JsonNode.Parse(Lines(Today)[^1])!["prev"]!.GetValue<string>());
        Assert.Equal(new AuditLineRef("2026-10-11.jsonl", 3, 99), Verify().FirstFailure);
        File.WriteAllText(DayFile("2026-10-11"), string.Concat(Lines(DayFile("2026-10-11")).Where(l => l != planted).Select(l => l + "\n")));
        var healed = Verify();
        Assert.Equal(AuditVerificationStatus.Verified, healed.Status);
        Assert.Equal(4, healed.LinesVerified);
    }

    [Fact]
    public void A_cut_tail_behind_a_copy_of_the_anchored_line_is_still_recorded_as_a_cut()
    {
        WriteTwoDays(2, 3); // lines 1-2 on the 11th, 3-5 on the 12th; the anchor names line 5 in the 12th's file
        var today = Lines(Today);
        File.WriteAllText(Today, today[0] + "\n"); // lines 4 and 5 cut
        File.AppendAllText(DayFile("2026-10-11"), today[2] + "\n"); // line 5, copied whole, into the closed day
        using (var log = Open()) Assert.Equal(["logCut"], log.OpenRecords);
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Failed, result.Status);
        Assert.Equal(new AuditLineRef("2026-10-11.jsonl", 3, 5), result.FirstFailure);
    }

    [Fact]
    public void The_anchored_line_moved_to_another_day_file_fails_the_anchor_and_is_recorded_as_a_cut()
    {
        using (var log = Open()) log.AppendBatch([Action("a"), Action("b"), Action("c")]);
        var lines = Lines(Today);
        File.WriteAllText(Today, lines[0] + "\n" + lines[1] + "\n");
        File.WriteAllText(DayFile("2026-10-13"), lines[2] + "\n"); // the chain still reads in order
        var moved = Verify();
        Assert.Equal(AuditVerificationStatus.Failed, moved.Status);
        Assert.Contains("the day file the anchor in Credential Manager names", moved.Reason, StringComparison.Ordinal);
        using (var log = Open()) Assert.Equal(["logCut"], log.OpenRecords);
    }

    // ---- 3. The key is replaced only when it is gone ----

    [Fact]
    public void A_key_read_that_fails_for_a_moment_replaces_nothing_and_fails_closed()
    {
        using (var log = Open()) log.AppendBatch([Action("a"), Action("b")]);
        var keyTarget = SecretStore.TargetName(Service, "hmac-key");
        var heldKey = temp.Credentials.Peek(keyTarget)!.Blob;
        var writes = temp.Credentials.Operations.Count(o => o.StartsWith("write ", StringComparison.Ordinal));
        temp.RefuseRead = target => target == keyTarget ? NoSuchLogonSession : 0;
        Assert.Throws<AuditKeyUnavailableException>(Open);
        Assert.Equal(AuditVerificationStatus.KeyUnavailable, Verify().Status);
        Assert.Throws<AuditKeyUnavailableException>(() => temp.Keys.HasKey());
        Assert.Equal(heldKey, temp.Credentials.Peek(keyTarget)!.Blob);
        Assert.Equal(writes, temp.Credentials.Operations.Count(o => o.StartsWith("write ", StringComparison.Ordinal)));
        Assert.Equal(2, Lines(Today).Count);
        temp.RefuseRead = null;
        using (var log = Open())
        {
            Assert.Empty(log.OpenRecords);
            Assert.Equal(3, log.Append(Action("c")).Sequence);
        }
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
    }

    [Fact]
    public void An_anchor_read_that_fails_for_a_moment_fails_closed_and_keeps_the_anchor()
    {
        using (var log = Open()) log.Append(Action("a"));
        var anchorTarget = SecretStore.TargetName(Service, "head");
        var held = temp.Credentials.Peek(anchorTarget)!.Blob;
        temp.RefuseRead = target => target == anchorTarget ? InMemoryCredentialBackend.ErrorAccessDenied : 0;
        Assert.Throws<AuditKeyUnavailableException>(Open);
        Assert.Equal(AuditVerificationStatus.KeyUnavailable, Verify().Status);
        temp.RefuseRead = null;
        Assert.Equal(held, temp.Credentials.Peek(anchorTarget)!.Blob);
        using (var log = Open()) Assert.Empty(log.OpenRecords);
    }

    [Fact]
    public void A_key_item_that_is_not_32_bytes_is_no_key_and_is_replaced_visibly()
    {
        using (var log = Open()) log.Append(Action("a"));
        Assert.Equal(SecretStoreStatus.Success, new SecretStore(temp.Credentials).Update(Service, "hmac-key", new byte[16]));
        using var reopened = Open();
        Assert.Equal(["keyReplaced"], reopened.OpenRecords);
        Assert.Equal(AuditKeyStore.KeyBytes, temp.Credentials.Peek(SecretStore.TargetName(Service, "hmac-key"))!.Blob.Length);
    }

    // ---- 4. What the redactor now masks ----

    [Fact]
    public void A_cards_fields_are_masked_by_name_with_any_quotes()
    {
        var number = WithLuhnDigit("999988887777666");
        var dotted = $"{number[..4]}.{number[4..8]}.{number[8..12]}.{number[12..]}";
        Assert.Equal("{\"card_number\":\"[secret]\",\"cvc\":\"[secret]\",\"exp\":\"[secret]\"}",
            AuditRedactor.Redact("{\"card_number\":\"" + dotted + "\",\"cvc\":\"123\",\"exp\":\"12/29\"}"));
        Assert.Equal("{\"cardCvc\": [secret], \"expMonth\": [secret], \"billing.cardNumber\": \"[secret]\"}",
            AuditRedactor.Redact("{\"cardCvc\": 123, \"expMonth\": 12, \"billing.cardNumber\": \"" + number + "\"}"));
        Assert.Equal("cvv2=[secret]&security_code=[secret]&exp_year=[secret]",
            AuditRedactor.Redact("cvv2=987&security_code=4321&exp_year=2029"));
    }

    [Fact]
    public void A_secret_inside_escaped_json_or_single_quotes_is_masked()
    {
        Assert.Equal("{\"body\":\"{\\\"api_key\\\":\\\"[secret]\\\",\\\"n\\\":1}\"}",
            AuditRedactor.Redact("{\"body\":\"{\\\"api_key\\\":\\\"fake-key-123456\\\",\\\"n\\\":1}\"}"));
        Assert.Equal("{'password': '[secret]'}", AuditRedactor.Redact("{'password': 'fake-hunter2'}"));
        Assert.Equal("\\\\\\\"token\\\\\\\":\\\\\\\"[secret]\\\\\\\"", AuditRedactor.Redact("\\\\\\\"token\\\\\\\":\\\\\\\"abc\\\\def\\\\\\\""));
    }

    [Fact]
    public void A_card_number_with_other_joints_or_beside_other_digits_is_masked()
    {
        var number = WithLuhnDigit("999988887777666");
        var amex = WithLuhnDigit("99998888777766");
        foreach (var form in new[]
        {
            $"{number[..4]}.{number[4..8]}.{number[8..12]}.{number[12..]}",
            $"{number[..4]}_{number[4..8]}_{number[8..12]}_{number[12..]}",
            $"{number[..4]} {number[4..8]}\n{number[8..12]} {number[12..]}",
            $"{number[..4]}\r\n{number[4..8]}\r\n{number[8..12]}\r\n{number[12..]}",
            $"{number[..4]} - {number[4..8]} - {number[8..12]} - {number[12..]}",
            $"{number[..4]} {number[4..8]} {number[8..12]} {number[12..]}",
            $"{amex[..4]} {amex[4..10]} {amex[10..]}",
            $"{number[..4]} {number[4..8]} {number[8..12]} {number[12..]} 123",
            $"12 {number[..4]} {number[4..8]} {number[8..12]} {number[12..]}",
        })
        {
            var redacted = AuditRedactor.Redact("pay with " + form + " today");
            Assert.Equal("pay with [secret] today", redacted);
        }
    }

    [Fact]
    public void Words_that_hold_a_card_name_and_number_lists_are_kept_as_they_are()
    {
        foreach (var text in new[]
        {
            "regexp: foo",
            "japan: 12",
            "expected: 5",
            "company=acme",
            "panel=left",
            "hosts 10.0.0.1 10.0.0.2 10.0.0.3 10.0.0.4 10.0.0.5",
            "1 2 3 4 5 6 7 8 9 10 11 12 13 14 15",
            "build 2026.10.12.2300",
        })
            Assert.Equal(text, AuditRedactor.Redact(text));
    }

    [Fact]
    public void A_secret_split_by_the_scan_cut_is_never_kept_even_when_the_masked_text_is_short()
    {
        var number = WithLuhnDigit("999988887777666");
        var block = "-----BEGIN RSA PRIVATE KEY-----\n" + new string('A', 60000) + "\n-----END RSA PRIVATE KEY-----";
        var head = block + " card ";
        var text = head + new string('z', AuditRedactor.MaxScanChars - head.Length - 1 - 12) + " " + number + " and the rest";
        Assert.Equal(number[..12], text[(AuditRedactor.MaxScanChars - 12)..AuditRedactor.MaxScanChars]); // the cut splits the card
        var redacted = AuditRedactor.Redact(text);
        Assert.StartsWith("[secret] card zzz", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(number[..8], redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(number[8..12], redacted, StringComparison.Ordinal);
        Assert.EndsWith(" chars]", redacted, StringComparison.Ordinal);
    }

    // ---- 5. A far-ahead day file ----

    [Fact]
    public void An_empty_day_file_far_ahead_never_takes_the_lines()
    {
        Directory.CreateDirectory(temp.AuditDirectory);
        File.WriteAllText(DayFile("9999-12-31"), "");
        using (var log = Open()) log.Append(Action("a"));
        AuditReceipt last;
        using (var log = Open())
        {
            last = log.Append(Action("b"));
            Assert.Equal("2026-10-12.jsonl", last.File);
            Assert.Equal(last.Hmac, log.DayFinal(new DateOnly(2026, 10, 12)));
        }
        Assert.Equal(2, Lines(Today).Count);
        Assert.Equal(0, new FileInfo(DayFile("9999-12-31")).Length);
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
    }

    [Fact]
    public void A_piece_planted_in_tomorrows_file_is_not_adopted_and_is_closed_before_tomorrows_first_line()
    {
        using (var log = Open()) log.Append(Action("a"));
        File.WriteAllText(DayFile("2026-10-13"), "{\"v\":1,\"seq\":");
        using (var log = Open())
        {
            Assert.Empty(log.OpenRecords);
            Assert.Equal("2026-10-12.jsonl", log.Append(Action("b")).File);
            clock.Advance(TimeSpan.FromHours(2)); // 01:00 on the 13th
            Assert.Equal(4, log.Append(Action("c")).Sequence);
        }
        var tomorrow = Lines(DayFile("2026-10-13"));
        Assert.Equal(new[] { "{\"v\":1,\"seq\":", "tornLine", "c" },
            new[] { tomorrow[0], Event(tomorrow[1]), JsonNode.Parse(tomorrow[2])!["result"]!.GetValue<string>() });
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Verified, result.Status);
        Assert.Equal(new AuditLineRef("2026-10-13.jsonl", 1, null), Assert.Single(result.Recovered));
    }

    // ---- 6. A failed write ----

    [Fact]
    public void A_write_that_fails_part_way_is_closed_and_recorded_before_the_next_line()
    {
        using (var log = Open())
        {
            log.Append(Action("a"));
            log.FailWriteForTests = sequence => sequence == 3 ? 20 : null;
            Assert.Throws<IOException>(() => log.AppendBatch([Action("b"), Action("c"), Action("d")]));
            log.FailWriteForTests = null;
            // b (2) reached the disk whole; c's first 20 bytes are closed off and named by a tornLine record (3).
            var receipt = log.Append(Action("e"));
            Assert.Equal(4, receipt.Sequence);
            Assert.Equal(4, log.HeadSequence);
        }
        var lines = Lines(Today);
        Assert.Equal(5, lines.Count);
        Assert.Equal(20, Encoding.UTF8.GetByteCount(lines[2]));
        Assert.Equal("tornLine", Event(lines[3]));
        var result = Verify();
        Assert.Equal(AuditVerificationStatus.Verified, result.Status);
        Assert.Equal(4, result.LinesVerified);
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 3, null), Assert.Single(result.Recovered));
    }

    [Fact]
    public void A_write_that_fails_before_any_byte_leaves_no_record_and_the_chain_goes_on()
    {
        using (var log = Open())
        {
            log.Append(Action("a"));
            log.FailWriteForTests = sequence => sequence == 2 ? 0 : null;
            Assert.Throws<IOException>(() => log.Append(Action("b")));
            log.FailWriteForTests = null;
            Assert.Equal(2, log.Append(Action("c")).Sequence);
        }
        Assert.Equal(2, Lines(Today).Count);
        Assert.Equal(AuditVerificationStatus.Verified, Verify().Status);
    }
}
