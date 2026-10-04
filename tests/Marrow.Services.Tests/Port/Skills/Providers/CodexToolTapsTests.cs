// PORT-ONLY: SK-11b / DL-12 Codex taps. Normalized events come from a committed fixture transcript synthesized from the app-server formats the port already reads (CodexSession item and approval handling); no live CLI.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class CodexToolTapsTests
{
    private static IEnumerable<CodexTranscriptLine> Transcript(string chatFolder) => File.ReadLines(CodexWiringFixture.FixturePath("codex-app-server-skills.jsonl")).Where(l => l.Length > 0).Select(raw =>
    {
        var json = CodexWiringFixture.Json(raw.Replace("{{CHAT}}", chatFolder.Replace('\\', '/'), StringComparison.Ordinal));
        return new CodexTranscriptLine(json["kind"]!.JSONValueString!, json["id"]?.JSONValueInt is { } id ? (int)id : null, json["method"]!.JSONValueString!, json["params"]!);
    });

    private static void Play(CodexSession session, IEnumerable<CodexTranscriptLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.Kind == "request") CodexWiringFixture.Call(session, "HandleRequest", new RPCID.Int(line.Id!.Value), line.Method, line.Params);
            else CodexWiringFixture.Call(session, "HandleNotification", line.Method, line.Params);
        }
    }

    private const string CommandsFixture = "soffice --headless --convert-to pdf report.docx";

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T12")]
    [Trait("Rule", "R-TEST-10")]
    public void The_transcript_yields_exactly_the_normalized_events_in_order()
    {
        using var f = new GenerationFixture();
        var pdf = f.Add("A1", "pdf");
        using var chat = f.Open().Start("codex", "chat-a", [pdf], f.Switches((pdf, true)), f.Budget);
        var session = CodexWiringFixture.Session(new CodexSkillWiring(chat, f.Paths));
        var events = new List<ToolEvent>();
        session.OnToolEvent = events.Add;
        Play(session, Transcript(chat.Folder));
        Assert.Equal<ToolEvent>(
        [
            new SkillUsed("pdf", "A1"),
            new SkillUsed("mine", null),
            new CommandHeld(CommandsFixture),
            new CommandHeld("python3 -c \"import docx\""),
            new CommandFinished(CommandsFixture, 9009, true, "'soffice' is not recognized as an internal or external command,\r\noperable program or batch file."),
            new CommandFinished("python3 -c \"import docx\"", 1, true, "ModuleNotFoundError: No module named 'docx'"),
            new CommandFinished("echo ok", 0, false, "ok\n"),
            new CommandFinished("python3 slow.py", null, true, ""),
        ], events);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T12")]
    [Trait("Rule", "R-TEST-10")]
    public void Without_wiring_the_same_command_events_arrive_and_a_skill_has_no_item_id()
    {
        var session = CodexWiringFixture.Session();
        var events = new List<ToolEvent>();
        session.OnToolEvent = events.Add;
        Play(session, Transcript("/nowhere"));
        Assert.Equal(new SkillUsed("pdf", null), events[0]);
        Assert.Equal(8, events.Count);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T12")]
    [Trait("Rule", "R-TEST-10")]
    public void Taps_change_nothing_else_the_provider_events_are_the_same_with_and_without_a_listener()
    {
        static List<string> Run(bool listening)
        {
            var session = CodexWiringFixture.Session();
            var seen = new List<string>();
            session.OnEvent = e => seen.Add(e.GetType().Name + ":" + e switch
            {
                ProviderEvent.Approval a => a.Value.Id + "|" + a.Value.Title + "|" + string.Join(",", a.Value.Options.Select(o => o.Id)),
                ProviderEvent.ToolStarted s => s.ToString(),
                ProviderEvent.ToolUpdated u => u.ToString(),
                _ => "",
            });
            if (listening) session.OnToolEvent = _ => { };
            Play(session, Transcript("/nowhere"));
            return seen;
        }
        var without = Run(false);
        Assert.NotEmpty(without);
        Assert.Equal(without, Run(true));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T12")]
    [Trait("Rule", "R-TEST-10")]
    public void A_listener_that_throws_never_breaks_the_session()
    {
        var session = CodexWiringFixture.Session();
        var approvals = 0;
        session.OnEvent = e => { if (e is ProviderEvent.Approval) approvals++; };
        session.OnToolEvent = _ => throw new InvalidOperationException("a bad listener");
        Play(session, Transcript("/nowhere"));
        Assert.Equal(4, approvals); // three command approvals (one without a command text) plus the file change
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T12")]
    [Trait("Rule", "R-TEST-10")]
    public void A_command_approval_with_no_command_text_taps_nothing_and_other_requests_are_not_commands()
    {
        Assert.Null(CodexToolTaps.Request("item/commandExecution/requestApproval", CodexWiringFixture.Json("{}")));
        Assert.Null(CodexToolTaps.Request("item/commandExecution/requestApproval", CodexWiringFixture.Json("{\"command\":\"  \"}")));
        Assert.Null(CodexToolTaps.Request("item/fileChange/requestApproval", CodexWiringFixture.Json("{\"command\":\"ls\"}")));
        Assert.Null(CodexToolTaps.Request("item/tool/requestUserInput", CodexWiringFixture.Json("{\"command\":\"ls\"}")));
        Assert.Equal(new CommandHeld("ls"), CodexToolTaps.Request("item/commandExecution/requestApproval", CodexWiringFixture.Json("{\"command\":\"ls\"}")));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T12")]
    [Trait("Rule", "R-TEST-10")]
    public void Items_that_are_not_commands_or_skill_inputs_tap_nothing()
    {
        Assert.Empty(CodexToolTaps.ItemCompleted(CodexWiringFixture.Json("{\"type\":\"agentMessage\",\"id\":\"m\",\"text\":\"x\"}")));
        Assert.Empty(CodexToolTaps.ItemCompleted(CodexWiringFixture.Json("{\"type\":\"commandExecution\",\"id\":\"c\"}")));
        Assert.Empty(CodexToolTaps.ItemStarted(CodexWiringFixture.Json("{\"type\":\"commandExecution\",\"id\":\"c\",\"command\":\"ls\"}"), null));
        Assert.Empty(CodexToolTaps.ItemStarted(CodexWiringFixture.Json("{\"type\":\"userMessage\",\"id\":\"u\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"},{\"type\":\"skill\"},{\"type\":\"skill\",\"name\":5}]}"), null));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T12")]
    [Trait("Rule", "R-TEST-10")]
    public void The_events_are_the_types_the_missing_tool_detector_reads()
    {
        var finished = Assert.Single(CodexToolTaps.ItemCompleted(CodexWiringFixture.Json(
            "{\"type\":\"commandExecution\",\"id\":\"c\",\"command\":\"soffice a.docx\",\"status\":\"failed\",\"exitCode\":127,\"aggregatedOutput\":\"bash: soffice: command not found\"}")));
        Assert.IsType<CommandFinished>(finished);
        Assert.IsAssignableFrom<ToolEvent>(finished);
    }
}
