using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus's separate workspace, the wire between nth and its agent (unit P18, ED-NEW-pcws-5; LEAN item 10: a
// persisted or IPC format is versioned). Every message carries its version; unknown fields survive; a newer message is
// marked and never acted on; a line is capped; the token is compared in fixed time; a log line never carries a secret.
[Trait("Decision", "ED-NEW-pcws-5")]
public sealed class WorkspaceAgentProtocolTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Every_message_carries_the_version_and_type_and_requests_and_replies_their_ids()
    {
        var request = WorkspaceAgentProtocol.Request(WorkspaceAgentProtocol.Launch, 7);
        Assert.Equal("{\"v\":1,\"type\":\"launch\",\"id\":7}", request.ToJsonString());
        var reply = WorkspaceAgentProtocol.Parse(WorkspaceAgentProtocol.Reply(WorkspaceAgentProtocol.Launched, 7).ToJsonString())!;
        Assert.Equal((1, "launched", (long?)null, (long?)7, false), (reply.Version, reply.Type, reply.Id, reply.Re, reply.Newer));
    }

    [Fact]
    public void Unknown_fields_survive_and_a_newer_version_is_marked()
    {
        var line = "{\"v\":1,\"type\":\"windows\",\"re\":3,\"windows\":[],\"later\":{\"x\":1}}";
        var message = WorkspaceAgentProtocol.Parse(line)!;
        Assert.Equal(line, message.ToLine());
        var newer = WorkspaceAgentProtocol.Parse("{\"v\":2,\"type\":\"call\",\"id\":1}")!;
        Assert.True(newer.Newer);
        Assert.Null(WorkspaceAgentProtocol.Parse("{\"type\":\"call\"}"));
        Assert.Null(WorkspaceAgentProtocol.Parse("[1,2]"));
        Assert.Null(WorkspaceAgentProtocol.Parse("not json"));
        Assert.Null(WorkspaceAgentProtocol.Parse(""));
    }

    [Fact]
    public async Task A_line_longer_than_the_cap_closes_the_link_before_it_is_kept()
    {
        var (nth, agent) = WorkspaceDuplex.Pair();
        await agent.WriteAsync(Encoding.UTF8.GetBytes(new string('x', 300) + "\n"));
        var reader = new WorkspaceLineReader(nth, maxBytes: 200);
        await Assert.ThrowsAsync<WorkspaceProtocolException>(() => reader.ReadLine(CancellationToken.None));
        var writer = new WorkspaceLineWriter(agent);
        var huge = WorkspaceAgentProtocol.Message("call");
        huge["blob"] = new string('y', WorkspaceAgentProtocol.MaxLineBytes);
        await Assert.ThrowsAsync<WorkspaceProtocolException>(() => writer.Write(huge, CancellationToken.None));
        writer.Dispose();
    }

    [Fact]
    public async Task Lines_split_across_reads_and_two_in_one_read_come_back_whole()
    {
        var (nth, agent) = WorkspaceDuplex.Pair();
        await agent.WriteAsync(Encoding.UTF8.GetBytes("{\"v\":1,\"ty"));
        await agent.WriteAsync(Encoding.UTF8.GetBytes("pe\":\"a\"}\r\n{\"v\":1,\"type\":\"b\"}\n"));
        agent.Dispose();
        var reader = new WorkspaceLineReader(nth);
        Assert.Equal("{\"v\":1,\"type\":\"a\"}", await reader.ReadLine(CancellationToken.None));
        Assert.Equal("{\"v\":1,\"type\":\"b\"}", await reader.ReadLine(CancellationToken.None));
        Assert.Null(await reader.ReadLine(CancellationToken.None));
    }

    [Fact]
    public void The_token_must_match_exactly()
    {
        Assert.True(WorkspaceAgentProtocol.TokenMatches(Secret, Secret));
        Assert.False(WorkspaceAgentProtocol.TokenMatches(Secret, Secret[..^1] + "0"));
        Assert.False(WorkspaceAgentProtocol.TokenMatches(Secret, Secret[..10]));
        Assert.False(WorkspaceAgentProtocol.TokenMatches(Secret, null));
        Assert.False(WorkspaceAgentProtocol.TokenMatches("", ""));
    }

    [Fact]
    public void A_log_line_names_the_message_never_its_secrets()
    {
        var hello = WorkspaceAgentProtocol.Message(WorkspaceAgentProtocol.Hello);
        hello["token"] = Secret;
        var call = WorkspaceAgentProtocol.Request(WorkspaceAgentProtocol.Call, 4);
        call["thread"] = "chat-1";
        call["tool"] = "pc_type";
        call["args"] = new JsonObject { ["text"] = "my password is hunter2", ["window"] = "w:4000000000001a" };
        var window = WorkspaceAgentProtocol.Reply(WorkspaceAgentProtocol.WindowReply, 5);
        window["window"] = new JsonObject { ["title"] = "Bank statement - Secret", ["handle"] = 26 };
        var lines = new[] { hello, call, window }.Select(m => WorkspaceAgentProtocol.Describe(WorkspaceAgentProtocol.Parse(m.ToJsonString())!)).ToList();
        Assert.Equal("type=hello v=1", lines[0]);
        Assert.Equal("type=call v=1 id=4 tool=pc_type thread=chat-1", lines[1]);
        Assert.Equal("type=window v=1 re=5", lines[2]);
        Assert.DoesNotContain(lines, l => l.Contains(Secret, StringComparison.Ordinal) || l.Contains("hunter2", StringComparison.Ordinal) || l.Contains("Bank", StringComparison.Ordinal));
        var odd = WorkspaceAgentProtocol.Message("call\" token=" + Secret);
        Assert.DoesNotContain(" token=", WorkspaceAgentProtocol.Describe(WorkspaceAgentProtocol.Parse(odd.ToJsonString())!), StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_round_trip_and_a_window_without_its_elevation_counts_as_elevated()
    {
        var info = new PcWindowInfo(0x1a, "Untitled - Notepad", "Notepad", 101, "C:\\Apps\\notepad.exe") { Frame = new PcRect(1, 2, 300, 200), StartTime = 99, Aumid = "App!Main" };
        var back = WorkspaceAgentProtocol.ToWindow(WorkspaceAgentProtocol.ToJson(new WorkspaceWindow(info, false)))!;
        Assert.Equal(info, back.Info);
        Assert.False(back.Elevated);
        Assert.True(WorkspaceAgentProtocol.ToWindow(JsonNode.Parse("{\"handle\":26,\"exe\":\"C:\\\\a.exe\"}"))!.Elevated);
        Assert.Null(WorkspaceAgentProtocol.ToWindow(JsonNode.Parse("{\"handle\":" + WorkspaceRefs.Bit + "}")));
        Assert.Null(WorkspaceAgentProtocol.ToWindow(JsonNode.Parse("{\"title\":\"x\"}")));
    }

    [Fact]
    public void Results_round_trip_and_an_unknown_error_is_never_a_success()
    {
        var result = PcResult.Fail(PcErrorCode.NotApproved, "the user did not allow it") with { Resumed = new PcResumed(1200, false, 0) };
        var back = WorkspaceAgentProtocol.ToResult(WorkspaceAgentProtocol.ToJson(result));
        Assert.Equal(result.Render(), back.Render());
        var image = PcResult.Success(body: "looked") with { Image = new PcImage([1, 2, 3], 4, 5, "o:1099511627777") };
        Assert.Equal(new byte[] { 1, 2, 3 }, WorkspaceAgentProtocol.ToResult(WorkspaceAgentProtocol.ToJson(image)).Image!.Jpeg);
        var unknown = WorkspaceAgentProtocol.ToResult(JsonNode.Parse("{\"error\":\"brand_new\",\"status\":\"ok\"}"));
        Assert.Equal(PcErrorCode.NotSupported, unknown.Error);
        Assert.False(unknown.Ok);
        Assert.Equal(PcErrorCode.NotSupported, WorkspaceAgentProtocol.ToResult(null).Error);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-7")]
    public void The_agent_settings_keep_confirmations_unless_told_and_never_log_typed_text()
    {
        var sent = PcSettings.Default with { ConfirmGuardedActions = false, Detail = PcDetail.High, ReadTextFromImages = true, UserDeniedApps = ["C:\\Apps\\bank.exe"], StopChord = "Ctrl+Alt+F12", LogTypedText = true };
        var agent = WorkspaceAgentProtocol.ToSettings(WorkspaceAgentProtocol.ToJson(sent));
        Assert.Equal((true, true, false, false, PcDetail.High, true, "Ctrl+Alt+F12"), (agent.Enabled, agent.RealInput, agent.LogTypedText, agent.ConfirmGuardedActions, agent.Detail, agent.ReadTextFromImages, agent.StopChord));
        Assert.Equal(new[] { "C:\\Apps\\bank.exe" }, agent.UserDeniedApps);
        Assert.True(WorkspaceAgentProtocol.ToSettings(null).ConfirmGuardedActions);
        Assert.True(WorkspaceAgentProtocol.ToSettings(JsonNode.Parse("{}")).ConfirmGuardedActions);
        Assert.Equal(PcApproval.Denied, WorkspaceAgentProtocol.ToApproval("maybe"));
        Assert.Equal(PcApproval.Always, WorkspaceAgentProtocol.ToApproval(WorkspaceAgentProtocol.Wire(PcApproval.Always)));
        Assert.Equal(PcCapability.See, WorkspaceAgentProtocol.ToCapability("anything"));
    }
}
