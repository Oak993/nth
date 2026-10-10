using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :15, :77-92; ED-002 / ED-220.
// These tests exercise the actual DroppyCodeApp entry points; OS transport, file IO and window activation are fakes.
public sealed class WindowsToastStartupTests
{
    [Theory]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:15; ED-220 (2), (6) protocol retains origin identity
    [InlineData("nth-dev://notification?payload=%7B%22update%22%3A%222.0%22%7D", "Nth.App.Dev")]
    [InlineData("nth-release://notification?payload=%7B%22update%22%3A%222.0%22%7D", "Nth.App")]
    [InlineData("nth-dev://notification?payload=invalid", "Nth.App")]
    public void Release_protocol_launch_retains_dev_identity_without_inherited_environment(string argument, string appId) =>
        Assert.Equal(appId, ToastBuildIdentity.ForLaunch(debug: false, dev: false, [argument]).AppId);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Source-derived; OD-0037 accepts the activation callback as the proof of a toast click.
    [Trait("Req", "PR-LAUNCH-064")] // App/AppDelegate.swift:77-92; ED-002, ED-212, ED-220 (6); owner_decisions.md OD-0037
    public void Activation_keeps_its_origin_root_even_if_another_dev_instance_registered_the_shared_scheme()
    {
        var origin = new ToastBuildIdentity("Nth.App.Dev", "nth-dev") { DevRoot = "C:\\First Dev Root" };
        var current = origin with { DevRoot = "C:\\Second Dev Root" };
        var info = new Dictionary<string, object> { ["threadID"] = "00112233-4455-6677-8899-AABBCCDDEEFF" };
        var uri = ToastActivation.Encode(origin, info);
        Assert.Equal("C:\\First Dev Root", ToastActivation.DevRoot(current, uri));
        Assert.Equal(info["threadID"], ToastActivation.Decode(current, uri)!.UserInfo["threadID"]);
        Assert.Null(ToastActivation.DevRoot(new("Nth.App", "nth-release"), uri));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:15; ED-220 (2) existing app icon resource
    public void The_existing_app_icon_can_be_read_before_Avalonia_startup()
    {
        // Read the compiled app's resource, as production does before any window; no registry/file writes.
        using var icon = new Avalonia.Platform.StandardAssetLoader().Open(new Uri("avares://Marrow.App/Assets/icon.png"));
        var signature = new byte[8];
        icon.ReadExactly(signature);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature);
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:15; ED-220 (2) app icon
    public void Shell_readable_icon_is_written_only_if_the_embedded_PNG_differs()
    {
        var files = new RecordingToastIconFiles();
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10];
        Assert.Equal("fake/icon.png", ToastIcon.Install(files, "fake/icon.png", png));
        Assert.Equal(png, files.Files["fake/icon.png"]);
        Assert.Equal(1, files.Writes);
        ToastIcon.Install(files, "fake/icon.png", png.ToArray());
        Assert.Equal(1, files.Writes);
        ToastIcon.Install(files, "fake/icon.png", [.. png, 1]);
        Assert.Equal(2, files.Writes);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Source-derived; OD-0037 accepts the activation callback as the proof of a toast click.
    [Trait("Req", "PR-LAUNCH-064")] // App/AppDelegate.swift:77-92; ED-002 / ED-220 (6); owner_decisions.md OD-0037
    [Trait("Req", "PR-LAUNCH-065")] // App/AppDelegate.swift:83-91; ED-002 / ED-220 (6); owner_decisions.md OD-0037
    [Trait("Req", "PR-UPDATE-024")] // App/AppDelegate.swift:83-91; ED-002 / ED-220 (6); owner_decisions.md OD-0037
    public void DroppyCodeApp_cold_and_second_launch_forward_the_update_without_ordinary_reopen()
    {
        using var runtime = new TestRuntime();
        var windows = new RecordingToastWindows();
        var transport = new RecordingToastTransport();
        using var ui = ToastActivationWindows.Current.Override(windows);
        using var pipe = ToastLaunchTransport.Current.Override(transport);
        var appDelegate = new AppDelegate();
        var uri = ToastActivation.Encode(WindowsToastStartup.Identity, new Dictionary<string, object> { ["update"] = "2.0" });
        runtime.OnMain(() => DroppyCodeApp.ReceiveNotificationArguments(appDelegate, [uri]));
        Assert.Equal(new[] { "activate", "about" }, windows.Events);
        windows.Events.Clear();
        using var listener = runtime.OnMain(() => DroppyCodeApp.ListenForNotificationArguments(appDelegate));
        runtime.OnMain(() => Assert.True(DroppyCodeApp.ForwardNotificationArguments([uri])));
        Assert.Equal(DroppyCodeApp.ToastPipeName, Assert.Single(transport.Sent).Name);
        Assert.Equal(new[] { "activate", "about" }, windows.Events);
        Assert.False(DroppyCodeApp.ForwardNotificationArguments([]));
        Assert.Single(transport.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-220")]
    public void Forwarded_activations_wait_for_launch_authority_without_losing_the_payload(bool update)
    {
        using var runtime = new TestRuntime();
        var windows = new RecordingToastWindows();
        var transport = new RecordingToastTransport();
        using var ui = ToastActivationWindows.Current.Override(windows);
        using var pipe = ToastLaunchTransport.Current.Override(transport);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appDelegate = new AppDelegate();
        var payload = update ? new Dictionary<string, object> { ["update"] = "2.0" }
            : new Dictionary<string, object> { ["threadID"] = Guid.NewGuid().ToString() };
        var uri = ToastActivation.Encode(WindowsToastStartup.Identity, payload);
        using var listener = runtime.OnMain(() => DroppyCodeApp.ListenForNotificationArguments(appDelegate, ready.Task));
        runtime.OnMain(() => Assert.True(DroppyCodeApp.ForwardNotificationArguments([uri])));
        Assert.Empty(windows.Events);
        ready.SetResult();
        runtime.Loop.RunUntil(() => windows.Events.Count == 2);
        Assert.Equal(update ? new[] { "activate", "about" } : new[] { "main", "activate" }, windows.Events);
        Assert.Equal(uri, Assert.Single(transport.Sent).Payload);
    }
}
