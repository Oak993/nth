using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11e / DL-12. Fixture transcripts through each tap alone.
public sealed class SkillTapTests
{
    private static string[] Show(IEnumerable<ToolEvent> events) => events.Select(e => e.ToString()!).ToArray();

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Pi_transcript_gives_held_and_finished_for_bash_only()
    {
        var events = new List<ToolEvent>();
        var tap = new PiToolEventTap(events.Add);
        foreach (var line in SkillTapFixtures.Lines("pi.jsonl"))
        {
            switch (line["type"]!.JSONValueString)
            {
                case "extension_ui_request": tap.Held(JSONValue.Parse(line["title"]!.JSONValueString!["droppy-gate ".Length..])!); break;
                case "tool_execution_start": tap.ToolStarted(line); break;
                case "tool_execution_end": tap.ToolEnded(line); break;
            }
        }
        Assert.Equal<ToolEvent>([
            new CommandHeld("soffice --headless --convert-to pdf a.docx"),
            new CommandFinished("soffice --headless --convert-to pdf a.docx", 9009, true, "'soffice' is not recognized as an internal or external command"),
            new CommandFinished("python -c \"print(1)\"", 0, false, "1"),
        ], events);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Pi_ignores_a_non_command_gate_and_an_end_with_no_start()
    {
        var events = new List<ToolEvent>();
        var tap = new PiToolEventTap(events.Add);
        tap.Held(JSONValue.Parse("{\"kind\":\"fileChange\",\"input\":{\"path\":\"a\"}}")!);
        tap.ToolEnded(JSONValue.Parse("{\"toolCallId\":\"zz\",\"result\":{}}")!);
        Assert.Empty(events);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void CommandCode_transcript_gives_skill_used_and_command_results()
    {
        var events = new List<ToolEvent>();
        var tap = new CommandCodeToolEventTap(events.Add);
        foreach (var line in SkillTapFixtures.Lines("commandcode.jsonl"))
        {
            var e = line["event"]!;
            var id = e["toolCallId"]!.JSONValueString!;
            switch (e["type"]!.JSONValueString)
            {
                case "tool_queued": tap.Queued(id, e["toolName"]!.JSONValueString!, e["input"]!); break;
                case "tool_completed": tap.Completed(id, e); break;
                case "tool_errored": tap.Errored(id, e); break;
            }
        }
        Assert.Equal<ToolEvent>([
            new SkillUsed("pdf"),
            new CommandFinished("pdftoppm -png a.pdf out", null, true, "pdftoppm : The term 'pdftoppm' is not recognized as the name of a cmdlet"),
            new CommandFinished("node --version", null, false, "v22.1.0"),
        ], events);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void CommandCode_held_is_for_shell_tools_only()
    {
        var events = new List<ToolEvent>();
        var tap = new CommandCodeToolEventTap(events.Add);
        tap.Held("powershell", JSONValue.Parse("{\"command\":\"soffice a\"}")!);
        tap.Held("write_file", JSONValue.Parse("{\"command\":\"x\"}")!);
        Assert.Equal<ToolEvent>([new CommandHeld("soffice a")], events);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void Grok_acp_transcript_gives_held_and_finished_for_execute_calls_only()
    {
        var events = new List<ToolEvent>();
        var tap = new AcpToolEventTap(events.Add);
        foreach (var line in SkillTapFixtures.Lines("grok.jsonl"))
        {
            var p = line["params"]!;
            if (line["method"]!.JSONValueString == "session/request_permission") { tap.Held(p["toolCall"]!); continue; }
            var update = p["update"]!;
            if (update["sessionUpdate"]!.JSONValueString == "tool_call") tap.ToolCall(update); else tap.ToolCallUpdate(update);
        }
        Assert.Equal<ToolEvent>([
            new CommandHeld("pandoc a.md -o a.docx"),
            new CommandFinished("pandoc a.md -o a.docx", 127, true, "pandoc: command not found"),
            new CommandFinished("git status", 0, false, "clean"),
        ], events);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void A_tap_with_no_sink_does_nothing_and_throws_nothing()
    {
        var line = SkillTapFixtures.Lines("pi.jsonl")[1];
        new PiToolEventTap(null).ToolStarted(line);
        new CommandCodeToolEventTap(null).Held("powershell", JSONValue.Parse("{\"command\":\"x\"}")!);
        new AcpToolEventTap(null).ToolCall(SkillTapFixtures.Lines("grok.jsonl")[1]["params"]!["update"]!);
        Assert.NotNull(Show([]));
    }
}
