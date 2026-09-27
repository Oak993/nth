using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :15, :77-92; ED-002 / ED-220.
// These tests exercise the actual DroppyCodeApp entry points; OS transport, file IO and window activation are fakes.
public sealed class WindowsToastStartupTests
{
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
    [Trait("Req", "PR-LAUNCH-064")] // App/AppDelegate.swift:77-92; ED-002 / ED-220 (6)
    [Trait("Req", "PR-LAUNCH-065")] // App/AppDelegate.swift:83-91; ED-002 / ED-220 (6)
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
}
