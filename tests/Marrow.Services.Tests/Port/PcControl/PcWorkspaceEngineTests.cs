using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus's separate workspace in the engine (unit P18, ED-NEW-pcws-1, ED-NEW-pcws-3, ED-NEW-pcws-8; DESIGN.md
// 2.13, 5.2) on a FakeWorkspaceHost: routing by place, not_in_workspace before anything is asked, an app that opens on the
// desk instead, steps on workspace windows through the desk's gates and into the agent, the agent's asks in the chat, the
// stop, "Take over", pc_list_apps, and ids that never mix.
[Trait("Decision", "ED-NEW-pcws-1")]
public sealed class PcWorkspaceEngineTests
{
    private const string NotepadPath = "C:\\\\Apps\\\\notepad.exe";
    private const string Inside = "w:400000000000001a";

    private static (PcEngineHarness Harness, FakeWorkspaceHost Host, PcControlEngine Engine) Ready()
    {
        var host = new FakeWorkspaceHost();
        var harness = new PcEngineHarness { Workspace = host };
        return (harness, host, harness.Engine());
    }

    private static string Launch(string path, string place) => "{\"path\":\"" + path + "\",\"place\":\"" + place + "\"}";

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-3")]
    public async Task A_workspace_that_cannot_be_used_is_refused_with_not_in_workspace_before_anything_is_asked()
    {
        var bare = new PcEngineHarness();
        var refused = await PcEngineHarness.Call(bare.Engine(), PcToolSchemas.LaunchApp, Launch(NotepadPath, "workspace"));
        Assert.Equal((PcErrorCode.NotInWorkspace, WorkspaceRouter.NotReleased), (refused.Error, refused.Reason));

        var (harness, host, engine) = Ready();
        host.Released = false;
        Assert.Equal(WorkspaceRouter.NotReleased, (await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch(NotepadPath, "workspace"))).Reason);
        host.Released = true;
        host.Current = WorkspaceStatus.NotSetUp;
        Assert.Equal(WorkspaceRouter.NotSetUp, (await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch(NotepadPath, "workspace"))).Reason);
        host.Current = WorkspaceStatus.Ready;
        harness.Settings = harness.Settings with { WorkspaceUse = "never" };
        var never = await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch(NotepadPath, "workspace"));
        Assert.Equal((PcErrorCode.NotInWorkspace, WorkspaceRouter.UserSaidNever), (never.Error, never.Reason));
        Assert.Empty(bare.Approver.Asked);
        Assert.Empty(harness.Approver.Asked);
        Assert.Empty(host.Launched);
    }

    [Fact]
    public async Task Auto_starts_a_new_app_in_a_ready_workspace_and_its_windows_carry_the_workspace_id()
    {
        var (harness, host, engine) = Ready();
        host.OnLaunch = _ => new WorkspaceLaunch(true, [host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101)], null);
        var started = await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch(NotepadPath, "auto"));
        Assert.True(started.Ok, started.Render());
        Assert.Contains("started in the separate workspace", started.Body, StringComparison.Ordinal);
        Assert.Contains(Inside + " \"Untitled - Notepad\"", started.Body, StringComparison.Ordinal);
        Assert.Equal(new[] { "C:\\Apps\\notepad.exe" }, host.Launched);
        Assert.Equal(new[] { ("notepad.exe", PcCapability.Act, true) }, harness.Approver.Asked);
        Assert.Equal("workspace", harness.Audit.Entries.Single().Rung);
    }

    [Fact]
    public async Task Auto_keeps_an_app_that_is_already_on_the_desk_there_and_desk_never_asks_the_workspace()
    {
        var (harness, host, engine) = Ready();
        harness.Notepad();
        harness.Desktop.OnLaunch = _ => harness.Desktop.AddApp(0x2a, "Untitled - Notepad", "notepad.exe", 102);
        var auto = await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch(NotepadPath, "auto"));
        Assert.True(auto.Ok, auto.Render());
        Assert.Contains("w:2a", auto.Body, StringComparison.Ordinal);
        var desk = await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch("C:\\\\Apps\\\\paint.exe", "desk"));
        Assert.NotEqual(PcErrorCode.NotInWorkspace, desk.Error);
        Assert.Empty(host.Launched);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-3")]
    public async Task An_app_that_opens_on_the_desk_instead_is_reported_not_in_workspace_and_stays_on_the_desk()
    {
        var (harness, host, engine) = Ready();
        host.OnLaunch = _ =>
        {
            harness.Desktop.AddApp(0x3a, "Spotify Premium", "spotify.exe", 303);
            return new WorkspaceLaunch(true, [], null);
        };
        var handed = await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch("C:\\\\Apps\\\\spotify.exe", "workspace"));
        Assert.Equal(PcErrorCode.NotInWorkspace, handed.Error);
        Assert.Contains("w:3a \"Spotify Premium\"", handed.Reason, StringComparison.Ordinal);
        Assert.True(harness.Router.DeskOnly("exe:c:\\apps\\spotify.exe"));
        var asked = harness.Approver.Asked.Count;
        var again = await PcEngineHarness.Call(engine, PcToolSchemas.LaunchApp, Launch("C:\\\\Apps\\\\spotify.exe", "workspace"));
        Assert.Equal((PcErrorCode.NotInWorkspace, WorkspaceRouter.OneCopy), (again.Error, again.Reason));
        Assert.Equal(asked, harness.Approver.Asked.Count);
        Assert.Single(host.Launched);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-8")]
    public async Task A_step_on_a_workspace_window_passes_the_desk_gates_then_runs_in_the_agent()
    {
        var (harness, host, engine) = Ready();
        harness.Notepad();
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var look = await PcEngineHarness.Call(engine, PcToolSchemas.Observe, "{\"window\":\"" + Inside + "\",\"screenshot\":\"no\"}");
        Assert.True(look.Ok, look.Render());
        var call = host.Calls.Single();
        Assert.Equal(("chat-1", PcToolSchemas.Observe, Inside), (call.Thread, call.Tool, call.Args["window"]!.GetValue<string>()));
        Assert.Same(harness.Settings, call.Settings);
        Assert.Equal(new[] { ("notepad.exe", PcCapability.See, true) }, harness.Approver.Asked);
        Assert.Equal(0, harness.Desktop.TreeReads);
        var entry = harness.Audit.Entries.Single();
        Assert.Equal(("workspace", "notepad.exe"), (entry.Rung, entry.App));
    }

    [Fact]
    public async Task The_deny_list_admin_rights_and_a_refusal_hold_in_the_workspace_and_nothing_reaches_the_agent()
    {
        var (harness, host, engine) = Ready();
        host.Add(0x3c, "PowerShell", "pwsh.exe", 303);
        host.Add(0x4c, "Admin tool", "tool.exe", 404, elevated: true);
        host.Add(0x5c, "Bank", "bank.exe", 505);
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        harness.Settings = harness.Settings with { UserDeniedApps = ["C:\\Apps\\bank.exe"] };
        async Task<PcErrorCode?> Look(string window) => (await PcEngineHarness.Call(engine, PcToolSchemas.Observe, "{\"window\":\"" + window + "\"}")).Error;
        Assert.Equal(PcErrorCode.DeniedApp, await Look("w:400000000000003c"));
        Assert.Equal(PcErrorCode.Elevated, await Look("w:400000000000004c"));
        Assert.Equal(PcErrorCode.DeniedApp, await Look("w:400000000000005c"));
        Assert.Equal(PcErrorCode.WindowGone, await Look("w:40000000000000ff"));
        harness.Approver.Answer = PcApproval.Denied;
        Assert.Equal(PcErrorCode.NotApproved, await Look(Inside));
        harness.Approver.Answer = PcApproval.Chat;
        harness.Desktop.Secure = true;
        Assert.Equal(PcErrorCode.SecureDesktop, await Look(Inside));
        harness.Desktop.Secure = false;
        host.Released = false;
        Assert.Equal(PcErrorCode.WindowGone, await Look(Inside));
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task The_agents_approvals_and_guarded_actions_are_asked_in_the_chat()
    {
        var (harness, host, engine) = Ready();
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        WorkspaceApproval? paint = null;
        WorkspaceApproval? shell = null;
        WorkspaceConfirm? send = null;
        host.OnCall = async (call, asks, cancellationToken) =>
        {
            paint = await asks.Approve(new PcAppIdentity("C:\\Apps\\paint.exe"), "paint", PcCapability.Act, cancellationToken);
            shell = await asks.Approve(new PcAppIdentity("C:\\Apps\\pwsh.exe"), "pwsh", PcCapability.See, cancellationToken);
            send = await asks.Confirm("Send", "Send", "outlook", cancellationToken);
            return PcResult.Success(body: "done");
        };
        harness.Confirmer.Answer = false;
        harness.Confirmer.Said = "not this one";
        var acted = await PcEngineHarness.Call(engine, PcToolSchemas.Click, "{\"window\":\"" + Inside + "\",\"element\":\"2\"}");
        Assert.True(acted.Ok, acted.Render());
        Assert.Equal(PcApproval.Chat, paint!.Answer);
        Assert.Equal(PcApproval.Denied, shell!.Answer);
        Assert.Equal(new WorkspaceConfirm(false, "not this one"), send);
        Assert.Contains(("paint.exe", PcCapability.Act, true), harness.Approver.Asked);
        Assert.DoesNotContain(harness.Approver.Asked, a => a.App == "pwsh.exe");
        Assert.Equal(new[] { "Send Send" }, harness.Confirmer.Asked);
    }

    [Fact]
    public async Task Typed_text_joins_the_audit_only_when_the_user_logs_it_and_never_from_a_password_field()
    {
        var (harness, host, engine) = Ready();
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var typed = "{\"window\":\"" + Inside + "\",\"text\":\"hello there\"}";
        await PcEngineHarness.Call(engine, PcToolSchemas.Type, typed);
        Assert.Equal((11, (string?)null), (harness.Audit.Entries[^1].TextLength, harness.Audit.Entries[^1].Text));
        harness.Settings = harness.Settings with { LogTypedText = true };
        await PcEngineHarness.Call(engine, PcToolSchemas.Type, typed);
        Assert.Equal("hello there", harness.Audit.Entries[^1].Text);
        host.OnCall = (_, _, _) => Task.FromResult(PcResult.Fail(PcErrorCode.PasswordField, "this is a password field"));
        await PcEngineHarness.Call(engine, PcToolSchemas.Type, "{\"window\":\"" + Inside + "\",\"text\":\"hunter2\"}");
        Assert.Null(harness.Audit.Entries[^1].TextLength);
    }

    [Fact]
    public async Task The_users_stop_cancels_a_step_running_in_the_workspace()
    {
        var (harness, host, engine) = Ready();
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var started = new TaskCompletionSource();
        host.OnCall = async (_, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return PcResult.Success();
        };
        var step = PcEngineHarness.Call(engine, PcToolSchemas.Observe, "{\"window\":\"" + Inside + "\"}");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Control.StopKey();
        var stopped = await step.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcErrorCode.StoppedByUser, stopped.Error);
    }

    [Fact]
    public async Task A_step_waits_while_the_user_has_taken_over_and_runs_when_they_give_it_back()
    {
        var (_, host, engine) = Ready();
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        host.TakenOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var step = PcEngineHarness.Call(engine, PcToolSchemas.Observe, "{\"window\":\"" + Inside + "\"}");
        Assert.True(await PcTestWait.StillWaiting(step));
        Assert.Empty(host.Calls);
        host.TakenOver.SetResult();
        Assert.True((await step.WaitAsync(TimeSpan.FromSeconds(5))).Ok);
        Assert.Single(host.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-4")]
    public async Task Desk_and_workspace_ids_never_mix()
    {
        var (harness, host, engine) = Ready();
        harness.Notepad();
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var mixed = await PcEngineHarness.Call(engine, PcToolSchemas.Batch, "{\"window\":\"" + Inside + "\",\"actions\":[{\"tool\":\"pc_click\",\"window\":\"w:1a\",\"element\":\"2\"}]}");
        Assert.Equal(PcErrorCode.BadArgs, mixed.Error);
        var lost = await PcEngineHarness.Call(engine, PcToolSchemas.Zoom, "{\"obs\":\"" + PcObsId.Format(WorkspaceRefs.ObservationBase + 5) + "\",\"x\":1,\"y\":1}");
        Assert.Equal(PcErrorCode.BadArgs, lost.Error);
        var obs = PcObsId.Format(WorkspaceRefs.ObservationBase + 1);
        host.OnCall = (call, _, _) => Task.FromResult(PcResult.Success(body: call.Tool) with { Window = new PcWindowLine("Untitled - Notepad", "notepad.exe", obs, 1200, 800, 1) });
        Assert.True((await PcEngineHarness.Call(engine, PcToolSchemas.Observe, "{\"window\":\"" + Inside + "\"}")).Ok);
        var zoom = await PcEngineHarness.Call(engine, PcToolSchemas.Zoom, "{\"obs\":\"" + obs + "\",\"x\":1,\"y\":1}");
        Assert.True(zoom.Ok, zoom.Render());
        Assert.Equal(PcToolSchemas.Zoom, host.Calls[^1].Tool);
        var batch = await PcEngineHarness.Call(engine, PcToolSchemas.Batch, "{\"window\":\"" + Inside + "\",\"actions\":[{\"tool\":\"pc_click\",\"element\":\"2\"}]}");
        Assert.Equal(PcToolSchemas.Batch, host.Calls[^1].Tool);
        Assert.True(batch.Ok, batch.Render());
        Assert.Equal(0, harness.Desktop.TreeReads);
    }

    [Fact]
    public async Task List_apps_shows_the_workspace_after_the_desk_once_it_is_released()
    {
        var (harness, host, engine) = Ready();
        harness.Notepad();
        host.Current = new WorkspaceStatus(WorkspaceState.Running, 1);
        host.Add(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var listed = (await PcEngineHarness.Call(engine, PcToolSchemas.ListApps, "{}")).Body!;
        Assert.Contains("separate workspace: running", listed, StringComparison.Ordinal);
        Assert.Contains(", place: workspace", listed, StringComparison.Ordinal);
        Assert.Contains(Inside + " \"Untitled - Notepad\", in the workspace", listed, StringComparison.Ordinal);
        Assert.True(listed.IndexOf("w:1a ", StringComparison.Ordinal) < listed.IndexOf("separate workspace", StringComparison.Ordinal));
        host.Released = false;
        Assert.DoesNotContain("workspace", (await PcEngineHarness.Call(engine, PcToolSchemas.ListApps, "{}")).Body!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_end_of_a_turn_reaches_the_agent()
    {
        var (_, host, engine) = Ready();
        engine.EndTurn();
        Assert.Equal(new[] { "chat-1" }, host.EndedTurns);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-8")]
    public async Task Inside_the_workspace_the_engine_takes_only_workspace_ids_and_gives_them_back_tagged()
    {
        using var rig = new PcAssemblyRig();
        var settings = new WorkspaceAgentSettings();
        using var assembly = WorkspaceAgent.Assembly(WorkspaceAgent.Backends(rig.Backends, "C:\\Profiles"), settings);
        rig.Desktop.AddApp(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var approver = new PcFakeApprover();
        using var engine = new PcControlEngine(assembly.WorkspaceParts(approver, new PcFakeConfirmer()), new PcCaller("chat-1", () => true, true));
        var desk = await engine.Call(PcToolSchemas.Observe, (JsonObject)JsonNode.Parse("{\"window\":\"w:1a\"}")!, CancellationToken.None);
        Assert.Equal(PcErrorCode.BadArgs, desk.Error);
        var inside = await engine.Call(PcToolSchemas.Observe, (JsonObject)JsonNode.Parse("{\"window\":\"" + Inside + "\",\"screenshot\":\"no\"}")!, CancellationToken.None);
        Assert.True(inside.Ok, inside.Render());
        Assert.True(WorkspaceRefs.IsWorkspaceObservation(inside.Window!.Obs));
        var listed = await engine.Call(PcToolSchemas.ListApps, new JsonObject(), CancellationToken.None);
        Assert.Contains(Inside, listed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("separate workspace", listed.Body, StringComparison.Ordinal);
    }
}
