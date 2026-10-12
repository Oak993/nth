using System.Text;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: the CSV export of the Activity view (DESIGN.md 12.5: "The log exports to JSON or CSV"; ED-NEW-dextra-as21-5).
// RFC 4180 with a byte order mark so a spreadsheet reads UTF-8; a text a spreadsheet would run as a formula (a path or a
// command an agent chose) is written as text. The expected file is written out whole from rows built by hand.
[Trait("Decision", "ED-NEW-dextra-as21-5")]
[Trait("Feature", "AS-21")]
public sealed class ActivityExportCsvTests
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);
    private static readonly string Zeros = new('0', 64);
    private static readonly string HmacA = new('a', 64);
    private static readonly string HmacB = new('b', 64);

    private static ActivityRow Action(int number, ActivityTrust trust, string? target, string? result) => new()
    {
        At = new AuditLineRef("2026-10-12.jsonl", number, number),
        Kind = ActivityRowKind.Action,
        Day = new DateOnly(2026, 10, 12),
        Time = new DateTimeOffset(2026, 10, 12, 9, 30, number - 1, TimeSpan.Zero),
        Assistant = "dextra",
        Tool = "shell",
        Class = "Run",
        Target = target,
        Result = result,
        Prev = number == 1 ? Zeros : HmacA,
        Hmac = number == 1 ? HmacA : HmacB,
        Trust = trust,
    };

    private static string Export(IEnumerable<ActivityRow> rows, out byte[] bytes)
    {
        using var output = new MemoryStream();
        ActivityExporter.Write(output, ActivityExportFormat.Csv, rows, new ActivityExportContext(new ActivityFilter(), null, ExportedAt));
        bytes = output.ToArray();
        return Encoding.UTF8.GetString(bytes);
    }

    [Fact]
    public void The_file_is_utf8_with_a_byte_order_mark_a_header_and_crlf_line_ends()
    {
        var text = Export([Action(1, ActivityTrust.Verified, "E:/repo", "ok")], out var bytes);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(21, ActivityExporter.CsvColumns.Count);
        Assert.Equal("\uFEFFseq,time,file,line,trust,kind,event,assistant,mission,agent,tool,class,target,inputs,result,costUsd,tokens,allowedBy,note,prev,hmac", text.Split("\r\n")[0]);
    }

    [Fact]
    public void An_empty_export_is_the_header_alone()
    {
        var text = Export([], out _);

        Assert.Equal("\uFEFF" + string.Join(',', ActivityExporter.CsvColumns) + "\r\n", text);
    }

    [Fact]
    public void Rows_are_written_whole_quoted_where_they_must_be_and_guarded_where_a_spreadsheet_would_run_them()
    {
        var full = Action(1, ActivityTrust.Verified, "E:/repo, main", "said \"ok\"") with
        {
            Mission = "m-1",
            Agent = "t-1",
            Inputs = [new ActivityInput("command", AuditInputKind.Text, "dotnet test"), new ActivityInput("body", AuditInputKind.Typed, TypedLength: 148), new ActivityInput("token", AuditInputKind.Secret)],
            CostUsd = 0.0125m,
            Tokens = 1840,
            AllowedBy = "grant:g-1",
        };
        var formulas = Action(2, ActivityTrust.NotVerified, "=HYPERLINK(\"http://x\")", "-1+2") with
        {
            Inputs = [new ActivityInput("cmd", AuditInputKind.Text, "@SUM(A1)")],
        };
        var unreadable = new ActivityRow
        {
            At = new AuditLineRef("2026-10-12.jsonl", 3, null),
            Kind = ActivityRowKind.Unreadable,
            Day = new DateOnly(2026, 10, 12),
            Note = "It does not parse. {bad}",
            Trust = ActivityTrust.NotVerified,
        };

        var text = Export([full, formulas, unreadable], out _);

        var header = string.Join(',', ActivityExporter.CsvColumns);
        var expected = "\uFEFF" + header + "\r\n"
            + "1,2026-10-12T09:30:00.000Z,2026-10-12.jsonl,1,verified,action,,dextra,m-1,t-1,shell,Run,\"E:/repo, main\","
                + "command=dotnet test; body=typed 148 characters; token=[secret],\"said \"\"ok\"\"\",0.0125,1840,grant:g-1,," + Zeros + "," + HmacA + "\r\n"
            + "2,2026-10-12T09:30:01.000Z,2026-10-12.jsonl,2,notVerified,action,,dextra,,,shell,Run,\"'=HYPERLINK(\"\"http://x\"\")\","
                + "cmd=@SUM(A1),'-1+2,,,,," + HmacA + "," + HmacB + "\r\n"
            + ",,2026-10-12.jsonl,3,notVerified,unreadable," + new string(',', 12) + "It does not parse. {bad},,\r\n";
        Assert.Equal(expected, text);
    }

    [Fact]
    public void A_record_the_log_wrote_about_itself_has_its_event_and_its_words()
    {
        var torn = new ActivityRow
        {
            At = new AuditLineRef("2026-10-12.jsonl", 4, 3),
            Kind = ActivityRowKind.Record,
            Day = new DateOnly(2026, 10, 12),
            Time = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero),
            Event = "tornLine",
            Assistant = "nth",
            Tool = "audit",
            Class = "Audit",
            Note = "A line a crash cut off. The log closed it and recorded its fingerprint (0123456789ab).",
            Prev = HmacA,
            Hmac = HmacB,
            Trust = ActivityTrust.Verified,
        };

        var line = Export([torn], out _).Split("\r\n")[1];

        Assert.Equal("3,2026-10-12T09:30:00.000Z,2026-10-12.jsonl,4,verified,record,tornLine,nth,,,audit,Audit,,,,,,,A line a crash cut off. The log closed it and recorded its fingerprint (0123456789ab)."
            + "," + HmacA + "," + HmacB, line);
    }

    [Theory]
    [InlineData("plain", true, "plain")]
    [InlineData("", true, "")]
    [InlineData("a,b", true, "\"a,b\"")]
    [InlineData("say \"hi\"", true, "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", true, "\"two\nlines\"")]
    [InlineData("two\r\nlines", true, "\"two\r\nlines\"")]
    [InlineData("=1+1", true, "'=1+1")]
    [InlineData("+1", true, "'+1")]
    [InlineData("-1", true, "'-1")]
    [InlineData("@sum(a1)", true, "'@sum(a1)")]
    [InlineData("\tx", true, "'\tx")]
    [InlineData("\rx", true, "\"'\rx\"")]
    [InlineData("\nx", true, "\"'\nx\"")]
    [InlineData("=a,b", true, "\"'=a,b\"")]
    [InlineData("a=1", true, "a=1")]
    [InlineData("-1", false, "-1")]
    [InlineData("0.0125", false, "0.0125")]
    public void A_cell_is_quoted_by_rfc_4180_and_a_formula_is_written_as_text(string value, bool guarded, string expected)
    {
        using var writer = new StringWriter();

        ActivityExporter.WriteCell(writer, value, guarded);

        Assert.Equal(expected, writer.ToString());
    }

    [Fact]
    public void A_log_of_awkward_text_round_trips_through_a_csv_reader()
    {
        using var rig = new ActivityLogRig();
        rig.Add(target: "C:\\Users\\Ömer\\New folder, copy", result: "line 1\nline 2, \"quoted\"", inputs: [AuditInput.Plain("script", "echo 1;\r\necho \"2\"")]);
        rig.Add(target: "=cmd|' /C calc'!A0", result: "+SUM(1,2)");
        var verification = AuditVerifier.Verify(rig.Directory, rig.Temp.Keys);

        var text = Export(ActivityLogReader.Stream(rig.Directory, verification), out _);

        var table = Parse(text.TrimStart('\uFEFF'));
        Assert.Equal(3, table.Count);
        Assert.Equal(ActivityExporter.CsvColumns, table[0]);
        Assert.All(table, row => Assert.Equal(21, row.Count));
        Assert.Equal("C:\\Users\\Ömer\\New folder, copy", table[1][12]);
        Assert.Equal("script=echo 1;\r\necho \"2\"", table[1][13]);
        Assert.Equal("line 1\nline 2, \"quoted\"", table[1][14]);
        Assert.Equal("'=cmd|' /C calc'!A0", table[2][12]);
        Assert.Equal("'+SUM(1,2)", table[2][14]);
        Assert.Equal("verified", table[1][4]);
        Assert.Equal("1", table[1][0]);
        Assert.Equal("2", table[2][0]);
    }

    [Fact]
    public void A_secret_a_line_holds_is_masked_again_in_every_text_column()
    {
        using var rig = new ActivityLogRig();
        rig.Add(result: "ok");
        rig.Close();
        var lines = rig.Lines("2026-10-12");
        lines[0] = lines[0].Replace("\"result\":\"ok\"", "\"result\":\"password=hunter2\"", StringComparison.Ordinal)
            .Replace("\"inputs\":[]", "\"inputs\":[{\"name\":\"h\",\"text\":\"Bearer abcdefghijklmnop\"}]", StringComparison.Ordinal);
        rig.Rewrite("2026-10-12", lines);

        var text = Export(ActivityLogReader.Stream(rig.Directory, null), out _);

        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop", text, StringComparison.Ordinal);
        Assert.Contains("h=Bearer [secret]", text, StringComparison.Ordinal);
        Assert.Contains("password=[secret]", text, StringComparison.Ordinal);
    }

    // A small RFC 4180 reader: quoted fields with doubled quotes and line breaks inside them.
    private static List<List<string>> Parse(string text)
    {
        var table = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                table.Add(row);
                row = [];
                i++;
            }
            else
            {
                cell.Append(c);
            }
        }
        return table;
    }
}
