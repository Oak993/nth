using System.Text.Json;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-8; DESIGN.md 4.6). The audit log writes one JSON line per
// tool call under <data>\pc-control\<thread>\<yyyy-mm-dd>.jsonl with the time of each step; typed text appears as its
// length unless the user turned on "Log typed text"; a failed write never reaches the tool call.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcAuditLogTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_line_holds_the_call_its_rung_its_text_length_and_its_step_times_summed_by_name()
    {
        var entry = new PcAuditEntry(Noon, "pc_type", "notepad.exe", "w:1a", "[2]", "edit", "ok", 42)
        {
            Rung = "R2 em_replacesel",
            TextLength = 5,
            Steps = [new("gates", 1), new("act", 30), new("check", 4), new("act", 7)],
        };
        using var json = JsonDocument.Parse(PcAuditLog.Line(entry));
        var root = json.RootElement;
        Assert.Equal("pc_type", root.GetProperty("tool").GetString());
        Assert.Equal("notepad.exe", root.GetProperty("app").GetString());
        Assert.Equal("[2]", root.GetProperty("element").GetString());
        Assert.Equal("ok", root.GetProperty("result").GetString());
        Assert.Equal(42, root.GetProperty("ms").GetInt64());
        Assert.Equal("R2 em_replacesel", root.GetProperty("rung").GetString());
        Assert.Equal(5, root.GetProperty("text_length").GetInt32());
        Assert.False(root.TryGetProperty("text", out _));
        Assert.Equal(37, root.GetProperty("steps").GetProperty("act").GetInt64());
        Assert.Equal(1, root.GetProperty("steps").GetProperty("gates").GetInt64());
        Assert.StartsWith("2026-10-11T12:00:00", root.GetProperty("time").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain('\n', PcAuditLog.Line(entry));
    }

    [Fact]
    public void Lines_go_to_one_file_per_thread_and_day_and_a_thread_id_becomes_a_safe_folder_name()
    {
        using var folder = new PcTestFolder("pcaudit");
        var log = new PcAuditLog("chat/1:..\\x", () => folder.Path);
        log.Write(new PcAuditEntry(Noon, "pc_observe", "notepad.exe", "w:1a", null, null, "ok", 3));
        log.Write(new PcAuditEntry(Noon.AddMinutes(1), "pc_click", "notepad.exe", "w:1a", "[3]", "button", "ok", 9));
        log.Write(new PcAuditEntry(Noon.AddDays(1), "pc_click", "notepad.exe", "w:1a", "[3]", "button", "expect_failed", 9));
        var path = log.PathFor(Noon);
        Assert.Equal(Path.Combine(folder.Path, "chat_1____x", "2026-10-11.jsonl"), path);
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"tool\":\"pc_click\"", lines[1], StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(log.PathFor(Noon.AddDays(1))));
        Assert.EndsWith(Path.Combine("thread", "2026-10-11.jsonl"), new PcAuditLog("", () => folder.Path).PathFor(Noon), StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_that_fails_is_dropped_without_an_error()
    {
        using var folder = new PcTestFolder("pcaudit");
        // The data folder is a file: the log cannot make its folder.
        var blocked = Path.Combine(folder.Path, "blocked");
        File.WriteAllText(blocked, "");
        var log = new PcAuditLog("chat-1", () => blocked);
        log.Write(new PcAuditEntry(Noon, "pc_click", "a.exe", "w:1a", "[3]", "button", "ok", 1));
        Assert.False(Directory.Exists(Path.Combine(blocked, "chat-1")));
    }

    [Fact]
    public void Step_times_are_measured_on_the_engines_clock()
    {
        var clock = new PcFakeClock { NowMs = 1000 };
        var timings = new PcTimings(clock);
        clock.NowMs = 1004;
        timings.Mark("gates");
        clock.NowMs = 1030;
        timings.Mark("act");
        clock.NowMs = 1031;
        timings.Mark("act");
        Assert.Equal(4, timings.Of("gates"));
        Assert.Equal(27, timings.Of("act"));
        Assert.Equal(31, timings.Total);
        Assert.Equal(3, timings.Steps.Count);
    }
}
