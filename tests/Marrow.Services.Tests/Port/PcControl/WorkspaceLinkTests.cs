using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus's separate workspace, nth's link and its agent over an in-memory pipe (unit P18, ED-NEW-pcws-5,
// ED-NEW-pcws-8). The agent runs its own engine on the fake desktop; nth's end launches, lists, runs steps and answers the
// agent's asks with the chat's; the hello's token and version are checked; a stopped step tells the agent to cancel; a
// broken pipe fails what waits; neither end ever logs the token.
[Trait("Decision", "ED-NEW-pcws-5")]
public sealed class WorkspaceLinkTests
{
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Wrong = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string Inside = "w:400000000000001a";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static Action<string> Into(List<string> log) => line =>
    {
        lock (log) log.Add(line);
    };

    private static WorkspaceCall Look(string thread = "chat-1") =>
        new(thread, PcToolSchemas.Observe, (JsonObject)JsonNode.Parse("{\"window\":\"" + Inside + "\",\"screenshot\":\"no\"}")!);

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-8")]
    public async Task The_agent_launches_lists_and_runs_steps_with_the_chats_asks_until_nth_says_bye()
    {
        using var rig = new PcAssemblyRig();
        var settings = new WorkspaceAgentSettings();
        using var assembly = WorkspaceAgent.Assembly(WorkspaceAgent.Backends(rig.Backends, "C:\\Profiles"), settings);
        var agentLog = new List<string>();
        var nthLog = new List<string>();
        var agent = new WorkspaceAgent(assembly, settings, Into(agentLog), new PcManualClock());
        rig.Desktop.OnLaunch = _ => rig.Desktop.AddApp(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var (nth, inside) = WorkspaceDuplex.Pair();
        var serving = agent.Run(inside, Token, CancellationToken.None);
        var link = await WorkspaceLink.Accept(nth, Token, new PcManualClock(), Into(nthLog), CancellationToken.None);
        Assert.NotNull(link);

        var launched = await link.Launch("C:\\Apps\\notepad.exe", CancellationToken.None);
        Assert.True(launched.Started, launched.Problem);
        Assert.Equal((nint)0x1a, launched.Windows.Single().Info.Handle);
        Assert.False(launched.Windows[0].Elevated);
        Assert.Single(await link.Windows(CancellationToken.None));
        Assert.Equal("Untitled - Notepad", (await link.Window(0x1a, CancellationToken.None))!.Info.Title);
        Assert.Null(await link.Window(0x99, CancellationToken.None));

        var asks = new FakeWorkspaceAsks();
        var looked = await link.Call(Look(), asks, CancellationToken.None);
        Assert.True(looked.Ok, looked.Render());
        Assert.True(WorkspaceRefs.IsWorkspaceObservation(looked.Window!.Obs));
        Assert.Equal(new[] { "approve notepad.exe see" }, asks.Asked);
        asks.Approval = new WorkspaceApproval(PcApproval.Denied, "not now");
        var refused = await link.Call(new WorkspaceCall("chat-2", PcToolSchemas.Observe, (JsonObject)JsonNode.Parse("{\"window\":\"" + Inside + "\"}")!), asks, CancellationToken.None);
        Assert.Equal(PcErrorCode.NotApproved, refused.Error);
        Assert.Contains("not now", refused.Reason, StringComparison.Ordinal);

        await link.DisposeAsync();
        Assert.Equal(WorkspaceAgent.ExitDone, await serving.WaitAsync(Patience));
        await agent.DisposeAsync();
        Assert.DoesNotContain(nthLog.Concat(agentLog), line => line.Contains(Token, StringComparison.Ordinal));
        Assert.Contains("workspace.hello.ok", nthLog);
    }

    [Fact]
    public async Task A_wrong_token_a_newer_agent_or_a_silent_one_is_refused_and_no_log_carries_a_token()
    {
        using var rig = new PcAssemblyRig();
        var settings = new WorkspaceAgentSettings();
        using var assembly = WorkspaceAgent.Assembly(WorkspaceAgent.Backends(rig.Backends, "C:\\Profiles"), settings);
        var agentLog = new List<string>();
        var nthLog = new List<string>();
        var agent = new WorkspaceAgent(assembly, settings, Into(agentLog), new PcManualClock());
        var (nth, inside) = WorkspaceDuplex.Pair();
        var serving = agent.Run(inside, Wrong, CancellationToken.None);
        Assert.Null(await WorkspaceLink.Accept(nth, Token, new PcManualClock(), Into(nthLog), CancellationToken.None));
        Assert.Equal(WorkspaceAgent.ExitRefused, await serving.WaitAsync(Patience));
        Assert.Contains("workspace.hello.refused", nthLog);

        var (nth2, raw) = WorkspaceDuplex.Pair();
        var newer = WorkspaceAgentProtocol.Message(WorkspaceAgentProtocol.Hello);
        newer["v"] = WorkspaceAgentProtocol.Version + 1;
        newer["token"] = Token;
        var writer = new WorkspaceLineWriter(raw);
        await writer.Write(newer, CancellationToken.None);
        Assert.Null(await WorkspaceLink.Accept(nth2, Token, new PcManualClock(), Into(nthLog), CancellationToken.None));
        var answer = WorkspaceAgentProtocol.Parse(await new WorkspaceLineReader(raw).ReadLine(CancellationToken.None))!;
        Assert.Equal((WorkspaceAgentProtocol.Failed, "version"), (answer.Type, answer.Str("reason")));
        writer.Dispose();

        var (nth3, _) = WorkspaceDuplex.Pair();
        var clock = new PcManualClock();
        var accepting = WorkspaceLink.Accept(nth3, Token, clock, Into(nthLog), CancellationToken.None);
        await PcTestWait.Until(() => clock.Pending > 0);
        clock.Advance(WorkspaceLink.HelloTimeoutMs);
        Assert.Null(await accepting.WaitAsync(Patience));
        Assert.Contains("workspace.hello.timeout", nthLog);

        await agent.DisposeAsync();
        Assert.DoesNotContain(nthLog.Concat(agentLog), line => line.Contains(Token, StringComparison.Ordinal) || line.Contains(Wrong, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stopped_step_tells_the_agent_to_cancel_and_a_broken_pipe_fails_what_waits()
    {
        using var rig = new PcAssemblyRig();
        var settings = new WorkspaceAgentSettings();
        using var assembly = WorkspaceAgent.Assembly(WorkspaceAgent.Backends(rig.Backends, "C:\\Profiles"), settings);
        var agent = new WorkspaceAgent(assembly, settings, null, new PcManualClock());
        rig.Desktop.AddApp(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var (nth, inside) = WorkspaceDuplex.Pair();
        var serving = agent.Run(inside, Token, CancellationToken.None);
        var link = (await WorkspaceLink.Accept(nth, Token, new PcManualClock(), _ => { }, CancellationToken.None))!;
        var closed = false;
        link.Closed += () => closed = true;

        var asks = new FakeWorkspaceAsks { Hold = new TaskCompletionSource<WorkspaceApproval>() };
        using var stop = new CancellationTokenSource();
        var step = link.Call(Look(), asks, stop.Token);
        await PcTestWait.Until(() => asks.Asked.Count == 1);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step.WaitAsync(Patience));
        await PcTestWait.Until(() => nth.Written.Any(line => line.Contains("\"type\":\"cancel\"", StringComparison.Ordinal)));

        var waiting = link.Call(Look("chat-2"), asks, CancellationToken.None);
        await PcTestWait.Until(() => asks.Asked.Count == 2);
        inside.Break();
        var broken = await waiting.WaitAsync(Patience);
        Assert.Equal(PcErrorCode.WindowGone, broken.Error);
        Assert.True(closed);
        Assert.False(link.IsOpen);
        var gone = await link.Launch("x.exe", CancellationToken.None);
        Assert.Equal((false, "the separate workspace closed"), (gone.Started, gone.Problem));
        await serving.WaitAsync(Patience);
        await link.DisposeAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task The_agent_answers_what_it_knows_and_leaves_newer_and_unknown_messages_alone()
    {
        using var rig = new PcAssemblyRig();
        var settings = new WorkspaceAgentSettings();
        using var assembly = WorkspaceAgent.Assembly(WorkspaceAgent.Backends(rig.Backends, "C:\\Profiles"), settings);
        var agentLog = new List<string>();
        var agent = new WorkspaceAgent(assembly, settings, Into(agentLog), new PcManualClock());
        var (raw, inside) = WorkspaceDuplex.Pair();
        var serving = agent.Run(inside, Token, CancellationToken.None);
        var reader = new WorkspaceLineReader(raw);
        var writer = new WorkspaceLineWriter(raw);
        var hello = WorkspaceAgentProtocol.Parse(await reader.ReadLine(CancellationToken.None))!;
        Assert.Equal((WorkspaceAgentProtocol.Hello, WorkspaceAgentProtocol.Version, Token), (hello.Type, hello.Version, hello.Str("token")));
        Assert.Equal((long?)Environment.ProcessId, hello.Long("pid"));
        await writer.Write(WorkspaceAgentProtocol.Message(WorkspaceAgentProtocol.Welcome), CancellationToken.None);
        var newer = WorkspaceAgentProtocol.Request(WorkspaceAgentProtocol.Launch, 1);
        newer["v"] = WorkspaceAgentProtocol.Version + 1;
        newer["target"] = "C:\\Apps\\notepad.exe";
        await writer.Write(newer, CancellationToken.None);
        await writer.Write(WorkspaceAgentProtocol.Request("mystery", 2), CancellationToken.None);
        var windows = WorkspaceAgentProtocol.Request(WorkspaceAgentProtocol.WindowsAsk, 3);
        windows["later"] = true;
        await writer.Write(windows, CancellationToken.None);
        var reply = WorkspaceAgentProtocol.Parse(await reader.ReadLine(CancellationToken.None).WaitAsync(Patience))!;
        Assert.Equal((WorkspaceAgentProtocol.WindowsReply, (long?)3), (reply.Type, reply.Re));
        await writer.Write(WorkspaceAgentProtocol.Message(WorkspaceAgentProtocol.Bye), CancellationToken.None);
        Assert.Equal(WorkspaceAgent.ExitDone, await serving.WaitAsync(Patience));
        Assert.Contains("workspace.protocol.newer type=launch v=2 id=1", agentLog);
        Assert.Contains("workspace.protocol.unexpected type=other v=1 id=2", agentLog);
        Assert.Empty(rig.Desktop.Apps);
        writer.Dispose();
        await agent.DisposeAsync();
    }
}
