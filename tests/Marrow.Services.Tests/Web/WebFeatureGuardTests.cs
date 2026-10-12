using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 8.4 through the surface every pane listens to: pointer lock asks (the page's gate waits for nth) and Esc always releases
// it; agent tabs never get fullscreen, permissions, sign-ins or dialogs, and the model is told in a note; an agent tab's file input
// is intercepted for the guarded card; preview and agent tabs never reach Windows Hello; a browse page's notification is nth's to
// show; a model step can never take a download, a permission, a file or a passkey (ED-NEW-web-w8b-3).
[Trait("Decision", "ED-NEW-web-w8b-3")]
[Trait("Feature", "W-8b")]
public sealed class WebFeatureGuardTests
{
    private const string Thread = @"C:\threads\one";
    private static readonly KeyChord Escape = new(0x01, ChordModifiers.None);

    [Theory]
    [InlineData(WebSurfaceKind.VisusTab, true, false, false)]
    [InlineData(WebSurfaceKind.VisusPrivateTab, true, false, false)]
    [InlineData(WebSurfaceKind.PreviewTab, true, false, true)]
    [InlineData(WebSurfaceKind.AgentTab, false, true, true)]
    public void Each_kind_gets_its_document_scripts_before_its_first_page(WebSurfaceKind kind, bool gate, bool agent, bool passkeys)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(kind, folder: Thread);
        var scripts = WebRig.Fake(view).DocumentScripts;
        Assert.Equal(gate, scripts.Contains(WebFeatureGuard.PointerLockGateScript));
        Assert.Equal(agent, scripts.Contains(WebFeatureGuard.AgentScript));
        Assert.Equal(passkeys, scripts.Contains(WebFeatureGuard.PasskeyDenyScript));
    }

    [Theory]
    [InlineData(WebSurfaceKind.Artifact)]
    [InlineData(WebSurfaceKind.InlineWidget)]
    [InlineData(WebSurfaceKind.McpApp)]
    public void Sandboxed_content_gets_none_of_them(WebSurfaceKind kind) => Assert.Empty(WebFeatureGuard.DocumentScriptsFor(kind));

    [Fact]
    public void The_agent_script_refuses_fullscreen_pointer_lock_and_page_clipboard_and_the_passkey_script_refuses_webauthn()
    {
        foreach (var word in new[] { "requestFullscreen", "requestPointerLock", "clipboard", "execCommand" })
            Assert.Contains(word, WebFeatureGuard.AgentScript, StringComparison.Ordinal);
        foreach (var word in new[] { "CredentialsContainer", "publicKey", "isUserVerifyingPlatformAuthenticatorAvailable" })
            Assert.Contains(word, WebFeatureGuard.PasskeyDenyScript, StringComparison.Ordinal);
        Assert.Contains(WebFeatureGuard.PointerLockBinding, WebFeatureGuard.PointerLockGateScript, StringComparison.Ordinal);
        Assert.Contains(WebFeatureGuard.PointerLockAnswer, WebFeatureGuard.PointerLockGateScript, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Visus_tab_adds_the_pointer_lock_binding_and_an_agent_tab_intercepts_file_choosers_and_webauthn_as_they_attach()
    {
        using var rig = new WebRig();
        var visus = WebRig.Fake(rig.OpenLive());
        Assert.Contains(("Runtime.addBinding", "{\"name\":\"__nthPointerLock\"}", (string?)null), visus.CdpCalls);
        Assert.DoesNotContain(visus.CdpCalls, call => call.Method == "Page.setInterceptFileChooserDialog");

        var agent = WebRig.Fake(rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread));
        Assert.Contains(("Page.setInterceptFileChooserDialog", "{\"enabled\":true}", (string?)null), agent.CdpCalls);
        Assert.Contains(("WebAuthn.enable", "{\"enableUI\":false}", (string?)null), agent.CdpCalls);
        Assert.DoesNotContain(agent.CdpCalls, call => call.Method == "Runtime.addBinding");

        var preview = WebRig.Fake(rig.OpenLive(WebSurfaceKind.PreviewTab, url: "http://localhost:5173/"));
        Assert.Contains(("WebAuthn.enable", "{\"enableUI\":false}", (string?)null), preview.CdpCalls);
    }

    [Fact]
    public void Pointer_lock_waits_for_nths_answer_and_answers_with_a_user_gesture_in_the_asking_frame()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);
        var asks = new List<WebPointerLockRequest>();
        view.PointerLockRequested += (_, e) => asks.Add(e);

        fake.EmitCdp("Runtime.bindingCalled", Binding("ask", 3, context: 7));
        rig.RunPosted();
        var ask = Assert.Single(asks);
        Assert.Equal("https://a.test", ask.Origin);
        Assert.DoesNotContain(fake.CdpCalls, call => call.Method == "Runtime.evaluate");

        // One ask at a time: a page that asks again while nth asks is refused at once.
        fake.EmitCdp("Runtime.bindingCalled", Binding("ask", 4, context: 7));
        rig.RunPosted();
        Assert.Single(asks);
        var refused = Evaluate(fake.CdpCalls.Last());
        Assert.Equal(WebFeatureGuard.AnswerExpression(4, false), refused["expression"]!.GetValue<string>());

        ask.Answer(true);
        var answer = Evaluate(fake.CdpCalls.Last());
        Assert.Equal(WebFeatureGuard.AnswerExpression(3, true), answer["expression"]!.GetValue<string>());
        Assert.True(answer["userGesture"]!.GetValue<bool>());
        Assert.Equal(7, answer["contextId"]!.GetValue<int>());
    }

    [Fact]
    public void Pointer_lock_with_nobody_to_ask_is_refused_and_a_closed_engine_refuses_a_waiting_ask()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);
        fake.EmitCdp("Runtime.bindingCalled", Binding("ask", 1, context: 2));
        rig.RunPosted();
        Assert.Equal(WebFeatureGuard.AnswerExpression(1, false), Evaluate(fake.CdpCalls.Last())["expression"]!.GetValue<string>());

        WebPointerLockRequest? held = null;
        view.PointerLockRequested += (_, e) => held = e;
        fake.EmitCdp("Runtime.bindingCalled", Binding("ask", 2, context: 2));
        rig.RunPosted();
        Assert.NotNull(held);
        rig.Surface.Close(view);
        Assert.True(held!.IsAnswered);
    }

    [Fact]
    public void A_locked_pointer_is_reported_and_Esc_always_releases_it()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        var changes = new List<bool>();
        view.PointerLockChanged += changes.Add;

        fake.EmitCdp("Runtime.bindingCalled", Binding("locked", 0, context: 2));
        rig.RunPosted();
        Assert.True(view.IsPointerLocked);

        fake.RaiseKey(Escape);
        rig.RunPosted();
        Assert.Contains(fake.CdpCalls, call => call.Method == "Runtime.evaluate" && Evaluate(call)["expression"]!.GetValue<string>() == WebFeatureGuard.ExitPointerLockExpression);

        fake.EmitCdp("Runtime.bindingCalled", Binding("released", 0, context: 2));
        rig.RunPosted();
        Assert.False(view.IsPointerLocked);
        Assert.Equal([true, false], changes);
    }

    [Theory]
    [InlineData("{\"name\":\"other\",\"payload\":\"{\\\"kind\\\":\\\"ask\\\",\\\"id\\\":1}\"}")]
    [InlineData("{\"name\":\"__nthPointerLock\",\"payload\":\"{\\\"kind\\\":\\\"ask\\\",\\\"id\\\":0}\"}")]
    [InlineData("{\"name\":\"__nthPointerLock\",\"payload\":\"not json\"}")]
    [InlineData("{\"name\":\"__nthPointerLock\",\"payload\":\"{\\\"kind\\\":\\\"grant\\\",\\\"id\\\":1}\"}")]
    [InlineData("{oops")]
    public void Anything_but_the_gates_own_calls_is_ignored(string parameters)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var asked = 0;
        view.PointerLockRequested += (_, _) => asked++;
        WebRig.Fake(view).EmitCdp("Runtime.bindingCalled", parameters);
        rig.RunPosted();
        Assert.Equal(0, asked);
        Assert.False(view.IsPointerLocked);
    }

    [Fact]
    public void Fullscreen_is_raised_for_a_Visus_page_and_ended_by_nth_in_an_agent_tab()
    {
        using var rig = new WebRig();
        var visus = rig.OpenLive();
        var raised = new List<bool>();
        visus.FullScreenChanged += (_, full) => raised.Add(full);
        WebRig.Fake(visus).RaiseFullScreen(true);
        Assert.Equal([true], raised);

        var agent = rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread);
        var agentRaised = 0;
        agent.FullScreenChanged += (_, _) => agentRaised++;
        var fake = WebRig.Fake(agent);
        fake.RaiseFullScreen(true);
        Assert.Equal(0, agentRaised);
        Assert.Contains("Script " + WebFeatureGuard.ExitFullscreenScript, fake.Log);
        Assert.Contains(agent.AgentNotes, note => note.Kind == WebAgentNoteKind.FeatureDenied);
    }

    [Fact]
    public void The_model_is_told_what_its_tab_refused()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread);
        var fake = WebRig.Fake(view);
        var noted = new List<WebAgentNote>();
        view.AgentNoted += noted.Add;

        Assert.Equal(WebScriptDialogDecision.Dismiss, WebRig.Decided(fake.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Confirm, "Delete\neverything?")));
        Assert.Null(WebRig.Decided(fake.RaiseAuth("https://intranet.a.test/")));
        Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(fake.RaisePermission("https://a.test", WebPermissionKind.Camera)));
        fake.RaiseDownload("https://a.test/report.csv", "report.csv");

        Assert.Equal(
            [WebAgentNoteKind.DialogDismissed, WebAgentNoteKind.SignInNeeded, WebAgentNoteKind.FeatureDenied, WebAgentNoteKind.DownloadWaiting],
            noted.Select(note => note.Kind));
        Assert.Equal("The page showed a confirm dialog and nth dismissed it: Delete everything?", noted[0].Text);
        Assert.StartsWith("page needs sign-in (intranet.a.test)", noted[1].Text, StringComparison.Ordinal);
        Assert.Contains("report.csv", noted[3].Text, StringComparison.Ordinal);
        Assert.Equal(noted, view.AgentNotes);
    }

    [Fact]
    public void An_agent_tab_keeps_its_latest_notes_only()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread);
        var fake = WebRig.Fake(view);
        for (var i = 0; i < WebSurfaceView.AgentNoteLimit + 5; i++) fake.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Alert, "n" + i);
        Assert.Equal(WebSurfaceView.AgentNoteLimit, view.AgentNotes.Count);
        Assert.EndsWith("n54", view.AgentNotes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Visus_tab_raises_no_notes()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        view.ScriptDialogOpening += (_, e) => e.Respond(WebScriptDialogDecision.Dismiss);
        WebRig.Fake(view).RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Alert, "hi");
        Assert.Empty(view.AgentNotes);
    }

    [Fact]
    public void An_agent_tabs_file_input_is_intercepted_and_only_nths_card_gives_it_files()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread);
        var fake = WebRig.Fake(view);
        WebFileChooser? chooser = null;
        view.FileChooserIntercepted += (_, e) => chooser = e;

        fake.EmitCdp("Page.fileChooserOpened", "{\"frameId\":\"F1\",\"mode\":\"selectSingle\",\"backendNodeId\":12}");
        rig.RunPosted();
        Assert.Equal(new WebFileChooser("F1", "selectSingle", 12), chooser);
        var note = Assert.Single(view.AgentNotes);
        Assert.Equal(WebAgentNoteKind.FileChooserBlocked, note.Kind);

        Assert.True(WebRig.Done(view.GiveFilesAsync(chooser!, [@"C:\Users\d\secret.pdf"])));
        var call = fake.CdpCalls.Last();
        Assert.Equal("DOM.setFileInputFiles", call.Method);
        Assert.Equal("{\"files\":[\"C:\\\\Users\\\\d\\\\secret.pdf\"],\"backendNodeId\":12}", call.Params);
        Assert.DoesNotContain(view.AgentNotes, entry => entry.Text.Contains("secret", StringComparison.Ordinal)); // the model never sees the path
    }

    [Fact]
    public void Only_an_agent_tab_takes_files_from_nths_card()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        Assert.False(WebRig.Done(view.GiveFilesAsync(new WebFileChooser("F", "selectSingle", 3), [@"C:\x.txt"])));
        Assert.DoesNotContain(WebRig.Fake(view).CdpCalls, call => call.Method == "DOM.setFileInputFiles");
    }

    [Fact]
    public void Only_a_browse_page_raises_notifications_and_only_Visus_pages_may_share_the_screen()
    {
        using var rig = new WebRig();
        foreach (var (kind, notifies, captures) in new[]
        {
            (WebSurfaceKind.VisusTab, true, true),
            (WebSurfaceKind.VisusPrivateTab, false, true),
            (WebSurfaceKind.PreviewTab, false, true),
            (WebSurfaceKind.AgentTab, false, false),
        })
        {
            var view = rig.OpenLive(kind, folder: Thread, url: kind == WebSurfaceKind.PreviewTab ? "http://localhost:5173/" : "https://a.test/");
            var raised = 0;
            view.NotificationReceived += (_, _) => raised++;
            var fake = WebRig.Fake(view);
            fake.RaiseNotification("https://a.test", "Build done");
            Assert.Equal(notifies ? 1 : 0, raised);
            Assert.Equal(captures, WebRig.Decided(fake.RaiseScreenCapture("https://a.test")));
        }
    }

    [Fact]
    public void A_saved_download_starts_a_transfer_to_the_chosen_path_and_a_cancel_starts_none()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);
        view.DownloadStarting += (_, e) => e.Respond(new WebDownloadDecision(true, @"C:\Users\d\Downloads\f.zip"));
        var saved = fake.RaiseDownload("https://a.test/f.zip", "f.zip", bytes: 2048);
        var transfer = Assert.IsType<FakeDownload>(WebRig.Done(saved.Started));
        Assert.Equal(@"C:\Users\d\Downloads\f.zip", transfer.ResultFilePath);
        Assert.Equal(2048, transfer.TotalBytes);
        transfer.Progress(1024);
        transfer.Pause();
        Assert.Equal(WebDownloadState.Paused, transfer.State);
        transfer.Resume();
        transfer.Complete();
        Assert.Equal((WebDownloadState.Completed, 2048L), (transfer.State, transfer.BytesReceived));

        using var rig2 = new WebRig();
        var other = rig2.OpenLive();
        other.DownloadStarting += (_, e) => e.Respond(WebDownloadDecision.Cancel);
        var cancelled = WebRig.Fake(other).RaiseDownload("https://a.test/g.zip", "g.zip");
        Assert.Null(WebRig.Done(cancelled.Started));
        Assert.Empty(WebRig.Fake(other).Downloads);
    }

    [Theory]
    [InlineData("Browser.setDownloadBehavior")]
    [InlineData("Browser.grantPermissions")]
    [InlineData("DOM.setFileInputFiles")]
    [InlineData("WebAuthn.addVirtualAuthenticator")]
    public void A_model_step_cannot_make_the_users_calls_and_nothing_reaches_the_page(string method)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);
        var before = fake.CdpCalls.Count;
        var call = view.Cdp!.CallForModelAsync(method, new JsonObject());
        Assert.True(call.IsFaulted);
        var refused = Assert.IsType<CdpModelCallRefusedException>(call.Exception!.InnerException);
        Assert.Equal(method, refused.Method);
        Assert.Equal(before, fake.CdpCalls.Count);

        WebRig.Done(view.Cdp!.CallForModelAsync("DOM.getDocument", new JsonObject { ["depth"] = 1 }));
        Assert.Equal("DOM.getDocument", fake.CdpCalls.Last().Method);
    }

    private static string Binding(string kind, int id, int context) =>
        new JsonObject
        {
            ["name"] = WebFeatureGuard.PointerLockBinding,
            ["payload"] = new JsonObject { ["kind"] = kind, ["id"] = id }.ToJsonString(),
            ["executionContextId"] = context,
        }.ToJsonString();

    private static JsonObject Evaluate((string Method, string Params, string? SessionId) call)
    {
        Assert.Equal("Runtime.evaluate", call.Method);
        return JsonNode.Parse(call.Params)!.AsObject();
    }
}
