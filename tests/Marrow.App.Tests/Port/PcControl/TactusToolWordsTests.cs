using Marrow.App.Port.PcControl;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the timeline's words for nth-pc calls (OD-0140, ED-NEW-pcctl-28; DESIGN.md 4.6, unit 14). An MCP call
// ("nth-pc · pc_click", kind Mcp) and an API call ("Tactus · Click", kind Other) read as one sentence from the tool, its
// arguments and the result: "Clicked Save in Notepad", "Typed 42 characters into Name Box in Excel"; running, failed,
// declined, paused, waiting and unverified calls say so; any other call keeps upstream's words and its MCP badge.
[Trait("Decision", "ED-NEW-pcctl-28")]
public sealed class TactusToolWordsTests
{
    private const string NotepadWindow = "window: \"Untitled - Notepad\" (notepad.exe) obs: o:4 size: 1280x720 scale: 1";
    private const string ExcelWindow = "window: \"Book1 - Excel\" (EXCEL.EXE) obs: o:9";

    private static ToolCall Mcp(string tool, string detail, string output = "", ToolCall.Status status = ToolCall.Status.Completed) =>
        new() { ToolCallKind = ToolCall.Kind.Mcp, Title = "nth-pc \u00B7 " + tool, Detail = detail, Output = output, ToolCallStatus = status };

    private static ToolCall Api(string title, string detail, string output = "", ToolCall.Status status = ToolCall.Status.Completed) =>
        new() { ToolCallKind = ToolCall.Kind.Other, Title = "Tactus \u00B7 " + title, Detail = detail, Output = output, ToolCallStatus = status };

    [Fact]
    public void A_click_reads_the_element_and_the_app_from_the_result_in_both_providers_rows()
    {
        var output = "ok\n" + NotepadWindow + "\ndone: clicked button \"Save\"\nrung: R1 (invoke)";
        var mcp = Mcp("pc_click", "{\"window\":\"w:3a0f12\",\"element\":12}", output);
        var api = Api("Click", "{\n  \"window\": \"w:3a0f12\",\n  \"element\": 12\n}", output);
        Assert.Equal("pc_click", TactusToolWords.ToolOf(mcp));
        Assert.Equal("pc_click", TactusToolWords.ToolOf(api));
        Assert.Equal("Clicked Save in Notepad", TactusToolWords.Label(mcp));
        Assert.Equal("Clicked Save in Notepad", TactusToolWords.Label(api));
        // the timeline's own words and badge take them
        Assert.Equal("Clicked Save in Notepad", ToolPresentation.Label(mcp));
        Assert.Equal("Clicked Save in Notepad", ToolPresentation.Label(api));
        Assert.Null(ToolPresentation.McpParts(mcp));
    }

    [Fact]
    public void Each_state_of_a_call_says_what_happened()
    {
        var target = "{\"window\":\"w:1\",\"target\":{\"name\":\"Save\",\"role\":\"button\"}}";
        Assert.Equal("Clicking Save", TactusToolWords.Label(Mcp("pc_click", target, status: ToolCall.Status.Running)));
        Assert.Equal("Could not click Save", TactusToolWords.Label(Mcp("pc_click", target, "error: not_approved: the user did not allow this.", ToolCall.Status.Failed)));
        Assert.Equal("Could not click Save in Notepad", TactusToolWords.Label(Mcp("pc_click", target, "error: expect_failed: the check did not hold.\n" + NotepadWindow)));
        Assert.Equal("Not allowed to click Save", TactusToolWords.Label(Mcp("pc_click", target, status: ToolCall.Status.Declined)));
        Assert.Equal("Paused: type 3 characters in Excel", TactusToolWords.Label(Api("Type", "{\"window\":\"w:2\",\"text\":\"abc\"}", "waiting_for_user: paused. token: t1. The user paused this app.\n" + ExcelWindow)));
        Assert.Equal("Waiting for you: click Save", TactusToolWords.Label(Mcp("pc_click", target, "waiting_for_user: approval. token: t2. Do other steps.")));
        Assert.Equal("Clicked Save in Notepad (not confirmed)", TactusToolWords.Label(Mcp("pc_click", target, "unverified: no change was seen. Call pc_observe.\n" + NotepadWindow)));
    }

