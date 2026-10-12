using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the Activity view reads the audit log (DESIGN.md 12.5, unit AS-21): every field of a line comes back as the
// writer kept it, in chain order, with each row's day taken from its day file; a line that is not a sealed audit line is a
// row, never dropped; a piece being written is not a line yet; hostile text is shown as escapes; a long log lists its newest
// lines; and the reader never holds the writer's files.
[Trait("Decision", "ED-NEW-dextra-as21-1")]
[Trait("Decision", "ED-NEW-dextra-as21-2")]
[Trait("Feature", "AS-21")]
public sealed class ActivityLogReaderTests : IDisposable
{
    private readonly ActivityLogRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public void Every_field_of_a_line_is_read_back_as_the_writer_kept_it()
    {
        rig.Add("dextra", "m-1", "thread-7", "shell", "Run", "E:/work/repo", "ok 1", "grant:g-12",
            [AuditInput.Plain("command", "dotnet test"), AuditInput.Typed("body", new string('x', 148)), AuditInput.Secret("token"), AuditInput.Numeric("attempt", 2), AuditInput.Boolean("dry", false)],
            0.0125m, 1840);

        var snapshot = ActivityLogReader.Load(rig.Source);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 1, 1), row.At);
        Assert.Equal(1, row.Sequence);
        Assert.Equal(ActivityRowKind.Action, row.Kind);
        Assert.Equal(new DateOnly(2026, 10, 12), row.Day);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero), row.Time);
        Assert.Null(row.Event);
        Assert.Equal("dextra", row.Assistant);
        Assert.Equal("m-1", row.Mission);
        Assert.Equal("thread-7", row.Agent);
        Assert.Equal("shell", row.Tool);
        Assert.Equal("Run", row.Class);
        Assert.Equal("E:/work/repo", row.Target);
        Assert.Equal("ok 1", row.Result);
        Assert.Equal("grant:g-12", row.AllowedBy);
        Assert.Equal(0.0125m, row.CostUsd);
        Assert.Equal(1840L, row.Tokens);
        Assert.Equal(["command", "body", "token", "attempt", "dry"], row.Inputs.Select(i => i.Name));
        Assert.Equal([AuditInputKind.Text, AuditInputKind.Typed, AuditInputKind.Secret, AuditInputKind.Number, AuditInputKind.Flag], row.Inputs.Select(i => i.Kind));
        Assert.Equal(["dotnet test", "typed 148 characters", "[secret]", "2", "no"], row.Inputs.Select(i => i.Display));
        Assert.Equal(148, row.Inputs[1].TypedLength);
        Assert.Equal(new string('0', 64), row.Prev);
        Assert.Matches("^[0-9a-f]{64}$", row.Hmac);
        Assert.Equal(ActivityTrust.Verified, row.Trust);
        Assert.Equal(AuditVerificationStatus.Verified, snapshot.Verification.Status);
        Assert.Equal(1, snapshot.Verification.LinesVerified);
        Assert.Equal(1, snapshot.TotalLines);
        Assert.Equal(0, snapshot.NotListed);
    }

    [Fact]
    public void A_typed_text_of_one_character_reads_in_the_singular()
    {
        rig.Add(inputs: [AuditInput.Typed("key", "a")]);
        Assert.Equal("typed 1 character", ActivityLogReader.Load(rig.Source).Rows[0].Inputs[0].Display);
    }

    [Fact]
    public void Rows_come_in_chain_order_across_day_files_and_a_rows_day_is_its_files_day()
    {
        using var data = ActivityDataset.Make();

        var snapshot = ActivityLogReader.Load(data.Source);

        Assert.Equal([1L, 2, 3, 4, 5, 6], snapshot.Rows.Select(r => r.Sequence));
        Assert.Equal(["2026-10-11.jsonl", "2026-10-11.jsonl", "2026-10-12.jsonl", "2026-10-12.jsonl", "2026-10-12.jsonl", "2026-10-12.jsonl"], snapshot.Rows.Select(r => r.At.File));
        Assert.Equal([1, 2, 1, 2, 3, 4], snapshot.Rows.Select(r => r.At.Line));
        Assert.Equal([new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)],
            snapshot.Rows.Select(r => r.Day));
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 0, 30, TimeSpan.Zero), snapshot.Rows[2].Time);
        Assert.All(snapshot.Rows, r => Assert.Equal(ActivityTrust.Verified, r.Trust));
    }

    [Fact]
    public void Facets_list_every_value_a_filter_can_pick_with_its_count()
    {
        using var data = ActivityDataset.Make();

        var facets = ActivityLogReader.Load(data.Source).Facets;

        Assert.Equal([("astur-shop", 2), ("dextra", 4)], facets.Assistants.Select(f => (f.Value, f.Count)));
        Assert.Equal([("m-3", 1), ("m-2", 2), ("m-1", 2)], facets.Missions.Select(f => (f.Value, f.Count))); // the mission that acted last first
        Assert.Equal([("t-1", 2), ("t-3", 2), ("t-2", 1)], facets.Agents.Select(f => (f.Value, f.Count))); // t-1 acted last (line 6), then t-3 (line 4), then t-2 (line 2)
        Assert.Equal([("Agents", 1), ("Outward", 2), ("Read web", 1), ("Run", 2)], facets.Classes.Select(f => (f.Value, f.Count)));
        Assert.Equal([("grant:g-1", 2), ("decision:d-7", 2)], facets.Grants.Select(f => (f.Value, f.Count)));
        Assert.Equal([(new DateOnly(2026, 10, 12), 4), (new DateOnly(2026, 10, 11), 2)], facets.Days.Select(d => (d.Day, d.Count)));
        Assert.Equal(1, facets.NoMission);
        Assert.Equal(2, facets.NoGrant);
    }

    [Fact]
    public void A_log_that_does_not_exist_lists_nothing_and_says_so()
    {
        var snapshot = ActivityLogReader.Load(rig.Source with { Directory = Path.Combine(rig.Temp.Folder, "no-such-folder") });

        Assert.Empty(snapshot.Rows);
        Assert.Equal(0, snapshot.TotalLines);
        Assert.Equal(AuditVerificationStatus.Empty, snapshot.Verification.Status);
        Assert.Equal("No activity yet", snapshot.Verification.Summary);
        Assert.Empty(snapshot.Facets.Assistants);
        Assert.Empty(snapshot.Facets.Days);
    }

    [Fact]
    public void A_piece_after_the_last_newline_is_a_line_being_written_and_is_not_a_row()
    {
        rig.Add(result: "one");
        rig.Add(result: "two");
        rig.Close();
        File.AppendAllText(rig.DayFile("2026-10-12"), "{\"v\":1,\"seq\":3,\"time\":\"2026-10-12T09:30:00.000Z\",\"assi");

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal(2, snapshot.TotalLines);
        Assert.Equal(AuditVerificationStatus.Verified, snapshot.Verification.Status);
    }

    [Fact]
    public void A_line_that_is_not_a_sealed_audit_line_is_a_row_and_is_the_first_failure()
    {
        rig.Add(result: "ok 1");
        rig.Add(result: "ok 2");
        rig.Add(result: "ok 3");
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines[1] = "{\"hello\":1}";
        rig.Rewrite("2026-10-12", lines);

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal(AuditVerificationStatus.Failed, snapshot.Verification.Status);
        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 2, null), snapshot.Verification.FirstFailure);
        var failing = snapshot.FirstFailureRow;
        Assert.NotNull(failing);
        Assert.Equal(ActivityRowKind.Unreadable, failing.Kind);
        Assert.Equal(2, failing.At.Line);
        Assert.StartsWith("It is not a sealed audit line. {\"hello\":1}", failing.Note, StringComparison.Ordinal);
        Assert.Equal([ActivityTrust.Verified, ActivityTrust.NotVerified, ActivityTrust.NotVerified], snapshot.Rows.Select(r => r.Trust));
    }

    [Fact]
    public void An_edited_line_shows_the_edited_text_and_the_chain_marks_it_and_every_line_after_it()
    {
        for (var i = 1; i <= 4; i++) rig.Add(result: "ok " + i);
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines[2] = lines[2].Replace("\"ok 3\"", "\"no 3\"", StringComparison.Ordinal);
        rig.Rewrite("2026-10-12", lines);

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal("Line 3 of 2026-10-12.jsonl fails: its HMAC does not match its contents (it was edited, or sealed without the key).", snapshot.Verification.Summary);
        Assert.Equal("no 3", snapshot.FirstFailureRow!.Result);
        Assert.Equal(3, snapshot.FirstFailureRow.Sequence);
        Assert.Equal([ActivityTrust.Verified, ActivityTrust.Verified, ActivityTrust.NotVerified, ActivityTrust.NotVerified], snapshot.Rows.Select(r => r.Trust));
    }

    [Fact]
    public void A_failure_in_a_later_day_leaves_the_earlier_days_verified()
    {
        rig.Add(result: "day one");
        rig.Advance(TimeSpan.FromDays(1));
        rig.Add(result: "day two a");
        rig.Add(result: "day two b");
        rig.Close();
        var lines = rig.Lines("2026-10-13");
        lines[1] = lines[1].Replace("day two b", "day two x", StringComparison.Ordinal);
        rig.Rewrite("2026-10-13", lines);

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal(new AuditLineRef("2026-10-13.jsonl", 2, 3), snapshot.Verification.FirstFailure);
        Assert.Equal([ActivityTrust.Verified, ActivityTrust.Verified, ActivityTrust.NotVerified], snapshot.Rows.Select(r => r.Trust));
    }

    [Fact]
    public void A_log_whose_end_is_missing_marks_every_row_not_verified_as_no_line_can_be_named()
    {
        for (var i = 1; i <= 3; i++) rig.Add(result: "ok " + i);
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines.RemoveAt(2);
        rig.Rewrite("2026-10-12", lines);

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal(AuditVerificationStatus.Failed, snapshot.Verification.Status);
        Assert.Null(snapshot.Verification.FirstFailure);
        Assert.Contains("lines after 2 are missing", snapshot.Verification.Summary, StringComparison.Ordinal);
        Assert.Null(snapshot.FirstFailureRow);
        Assert.All(snapshot.Rows, r => Assert.Equal(ActivityTrust.NotVerified, r.Trust));
    }

    [Fact]
    public void A_day_that_no_longer_ends_with_the_hmac_the_digest_printed_fails_at_that_day()
    {
        rig.Add(result: "one");
        rig.Advance(TimeSpan.FromDays(1));
        rig.Add(result: "two");
        var printed = new Dictionary<DateOnly, string> { [new DateOnly(2026, 10, 12)] = new string('a', 64) };

        var snapshot = ActivityLogReader.Load(rig.Source with { PrintedDayFinals = () => printed });

        Assert.Equal(new AuditLineRef("2026-10-12.jsonl", 0, null), snapshot.Verification.FirstFailure);
        Assert.Null(snapshot.FirstFailureRow); // a day, not a line: there is no row to jump to
        Assert.Equal([ActivityTrust.NotVerified, ActivityTrust.NotVerified], snapshot.Rows.Select(r => r.Trust));
    }

    [Fact]
    public void A_missing_key_leaves_every_row_unchecked_and_says_why()
    {
        rig.Add(result: "one");
        rig.Close();
        rig.Temp.Keys.DeleteAll();

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal(AuditVerificationStatus.KeyMissing, snapshot.Verification.Status);
        Assert.Equal("Not verified: the log's key is missing", snapshot.Verification.Summary);
        Assert.Equal(ActivityTrust.Unchecked, Assert.Single(snapshot.Rows).Trust);
    }

    [Fact]
    public void A_key_that_credential_manager_refuses_leaves_every_row_unchecked()
    {
        rig.Add(result: "one");
        rig.Close();
        rig.Temp.RefuseRead = _ => 1312;

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal(AuditVerificationStatus.KeyUnavailable, snapshot.Verification.Status);
        Assert.Equal(ActivityTrust.Unchecked, Assert.Single(snapshot.Rows).Trust);
    }

    [Fact]
    public void Lines_written_after_the_check_read_the_files_are_unchecked_not_verified()
    {
        rig.Add(result: "one");
        rig.Add(result: "two");
        var verification = AuditVerifier.Verify(rig.Directory, rig.Temp.Keys);
        rig.Add(result: "three");

        var rows = ActivityLogReader.Stream(rig.Directory, verification).ToList();

        Assert.Equal([ActivityTrust.Verified, ActivityTrust.Verified, ActivityTrust.Unchecked], rows.Select(r => r.Trust));
    }

    [Fact]
    public void Rows_streamed_without_a_check_are_all_unchecked()
    {
        rig.Add(result: "one");
        Assert.Equal(ActivityTrust.Unchecked, Assert.Single(ActivityLogReader.Stream(rig.Directory, null)).Trust);
    }

    [Fact]
    public void A_line_a_crash_cut_off_is_a_row_the_log_repaired_and_its_record_is_a_row_too()
    {
        rig.Add(result: "one");
        rig.Add(result: "two");
        rig.Close();
        File.AppendAllText(rig.DayFile("2026-10-12"), "{\"v\":1,\"seq\":3,\"time\":\"2026-10-12T09:30:00.000Z\",\"assi");
        rig.Add(result: "three"); // reopens the writer, which closes the piece and records it in the chain

        var snapshot = ActivityLogReader.Load(rig.Source);

        Assert.Equal(AuditVerificationStatus.Verified, snapshot.Verification.Status);
        Assert.Equal([ActivityRowKind.Action, ActivityRowKind.Action, ActivityRowKind.Unreadable, ActivityRowKind.Record, ActivityRowKind.Action], snapshot.Rows.Select(r => r.Kind));
        var cut = snapshot.Rows[2];
        Assert.Equal(ActivityTrust.Unchecked, cut.Trust); // not a link of the chain
        Assert.StartsWith("A line a crash cut off. The log closed it and recorded its fingerprint, so it is not part of the chain.", cut.Note, StringComparison.Ordinal);
        var record = snapshot.Rows[3];
        Assert.Equal("tornLine", record.Event);
        Assert.Equal("nth", record.Assistant);
        Assert.Equal(ActivityTrust.Verified, record.Trust);
        Assert.Matches(@"^A line a crash cut off\. The log closed it and recorded its fingerprint \([0-9a-f]{12}\)\.$", record.Note);
        Assert.Equal([ActivityTrust.Verified, ActivityTrust.Verified, ActivityTrust.Unchecked, ActivityTrust.Verified, ActivityTrust.Verified], snapshot.Rows.Select(r => r.Trust));
    }

    [Fact]
    public void A_cut_log_is_a_record_row_and_the_check_fails_at_it()
    {
        for (var i = 1; i <= 3; i++) rig.Add(result: "ok " + i);
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines.RemoveAt(2); // the anchor in Credential Manager still names line 3
        rig.Rewrite("2026-10-12", lines);
        rig.Add(result: "after the cut"); // the writer records what it found

        var snapshot = ActivityLogReader.Load(rig.Source);

        var record = Assert.Single(snapshot.Rows, r => r.Kind == ActivityRowKind.Record);
        Assert.Equal("logCut", record.Event);
        Assert.Equal("The log was cut: line 3 had been written, but the files ended at line 2.", record.Note);
        Assert.Equal(AuditVerificationStatus.Failed, snapshot.Verification.Status);
        Assert.Equal(record.At.Line, snapshot.Verification.FirstFailure!.Line);
        Assert.Same(record, snapshot.FirstFailureRow);
    }

    [Fact]
    public void A_line_over_the_bound_is_a_row_that_was_not_read()
    {
        rig.Add(result: "one");
        rig.Close();
        File.AppendAllText(rig.DayFile("2026-10-12"), new string('a', ActivityLogReader.MaxLineBytes + 200_000) + "\n");
        File.AppendAllText(rig.DayFile("2026-10-12"), "short\n");

        var rows = ActivityLogReader.Stream(rig.Directory, null).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(ActivityRowKind.Unreadable, rows[1].Kind);
        Assert.Equal("The line is longer than 1 MiB, so it was not read.", rows[1].Note);
        Assert.Equal("It is not a sealed audit line. short", rows[2].Note);
    }

    [Fact]
    public void An_unreadable_line_is_masked_and_cut_short_in_its_note()
    {
        rig.Add(result: "one");
        rig.Close();
        File.AppendAllText(rig.DayFile("2026-10-12"), "someone wrote password=hunter2 here " + new string('z', 600) + "\n");

        var note = ActivityLogReader.Load(rig.Source).Rows[1].Note!;

        Assert.DoesNotContain("hunter2", note, StringComparison.Ordinal);
        Assert.Contains("[secret]", note, StringComparison.Ordinal);
        Assert.EndsWith("…", note, StringComparison.Ordinal);
        Assert.True(note.Length < 400, "the note is " + note.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters");
    }

    [Fact]
    public void A_line_with_the_wrong_types_in_its_fields_is_read_without_failing()
    {
        rig.Add(result: "one");
        rig.Close();
        var good = rig.Lines("2026-10-12")[0];
        // Fields of the wrong type (as a planted line may carry) read as absent, never as an error.
        var planted = good.Replace("\"assistant\":\"dextra\"", "\"assistant\":5", StringComparison.Ordinal)
            .Replace("\"tool\":\"shell\"", "\"tool\":{\"a\":1}", StringComparison.Ordinal)
            .Replace("\"inputs\":[]", "\"inputs\":[1,{\"name\":2},{\"name\":\"n\",\"text\":3},{\"name\":\"ok\",\"flag\":true}]", StringComparison.Ordinal);
        rig.Rewrite("2026-10-12", [planted]);

        var row = Assert.Single(ActivityLogReader.Load(rig.Source).Rows);

        Assert.Equal(ActivityRowKind.Action, row.Kind);
        Assert.Equal("", row.Assistant);
        Assert.Equal("", row.Tool);
        Assert.Equal(["ok"], row.Inputs.Select(i => i.Name));
        Assert.True(row.Inputs[0].Flag);
    }

    [Fact]
    public void A_property_named_twice_is_a_damaged_line_not_an_error()
    {
        rig.Add(result: "one");
        rig.Close();
        var good = rig.Lines("2026-10-12")[0];
        var doubled = good.Replace("\"tool\":\"shell\"", "\"tool\":\"shell\",\"tool\":\"other\"", StringComparison.Ordinal);
        rig.Rewrite("2026-10-12", [doubled]);

        var row = Assert.Single(ActivityLogReader.Load(rig.Source).Rows);

        Assert.Equal(ActivityRowKind.Unreadable, row.Kind);
        Assert.StartsWith("It does not parse.", row.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_that_could_mislead_is_shown_as_escapes_and_ordinary_text_is_kept()
    {
        rig.Add(target: "evil\u202Efdp.exe", result: "bell\u0007 and zero\u200Bwidth", inputs: [AuditInput.Plain("script", "line 1\nline 2\tend"), AuditInput.Plain("note", "café 😀 日本")]);

        var row = ActivityLogReader.Load(rig.Source).Rows[0];

        Assert.Equal("evil\\u202Efdp.exe", row.Target);
        Assert.Equal("bell\\u0007 and zero\\u200Bwidth", row.Result);
        Assert.Equal("line 1\nline 2\tend", row.Inputs[0].Text); // a newline and a tab are kept for the detail panel
        Assert.Equal("café 😀 日本", row.Inputs[1].Text);
    }

    [Fact]
    public void A_long_log_lists_its_newest_rows_and_counts_the_rest()
    {
        for (var i = 1; i <= 5; i++) rig.Add(result: "ok " + i);

        var snapshot = ActivityLogReader.Load(rig.Source, maxRows: 3);

        Assert.Equal([3L, 4, 5], snapshot.Rows.Select(r => r.Sequence));
        Assert.Equal(5, snapshot.TotalLines);
        Assert.Equal(2, snapshot.NotListed);
        Assert.Equal(AuditVerificationStatus.Verified, snapshot.Verification.Status);
        Assert.Equal(5, snapshot.Verification.LinesVerified); // the check covered the whole log
    }

    [Fact]
    public void Reading_never_holds_the_writers_files_and_a_cancelled_read_stops()
    {
        rig.Add(result: "one");
        Assert.Single(ActivityLogReader.Load(rig.Source).Rows);
        rig.Add(result: "two"); // the writer is not blocked by the reader
        Assert.Equal(2, ActivityLogReader.Load(rig.Source).Rows.Count);

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => ActivityLogReader.Load(rig.Source, cancel.Token));
        Assert.Throws<OperationCanceledException>(() => ActivityLogReader.Stream(rig.Directory, null, cancel.Token).ToList());
    }

    [Fact]
    public void Utf8_bytes_of_other_languages_survive_the_round_trip_through_the_files()
    {
        rig.Add(result: "Ошибка: файл не найден", target: "C:\\Users\\Ömer\\Belgeler");

        var row = ActivityLogReader.Load(rig.Source).Rows[0];

        Assert.Equal("Ошибка: файл не найден", row.Result);
        Assert.Equal("C:\\Users\\Ömer\\Belgeler", row.Target);
    }
}
