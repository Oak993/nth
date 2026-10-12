using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the JSON export of the Activity view (DESIGN.md 12.5: "The log exports to JSON or CSV"; ED-NEW-dextra-as21-5).
// The file says what chose its rows and whether the chain verified, and each line carries its numbers, its HMAC and whether
// the chain vouched for it. The expected values are written out here from ActivityDataset's six actions.
[Trait("Decision", "ED-NEW-dextra-as21-5")]
[Trait("Feature", "AS-21")]
public sealed class ActivityExportJsonTests : IDisposable
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);
    private readonly ActivityLogRig data = ActivityDataset.Make();

    public void Dispose() => data.Dispose();

    private static JsonObject Export(IEnumerable<ActivityRow> rows, ActivityFilter filter, AuditVerification? verification, out byte[] bytes)
    {
        using var output = new MemoryStream();
        ActivityExporter.Write(output, ActivityExportFormat.Json, rows, new ActivityExportContext(filter, verification, ExportedAt));
        bytes = output.ToArray();
        return JsonNode.Parse(Encoding.UTF8.GetString(bytes))!.AsObject();
    }

    private (List<ActivityRow> Rows, AuditVerification Verification) Read(ActivityLogRig rig)
    {
        var verification = AuditVerifier.Verify(rig.Directory, rig.Temp.Keys);
        return (ActivityLogReader.Stream(rig.Directory, verification).ToList(), verification);
    }

    [Fact]
    public void The_file_names_its_format_its_filter_and_the_chain_check_and_counts_its_lines()
    {
        var (rows, verification) = Read(data);

        var json = Export(rows, new ActivityFilter(), verification, out var bytes);

        Assert.Equal((byte)'{', bytes[0]); // no byte order mark
        Assert.Equal("nth-activity-log", json["format"]!.GetValue<string>());
        Assert.Equal(1, json["version"]!.GetValue<int>());
        Assert.Equal("2026-10-13T07:00:00.000Z", json["exportedAt"]!.GetValue<string>());
        Assert.Empty(json["filter"]!.AsObject());
        var check = json["verification"]!.AsObject();
        Assert.Equal("Verified", check["status"]!.GetValue<string>());
        Assert.Equal("Verified", check["summary"]!.GetValue<string>());
        Assert.Equal(6, check["linesVerified"]!.GetValue<int>());
        Assert.False(check.ContainsKey("firstFailure"));
        Assert.False(check.ContainsKey("reason"));
        Assert.Equal(6, json["lines"]!.AsArray().Count);
        Assert.Equal(6, json["count"]!.GetValue<int>());
    }

    [Fact]
    public void A_line_carries_every_field_the_log_holds_and_nothing_it_does_not()
    {
        var (rows, verification) = Read(data);

        var lines = Export(rows, new ActivityFilter(), verification, out _)["lines"]!.AsArray();

        var first = lines[0]!.AsObject();
        Assert.Equal(1, first["seq"]!.GetValue<long>());
        Assert.Equal("2026-10-11T20:00:00.000Z", first["time"]!.GetValue<string>());
        Assert.Equal("2026-10-11.jsonl", first["file"]!.GetValue<string>());
        Assert.Equal(1, first["line"]!.GetValue<int>());
        Assert.Equal("verified", first["trust"]!.GetValue<string>());
        Assert.Equal("action", first["kind"]!.GetValue<string>());
        Assert.Equal("dextra", first["assistant"]!.GetValue<string>());
        Assert.Equal("m-1", first["mission"]!.GetValue<string>());
        Assert.Equal("t-1", first["agent"]!.GetValue<string>());
        Assert.Equal("shell", first["tool"]!.GetValue<string>());
        Assert.Equal("Run", first["class"]!.GetValue<string>());
        Assert.Equal("E:/shop", first["target"]!.GetValue<string>());
        Assert.Equal("command", first["inputs"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("dotnet build", first["inputs"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("build ok", first["result"]!.GetValue<string>());
        Assert.Equal(0.01m, first["costUsd"]!.GetValue<decimal>());
        Assert.Equal(100, first["tokens"]!.GetValue<long>());
        Assert.Equal("grant:g-1", first["allowedBy"]!.GetValue<string>());
        Assert.Equal(new string('0', 64), first["prev"]!.GetValue<string>());
        Assert.Equal(rows[0].Hmac, first["hmac"]!.GetValue<string>());
        Assert.False(first.ContainsKey("inputsNotLogged"));
        Assert.False(first.ContainsKey("note"));
        Assert.False(first.ContainsKey("event"));

        var typed = lines[2]!["inputs"]!.AsArray();
        Assert.Equal(148, typed[0]!["typedLength"]!.GetValue<int>());
        Assert.False(typed[0]!.AsObject().ContainsKey("text"));
        Assert.Equal("[secret]", typed[1]!["secret"]!.GetValue<string>());

        var bare = lines[4]!.AsObject(); // line 5: no mission, agent, target or grant, so none of those keys
        Assert.Equal(["seq", "time", "file", "line", "trust", "kind", "assistant", "tool", "class", "inputs", "result", "prev", "hmac"], bare.Select(p => p.Key));
        Assert.Empty(bare["inputs"]!.AsArray());
        Assert.Equal(rows[3].Hmac, bare["prev"]!.GetValue<string>());
    }

    [Fact]
    public void Numbers_and_flags_stay_numbers_and_flags()
    {
        using var rig = new ActivityLogRig();
        rig.Add(inputs: [AuditInput.Numeric("attempt", 2.5), AuditInput.Boolean("dry", true), AuditInput.Numeric("ratio", double.NaN)]);
        var (rows, verification) = Read(rig);

        var inputs = Export(rows, new ActivityFilter(), verification, out _)["lines"]![0]!["inputs"]!.AsArray();

        Assert.Equal(2.5, inputs[0]!["number"]!.GetValue<double>());
        Assert.True(inputs[1]!["flag"]!.GetValue<bool>());
        Assert.Equal("NaN", inputs[2]!["number"]!.GetValue<string>());
    }

    [Fact]
    public void A_filtered_export_says_what_filter_chose_its_rows()
    {
        var (rows, verification) = Read(data);
        var filter = new ActivityFilter { Mission = "m-2", Class = "Outward", Grant = ActivityFilter.None, From = new DateOnly(2026, 10, 12), To = new DateOnly(2026, 10, 12), Text = " sent " };

        var json = Export(rows.Where(filter.Matches), filter, verification, out _);

        var described = json["filter"]!.AsObject();
        Assert.Equal(["mission", "class", "grant", "from", "to", "text"], described.Select(p => p.Key));
        Assert.Equal("m-2", described["mission"]!.GetValue<string>());
        Assert.Equal("", described["grant"]!.GetValue<string>());
        Assert.Equal("2026-10-12", described["from"]!.GetValue<string>());
        Assert.Equal("sent", described["text"]!.GetValue<string>());
        Assert.Empty(json["lines"]!.AsArray()); // no row passes all of that: the file is honest about it
        Assert.Equal(0, json["count"]!.GetValue<int>());
    }

    [Fact]
    public void An_export_of_a_log_that_fails_says_where_and_marks_each_line_by_what_the_check_covered()
    {
        for (var i = 1; i <= 3; i++) data.Add(result: "extra " + i);
        data.Close();
        var lines = data.Lines("2026-10-12");
        lines[5] = lines[5].Replace("extra 2", "extra X", StringComparison.Ordinal); // the file's sixth line is the second extra
        data.Rewrite("2026-10-12", lines);
        var (rows, verification) = Read(data);

        var json = Export(rows, new ActivityFilter(), verification, out _);

        var check = json["verification"]!.AsObject();
        Assert.Equal("Failed", check["status"]!.GetValue<string>());
        Assert.Equal("Line 6 of 2026-10-12.jsonl fails: its HMAC does not match its contents (it was edited, or sealed without the key).", check["summary"]!.GetValue<string>());
        Assert.Equal("its HMAC does not match its contents (it was edited, or sealed without the key).", check["reason"]!.GetValue<string>());
        Assert.Equal(7, check["linesVerified"]!.GetValue<int>()); // the seven lines before the edited one
        var failure = check["firstFailure"]!.AsObject();
        Assert.Equal("2026-10-12.jsonl", failure["file"]!.GetValue<string>());
        Assert.Equal(6, failure["line"]!.GetValue<int>());
        Assert.Equal(8, failure["seq"]!.GetValue<int>());
        var trust = json["lines"]!.AsArray().Select(l => l!["trust"]!.GetValue<string>()).ToArray();
        Assert.Equal(["verified", "verified", "verified", "verified", "verified", "verified", "verified", "notVerified", "notVerified"], trust);
    }

    [Fact]
    public void A_secret_a_line_holds_is_masked_again_on_the_way_out()
    {
        data.Add(result: "ok");
        data.Close();
        var lines = data.Lines("2026-10-12");
        // An older build's line, or one planted in the folder: unmasked text the writer would have masked.
        lines[^1] = lines[^1].Replace("\"result\":\"ok\"", "\"result\":\"login with password=hunter2 and Bearer abcdefghijklmnop\"", StringComparison.Ordinal);
        data.Rewrite("2026-10-12", lines);
        var (rows, verification) = Read(data);

        var json = Export(rows, new ActivityFilter(), verification, out var bytes);

        Assert.DoesNotContain("hunter2", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal("login with password=[secret] and Bearer [secret]", json["lines"]!.AsArray().Last()!["result"]!.GetValue<string>());
    }

    [Fact]
    public void Text_the_log_wrote_masked_comes_out_unchanged()
    {
        using var rig = new ActivityLogRig();
        rig.Add(target: "https://example.test/a?token=abc123&x=1", result: "key sk-abcdefghijklmnopqrstuvwxyz was used; password=hunter2",
            inputs: [AuditInput.Plain("header", "Authorization: Bearer abcdefghijklmnop"), AuditInput.Plain("note", "café — 日本 \u2192 ok")]);
        var (rows, verification) = Read(rig);

        var line = Export(rows, new ActivityFilter(), verification, out _)["lines"]![0]!.AsObject();

        Assert.Equal(rows[0].Target, line["target"]!.GetValue<string>());
        Assert.Equal(rows[0].Result, line["result"]!.GetValue<string>());
        Assert.Equal(rows[0].Inputs[0].Text, line["inputs"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("café — 日本 → ok", line["inputs"]![1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void A_line_that_cannot_be_read_is_exported_as_what_it_is()
    {
        data.Close();
        File.AppendAllText(data.DayFile("2026-10-12"), "this line is not sealed\n");
        var (rows, verification) = Read(data);

        var line = Export(rows, new ActivityFilter(), verification, out _)["lines"]!.AsArray().Last()!.AsObject();

        Assert.Equal("unreadable", line["kind"]!.GetValue<string>());
        Assert.Equal("notVerified", line["trust"]!.GetValue<string>());
        Assert.Equal(5, line["line"]!.GetValue<int>()); // the seventh line of the log is the fifth of that day's file
        Assert.Equal("It is not a sealed audit line. this line is not sealed", line["note"]!.GetValue<string>());
        Assert.False(line.ContainsKey("seq"));
        Assert.False(line.ContainsKey("time"));
        Assert.False(line.ContainsKey("assistant"));
        Assert.False(line.ContainsKey("hmac"));
    }

    [Fact]
    public void An_export_without_a_check_says_it_was_not_checked()
    {
        var rows = ActivityLogReader.Stream(data.Directory, null).ToList();

        var json = Export(rows, new ActivityFilter(), null, out _);

        Assert.Equal("NotChecked", json["verification"]!["status"]!.GetValue<string>());
        Assert.Equal("unchecked", json["lines"]![0]!["trust"]!.GetValue<string>());
    }
}