    [Fact]
    public void Every_tool_has_its_own_sentence()
    {
        var text = new string('x', 42);
        Assert.Equal("Typed 42 characters into Name Box in Excel", TactusToolWords.Label(Api("Type", "{\"window\":\"w:2\",\"text\":\"" + text + "\"}", "ok\n" + ExcelWindow + "\ndone: typed into edit \"Name Box\"")));
        Assert.Equal("Typed 1 character in Notepad", TactusToolWords.Label(Mcp("pc_type", "{\"window\":\"w:1\",\"text\":\"\u00E9\"}", "ok\n" + NotepadWindow)));
        Assert.Equal("Listed apps", TactusToolWords.Label(Mcp("pc_list_apps", "{}", "ok")));
        Assert.Equal("Opened Notepad", TactusToolWords.Label(Mcp("pc_launch_app", "{\"path\":\"c:\\\\windows\\\\notepad.exe\"}", "ok")));
        Assert.Equal("Looked at Excel", TactusToolWords.Label(Api("Look at window", "{\"window\":\"w:2\"}", "ok\n" + ExcelWindow)));
        Assert.Equal("Set Total in Excel", TactusToolWords.Label(Mcp("pc_set_value", "{\"window\":\"w:2\",\"element\":4,\"value\":\"9\"}", "ok\n" + ExcelWindow + "\ndone: set the value of edit \"Total\"")));
        Assert.Equal("Pressed Ctrl+S in Notepad", TactusToolWords.Label(Mcp("pc_key", "{\"window\":\"w:1\",\"keys\":\"Ctrl+S\"}", "ok\n" + NotepadWindow)));
        Assert.Equal("Pressed Tab 3 times in Notepad", TactusToolWords.Label(Mcp("pc_key", "{\"window\":\"w:1\",\"keys\":\"Tab\",\"repeat\":3}", "ok\n" + NotepadWindow)));
        Assert.Equal("Scrolled down in Excel", TactusToolWords.Label(Mcp("pc_scroll", "{\"window\":\"w:2\",\"direction\":\"down\"}", "ok\n" + ExcelWindow)));
        Assert.Equal("Dragged in Excel", TactusToolWords.Label(Mcp("pc_drag", "{\"window\":\"w:2\",\"obs\":\"o:9\"}", "ok\n" + ExcelWindow)));
        Assert.Equal("Expanded Folders in Notepad", TactusToolWords.Label(Mcp("pc_action", "{\"window\":\"w:1\",\"element\":3,\"action\":\"expand\"}", "ok\n" + NotepadWindow + "\ndone: expanded tree item \"Folders\"")));
        Assert.Equal("Minimized Notepad", TactusToolWords.Label(Mcp("pc_action", "{\"window\":\"w:1\",\"action\":\"minimize\"}", "ok\n" + NotepadWindow)));
        Assert.Equal("Showed Excel", TactusToolWords.Label(Api("Show window", "{\"window\":\"w:2\"}", "ok\n" + ExcelWindow)));
        Assert.Equal("Ran 3 steps in Excel", TactusToolWords.Label(Api("Run steps", "{\"window\":\"w:2\",\"actions\":[{},{},{}]}", "ok\n" + ExcelWindow)));
        Assert.Equal("Read text in Notepad", TactusToolWords.Label(Mcp("pc_read", "{\"window\":\"w:1\"}", "ok\n" + NotepadWindow)));
        Assert.Equal("Zoomed in on a screenshot", TactusToolWords.Label(Mcp("pc_zoom", "{\"obs\":\"o:4\",\"region\":[0,0,10,10]}", "ok")));
        Assert.Equal("Waited for Notepad", TactusToolWords.Label(Mcp("pc_wait", "{\"window\":\"w:1\",\"until\":\"idle\"}", "ok\n" + NotepadWindow)));
        Assert.Equal("Asked you: Which sheet?", TactusToolWords.Label(Mcp("pc_ask", "{\"question\":\"Which sheet?\",\"choices\":[\"Q1\",\"Q2\"]}", "answer: Q1")));
        // the title's own app name wins over the exe's when it holds it
        Assert.Equal("Looked at Google Chrome", TactusToolWords.Label(Mcp("pc_observe", "{}", "ok\nwindow: \"Inbox - Google Chrome\" (chrome.exe)")));
        Assert.Equal("Looked at Winword", TactusToolWords.Label(Mcp("pc_observe", "{}", "ok\nwindow: \"Report.docx\" (winword.exe)")));
        // arguments that are not JSON leave the words to the result
        Assert.Equal("Clicked in Notepad", TactusToolWords.Label(Mcp("pc_click", "not json", "ok\n" + NotepadWindow)));
    }

    [Fact]
    public void Any_other_call_keeps_upstreams_words()
    {
        var github = new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = "github \u00B7 create_issue", Detail = "{}" };
        var skill = new ToolCall { ToolCallKind = ToolCall.Kind.Other, Title = "Skill \u00B7 review" };
        var wrongKind = new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = "Tactus \u00B7 Click" };
        var unknownTool = new ToolCall { ToolCallKind = ToolCall.Kind.Mcp, Title = "nth-pc \u00B7 pc_unknown" };
        foreach (var call in new[] { github, skill, wrongKind, unknownTool })
        {
            Assert.Null(TactusToolWords.ToolOf(call));
            Assert.Null(TactusToolWords.Label(call));
        }
        Assert.Equal("Called github \u00B7 create_issue", ToolPresentation.Label(github with { ToolCallStatus = ToolCall.Status.Completed }));
        Assert.Equal("Used Skill \u00B7 review", ToolPresentation.Label(skill with { ToolCallStatus = ToolCall.Status.Completed }));
    }
}
