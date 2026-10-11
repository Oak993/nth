using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 5.4, 5.5, 8.1, 8.3, 8.4: each request has a safe default (deny, cancel, block) when nobody answers, and the
// baseline of each profile answers before any pane can: the agent profile and sandboxed content never get permissions,
// sandboxed content never downloads, opens windows or launches protocols, and "Continue anyway" is never offered to
// the model or sandboxed content. A baseline answer is final.
[Trait("Decision", "ED-NEW-web-2")]
[Trait("Decision", "ED-NEW-web-7")]
[Trait("Feature", "W-7")]
public sealed class WebBaselinePolicyTests
{
    [Fact]
    public void Unanswered_requests_take_their_safe_default()
    {
        using var rig = new WebRig();
        var fake = WebRig.Fake(rig.OpenLive(shown: true));
        Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(fake.RaisePermission("https://a.test", WebPermissionKind.Camera)));
        Assert.Equal(WebDownloadDecision.Cancel, WebRig.Decided(fake.RaiseDownload("https://a.test/f.zip", "f.zip")));
        Assert.Equal(WebNewWindowDecision.Block, WebRig.Decided(fake.RaiseNewWindow("https://b.test/")));
        Assert.False(WebRig.Decided(fake.RaiseExternalUri("mailto:a@b.test", "https://a.test", userInitiated: true)));
        Assert.Null(WebRig.Decided(fake.RaiseAuth("https://a.test/")));
        Assert.Equal(WebScriptDialogDecision.Dismiss, WebRig.Decided(fake.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Confirm, "Sure?")));
        Assert.False(WebRig.Decided(fake.RaiseCertificateError("https://a.test/")));
    }

    [Fact]
    public void A_Visus_tab_lets_its_pane_answer_later()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        WebPermissionRequest? held = null;
        view.PermissionRequested += (_, e) =>
        {
            e.Defer();
            held = e;
        };

        var request = WebRig.Fake(view).RaisePermission("https://a.test", WebPermissionKind.Microphone);

        Assert.False(request.Decision.IsCompleted);
        held!.Respond(WebPermissionDecision.AllowOnce);
        Assert.Equal(WebPermissionDecision.AllowOnce, WebRig.Decided(request));
    }

    [Theory]
    [InlineData(WebSurfaceKind.AgentTab)]
    [InlineData(WebSurfaceKind.Artifact)]
    [InlineData(WebSurfaceKind.InlineWidget)]
    [InlineData(WebSurfaceKind.McpApp)]
    public void The_agent_profile_and_sandboxed_content_never_get_a_permission(WebSurfaceKind kind)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(kind, folder: @"C:\threads\one");
        view.PermissionRequested += (_, e) => e.Respond(WebPermissionDecision.AllowAlways);
        var request = WebRig.Fake(view).RaisePermission("https://a.test", WebPermissionKind.Location);
        Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(request));
    }

    [Theory]
    [InlineData(WebPermissionKind.PersistentStorage)]
    [InlineData(WebPermissionKind.Unknown)]
    public void A_kind_outside_the_design_list_is_denied_with_no_prompt_even_in_a_Visus_tab(WebPermissionKind kind)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var prompted = 0;
        view.PermissionRequested += (_, e) =>
        {
            if (!e.Decision.IsCompleted) prompted++;
            e.Respond(WebPermissionDecision.AllowAlways);
        };

        var request = WebRig.Fake(view).RaisePermission("https://a.test", kind);

        Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(request));
        Assert.Equal(0, prompted);
    }

    [Fact]
    public void Preview_tabs_never_get_notifications()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(WebSurfaceKind.PreviewTab);
        view.PermissionRequested += (_, e) => e.Respond(WebPermissionDecision.AllowOnce);
        var fake = WebRig.Fake(view);
        Assert.Equal(WebPermissionDecision.Deny, WebRig.Decided(fake.RaisePermission("http://localhost:5173", WebPermissionKind.Notifications)));
        Assert.Equal(WebPermissionDecision.AllowOnce, WebRig.Decided(fake.RaisePermission("http://localhost:5173", WebPermissionKind.ClipboardRead)));
    }

    [Theory]
    [InlineData(WebSurfaceKind.Artifact)]
    [InlineData(WebSurfaceKind.InlineWidget)]
    [InlineData(WebSurfaceKind.McpApp)]
    public void Sandboxed_content_never_downloads_opens_windows_launches_protocols_or_continues_past_a_bad_certificate(WebSurfaceKind kind)
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(kind);
        view.DownloadStarting += (_, e) => e.Respond(new WebDownloadDecision(true, @"C:\x\f.zip"));
        view.NewWindowRequested += (_, e) => e.Respond(WebNewWindowDecision.HandledByNth);
        view.ExternalUriRequested += (_, e) => e.Respond(true);
        view.AuthenticationRequested += (_, e) => e.Respond(new WebCredentials("u", "p"));
        view.ScriptDialogOpening += (_, e) => e.Respond(new WebScriptDialogDecision(true, "x"));
        view.CertificateErrorDetected += (_, e) => e.Respond(true);
        var fake = WebRig.Fake(view);

        Assert.Equal(WebDownloadDecision.Cancel, WebRig.Decided(fake.RaiseDownload("https://a.test/f.zip", "f.zip")));
        Assert.Equal(WebNewWindowDecision.Block, WebRig.Decided(fake.RaiseNewWindow("https://b.test/")));
        Assert.False(WebRig.Decided(fake.RaiseExternalUri("ms-settings:", "https://a.test", userInitiated: true)));
        Assert.Null(WebRig.Decided(fake.RaiseAuth("https://a.test/")));
        Assert.Equal(WebScriptDialogDecision.Dismiss, WebRig.Decided(fake.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Alert, "hi")));
        Assert.False(WebRig.Decided(fake.RaiseCertificateError("https://a.test/")));
    }

    [Fact]
    public void Agent_tabs_deny_protocols_auth_dialogs_and_bad_certificates_with_no_prompt()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(WebSurfaceKind.AgentTab, folder: @"C:\threads\one");
        view.ExternalUriRequested += (_, e) => e.Respond(true);
        view.AuthenticationRequested += (_, e) => e.Respond(new WebCredentials("u", "p"));
        view.ScriptDialogOpening += (_, e) => e.Respond(new WebScriptDialogDecision(true, null));
        view.CertificateErrorDetected += (_, e) => e.Respond(true);
        var fake = WebRig.Fake(view);

        Assert.False(WebRig.Decided(fake.RaiseExternalUri("mailto:a@b.test", "https://a.test", userInitiated: true)));
        Assert.Null(WebRig.Decided(fake.RaiseAuth("https://a.test/")));
        Assert.Equal(WebScriptDialogDecision.Dismiss, WebRig.Decided(fake.RaiseScriptDialog("https://a.test/", WebScriptDialogKind.Prompt, "?")));
        Assert.False(WebRig.Decided(fake.RaiseCertificateError("https://a.test/")));
    }

    [Fact]
    public void A_Visus_tab_launches_a_protocol_only_from_a_user_gesture()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        view.ExternalUriRequested += (_, e) => e.Respond(true);
        var fake = WebRig.Fake(view);
        Assert.False(WebRig.Decided(fake.RaiseExternalUri("mailto:a@b.test", "https://a.test", userInitiated: false)));
        Assert.True(WebRig.Decided(fake.RaiseExternalUri("mailto:a@b.test", "https://a.test", userInitiated: true)));
    }
}
