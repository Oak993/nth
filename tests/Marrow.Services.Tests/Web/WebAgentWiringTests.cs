using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W11, how the web tools sit inside Tactus and the providers (DESIGN.md 5.10 "Stop is the Tactus chord
// ... it ends every web step at once", "Pause: the model's queue holds", "User takes over ... the pill says 'You took over
// Visus' with Resume", 2.7 "a crash during a running model step fails the step and never re-sends a guarded action", 13.2
// "Failure paths with the fake"): the Tactus pause and its turn, a crash in the middle of a step, the provider approvals that
// nth's own cards govern, and screenshots for clients that cannot carry images.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebAgentWiringTests
{
    private static string Tab(string tab = "t1", string extra = "") => "{\"tab\":\"" + tab + "\"" + extra + "}";

    private static string Ref(string reference, string extra = "", string tab = "t1") => Tab(tab, ",\"ref\":\"" + reference + "\"" + extra);

    private static async Task<FakeAgentTab> ReadInvite(WebAgentRig rig)
    {
        var tab = await rig.InviteTab();
        Assert.Null((await rig.Call(WebToolSchemas.Read, Tab())).Error);
        return tab;
    }

    [Fact]
    public async Task ACrashWhileAStepRunsFailsItAndNothingIsSentAgain()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        var hold = new TaskCompletionSource<bool>();
        tab.Page.Hold = ("Input.dispatchMouseEvent", hold.Task);

        var click = rig.Start(WebToolSchemas.Click, Ref("e9"));
        await PcTestWait.Until(() => tab.Page.Count("Input.dispatchMouseEvent") == 1);
        tab.RaiseStepFailed(WebSurface.StepFailedCrashed);
        var result = await click;
        hold.SetResult(true);
        var next = await rig.Call(WebToolSchemas.Tabs);

        Assert.Equal(WebErrorCodes.PageFailed, result.Error);
        Assert.Contains("page crashed", result.Text, StringComparison.Ordinal);
        Assert.Contains("Nothing was sent again", result.Text, StringComparison.Ordinal);
        Assert.Equal(1, tab.Page.Count("Input.dispatchMouseEvent"));
        Assert.Null(next.Error);
        Assert.False(rig.Engine.StoppedForTurn);
    }

    [Fact]
    public async Task ACrashOnATabNoStepUsesChangesNothing()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        tab.RaiseStepFailed(WebSurface.StepFailedCrashed);
        var read = await rig.Call(WebToolSchemas.Read, Tab());

        Assert.Null(read.Error);
        Assert.Equal(0, tab.OpenSteps);
    }

    [Fact]
    public async Task EveryCallHoldsItsTabSoItIsNeverSuspendedMeanwhile()
    {
        using var rig = new WebAgentRig();
        var tab = await rig.InviteTab();
        var hold = new TaskCompletionSource<bool>();
        tab.Page.Hold = ("Accessibility.getFullAXTree", hold.Task);

        var read = rig.Start(WebToolSchemas.Read, Tab());
        await PcTestWait.Until(() => tab.Page.Count("Accessibility.getFullAXTree") == 1);
        var during = tab.OpenSteps;
        hold.SetResult(true);
        await read;

        Assert.Equal(1, during);
        Assert.Equal(0, tab.OpenSteps);
    }

    [Fact]
    public async Task WithTactusOnThePillsPauseOfVisusHoldsTheStepsAndItsResumeLetsThemGo()
    {
        using var rig = new WebAgentRig(tactusPause: true);
        await rig.InviteTab();
        var pause = rig.Pause!;

        pause.Pause(PcScope.App(WebAgentHub.App), PcPauseSource.Row);
        var call = rig.Start(WebToolSchemas.Read, Tab());
        Assert.True(await PcTestWait.StillWaiting(call));
        Assert.True(rig.Hub.IsPaused);
        pause.Resume(PcScope.App(WebAgentHub.App), PcPauseSource.Row);

        Assert.Null((await call).Error);
        Assert.False(rig.Hub.IsPaused);
    }

    [Fact]
    public async Task WithTactusOnAResumeOfVisusHandsBackTheTabTheUserTookOver()
    {
        using var rig = new WebAgentRig(tactusPause: true);
        var tab = await ReadInvite(rig);
        await rig.Call(WebToolSchemas.Click, Ref("e9"));
        tab.RaiseUserInput();
        Assert.True(rig.Hub.AnyTookOver);

        rig.Pause!.Pause(PcScope.App(WebAgentHub.App), PcPauseSource.Pill);
        rig.Pause.Resume(PcScope.App(WebAgentHub.App), PcPauseSource.Pill);

        Assert.False(rig.Hub.AnyTookOver);
        Assert.Null((await rig.Call(WebToolSchemas.Read, Tab())).Error);
    }

    [Fact]
    public async Task TheHubsHandBackEndsATakeOverWithoutAPause()
    {
        using var rig = new WebAgentRig(tactusPause: true);
        var tab = await ReadInvite(rig);
        await rig.Call(WebToolSchemas.Click, Ref("e9"));
        tab.RaiseUserInput();

        rig.Hub.HandBack();

        Assert.False(rig.Hub.AnyTookOver);
    }

    [Fact]
    public async Task VisusIsTheChatsAppInThePillUntilTheTurnEnds()
    {
        using var rig = new WebAgentRig(tactusPause: true);
        await rig.InviteTab();

        var during = rig.Pause!.Chats(PcScope.All);
        rig.Hub.EndTurn(WebAgentRig.Thread);
        var after = rig.Pause.Chats(PcScope.All);

        Assert.Contains(WebAgentHub.App, during[WebAgentRig.Thread]);
        Assert.False(after.ContainsKey(WebAgentRig.Thread));
    }

    [Fact]
    public async Task ThePillsStopOfVisusOnlyEndsTheWebSteps()
    {
        using var rig = new WebAgentRig(tactusPause: true);
        await rig.InviteTab();

        rig.Run.Stop(PcScope.App("exe:C:\\Windows\\notepad.exe"), PcStopSource.Pill);
        var other = await rig.Call(WebToolSchemas.Tabs);
        rig.Run.Stop(PcScope.App(WebAgentHub.App), PcStopSource.Pill);
        var mine = await rig.Call(WebToolSchemas.Tabs);

        Assert.Null(other.Error);
        Assert.Equal(WebErrorCodes.Stopped, mine.Error);
    }

    [Theory]
    [InlineData("mcp__nth-pc__web_click", true)]
    [InlineData("mcp__nth-pc__web_read", true)]
    [InlineData("mcp__nth-pc__web_teleport", false)]
    [InlineData("mcp__other__web_click", false)]
    public void ClaudeCodesOwnPromptIsAnsweredForTheWebToolsOnly(string name, bool nths)
    {
        Assert.Equal(nths, PcProviderWiring.IsClaudeTool(name));
    }

    [Theory]
    [InlineData("nth-pc: web_read", "web_read")]
    [InlineData("mcp__nth-pc__web_type", "web_type")]
    [InlineData("nth-pc/web_open", "web_open")]
    [InlineData("nth-pc: web_teleport", null)]
    public void AcpTitlesNameTheWebTools(string title, string? tool)
    {
        Assert.Equal(tool, PcProviderWiring.AcpTool(title));
    }

    [Fact]
    public void AClientThatDropsImagesGetsTheScreenshotAsAFileNamedInTheText()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nth-w11-shots-" + Guid.NewGuid().ToString("N"));
        try
        {
            var shot = new WebToolResult("Screenshot of t1.") { Jpeg = AgentPage.Jpeg(64, 48), ImageWidth = 64, ImageHeight = 48 };

            var saved = WebMcpTools.Content(shot, vision: true, imageFolder: folder);
            var inline = WebMcpTools.Content(shot, vision: true);
            var blind = WebMcpTools.Content(shot, vision: false, imageFolder: folder);

            var parts = saved["content"]!.AsArray();
            var text = Assert.Single(parts)!["text"]!.GetValue<string>();
            Assert.Contains("screenshot saved: " + folder, text, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(folder, "*.jpg"));
            Assert.Equal("image", inline["content"]![1]!["type"]!.GetValue<string>());
            Assert.Single(blind["content"]!.AsArray());
            Assert.Single(Directory.GetFiles(folder, "*.jpg"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AnApiSessionWhoseModelSeesImagesGetsTheScreenshotPicture()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab();
        var tools = new WebNativeTools(() => rig.Hub, WebAgentRig.Thread, "gpt");

        var seeing = await tools.Run(WebToolSchemas.Screenshot, JSONValue.Parse(Tab())!, vision: true, CancellationToken.None);
        var blind = await tools.Run(WebToolSchemas.Screenshot, JSONValue.Parse(Tab())!, vision: false, CancellationToken.None);

        Assert.Null(seeing.Error);
        Assert.NotNull(seeing.Jpeg);
        Assert.DoesNotContain("cannot see pictures", seeing.Text, StringComparison.Ordinal);
        Assert.Contains("This model cannot see pictures: use web_read", blind.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnApiCallWithNoArgumentsIsAnEmptyObject()
    {
        using var rig = new WebAgentRig();
        rig.Host.Add("https://a.test/");
        var tools = new WebNativeTools(() => rig.Hub, WebAgentRig.Thread, "gpt");

        var result = await tools.Run(WebToolSchemas.Tabs, new JSONValue.Null(), vision: false, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains("t1 (yours)", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACodexChatKeepsItsOwnComputerUseWhileTactusIsOff()
    {
        using var parts = PcControlBridge.Parts.Override(() => null);

        Assert.False(PcControlBridge.TactusOn);
    }

    [Fact]
    public void TheStopLineNamesTheChordOnlyWhileTactusIsBound()
    {
        Assert.Null(new WebAgentHub(() => null, () => null, null, () => "Ctrl+Alt+Esc").StopChord);
        Assert.Equal("Ctrl+Alt+Esc", new WebAgentHub(() => new PcRunControl(), () => null, null, () => "Ctrl+Alt+Esc").StopChord);
        Assert.Null(new WebAgentHub(() => new PcRunControl(), () => null, null, () => "").StopChord);
    }

    [Fact]
    public async Task AGuardedClickTellsTheCardWhereItActs()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        await ReadInvite(rig);

        await rig.Call(WebToolSchemas.Click, Ref("e3"));

        var card = Assert.Single(rig.Asker.Of<WebGuardAsk>());
        Assert.NotNull(card.Near);
        Assert.True(card.Near!.Value.X > 0 && card.Near.Value.Y > 0);
    }

    [Fact]
    public async Task TheServerSaysVisusIsStillWorkingOnALongWebStep()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        var hold = new TaskCompletionSource<bool>();
        tab.Page.Hold = ("Accessibility.getFullAXTree", hold.Task);
        var registration = new PcRegistration(WebAgentRig.Thread, Marrow.Core.Models.ProviderKind.Claude, "claude-sonnet", true);
        var notes = new List<string>();
        var server = new PcControlMcpServer(registration, _ => null, () => PcSettings.Default, note =>
        {
            lock (notes) notes.Add(note);
            return Task.CompletedTask;
        })
        { Web = new WebMcpTools(() => rig.Hub), ProgressMs = 20 };

        var call = server.Handle("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"web_read\",\"arguments\":{\"tab\":\"t1\"},\"_meta\":{\"progressToken\":\"p\"}}}", CancellationToken.None);
        await PcTestWait.Until(() =>
        {
            lock (notes) return notes.Count > 0;
        });
        hold.SetResult(true);
        var answer = JsonNode.Parse((await call)!)!;

        lock (notes) Assert.Contains("Visus is still working on this step.", notes[0], StringComparison.Ordinal);
        Assert.False(answer["result"]!["isError"]!.GetValue<bool>());
    }
}
