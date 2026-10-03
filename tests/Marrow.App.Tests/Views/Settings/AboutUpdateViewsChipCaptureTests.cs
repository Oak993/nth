using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :539-571 (the identity chip) and :573-704 (the version
// chip). The reference is the recorded Mac runtime, E:\Droppy\work\reference-video\1-settings-tour.mov, S 199.5-214.5 (the Settings
// header on About, v1.7.0 and "Up to date"), measured on the extracted frame frames/w-1-settings-tour-210.0/win/000204.png
// (2560x1440 at 1 px = 1 pt): identity chip 81 x 32 (edges 587.5 to 668.5), Up to date chip 108 x 32 (677.5 to 785.5), 9 to 10 pt
// between them. The Mac draws SF and the Droppy icon; the port draws Inter (ED-059, OD-0009) and the Marrow icon (OD-0033), so
// the widths are compared with a tolerance. Only the Up to date standing is on any reference frame: Check failed and Not checked
// yet appear on none (the recorded feed answered), so they stay UNVERIFIED under OD-0001.
[Collection("Headless")]
public sealed class AboutUpdateViewsChipCaptureTests(HeadlessSession headless)
{
    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);

    [Fact]
    [Trait("Req", "PR-UPDATE-072")] // App/Views/Settings/AboutUpdateViews.swift:587-621: the Up to date chip against the reference frame (the capture comparison, for the one standing on a frame)
    [Trait("Req", "UI-SETTINGS-055")] // App/Views/Settings/AboutUpdateViews.swift:551-571: the identity chip against the reference frame
    [Trait("Req", "UI-UPDATE-001")] // App/Views/Settings/AboutUpdateViews.swift:544-571: the identity chip and its 10 pt gap to the version chip
    [Trait("Req", "UI-UPDATE-002")] // App/Views/Settings/AboutUpdateViews.swift:587-621,650-682: the Up to date chip against the reference frame
    [Trait("Decision", "ED-059")] // Inter label widths against the reference's SF, compared with a tolerance
    public void The_header_chips_match_the_reference_frame_sizes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], "")); Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now);
        using var shared = UpdateChecker.OverrideShared(checker);
        var accessory = new AboutUpdateChromeAccessory();
        var window = new Avalonia.Controls.Window { Width = 400, Height = 100, UseLayoutRounding = false, RequestedThemeVariant = ThemeVariant.Dark, Content = accessory };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        var identity = Assert.Single(accessory.GetVisualDescendants().OfType<AboutIdentityPill>()); var version = Assert.Single(accessory.GetVisualDescendants().OfType<AboutVersionPill>());
        Assert.Contains(version.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Up to date");
        Assert.Equal(32, identity.Bounds.Height); Assert.Equal(32, version.Bounds.Height); // reference: both 32 tall
        Assert.InRange(identity.Bounds.Width, 81 - 8, 81 + 8); // reference 81
        Assert.InRange(version.Bounds.Width, 108 - 8, 108 + 8); // reference 108
        Assert.InRange(version.Bounds.X - identity.Bounds.Right, 9, 10); // reference 9 to 10 (the HStack's 10)
        window.Close();
    });
}
