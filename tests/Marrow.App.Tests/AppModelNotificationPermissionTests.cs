using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Xunit;

namespace Marrow.App.Tests;

// App/AppModel.swift:118 didRequestNotifications, :1240-1246 requestNotificationPermission, :1251 notify. The Windows
// toast centre has no runtime prompt (PLATFORM BOUNDARY, ED-220 (4)): what is kept is the lazy, once-per-model request
// point, which these tests observe through the centre the model is given (an ordered recorder, never a real toast).
// Capture-run mode (:1241) is not ported (ED-001).
[Collection("Headless")]
public sealed class AppModelNotificationPermissionTests(HeadlessSession headless)
{
    private static readonly Guid SomeThread = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Fact]
    [Trait("Req", "PR-LAUNCH-067")] // AppModel.swift:1240-1246; AppDelegate.swift:15-29
    public void Launching_the_app_requests_no_notification_permission() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        var center = new OrderedToastCenter();
        using var binding = ToastNotifications.Center.Override(center);

        launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
        launch.Harness.Main.Loop.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("bootstrap", launch.Events);
        Assert.Empty(center.Log);
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-067")] // AppModel.swift:1240-1246
    public void Making_a_model_asks_for_nothing_until_the_first_notification_is_wanted() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var center = new OrderedToastCenter();
        using var binding = ToastNotifications.Center.Override(center);
        using var active = NSApplication.IsActive.Override(() => false);

        var model = h.MakeModel();
        h.Main.Loop.RunUntilIdle();
        Assert.Empty(center.Log);

        h.Main.OnMain(() => model.Notify(SomeThread, "Title", "first"));
        h.Main.Loop.RunUntilIdle();

        // The request comes before the toast is added (:1251), at the moment the app first wants to notify.
        Assert.Equal(["permission", "add:first"], center.Log);
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-067")] // AppModel.swift:118, :1240-1246
    public void The_request_is_made_once_whatever_asks_for_it_and_however_many_toasts_follow() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var center = new OrderedToastCenter();
        using var binding = ToastNotifications.Center.Override(center);
        using var active = NSApplication.IsActive.Override(() => false);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.RequestNotificationPermission();
            model.Notify(SomeThread, "Title", "one");
            model.Notify(SomeThread, "Title", "two", sound: false);
            model.RequestNotificationPermission();
        });
        h.Main.Loop.RunUntilIdle();

        Assert.Equal(["permission", "add:one", "add:two"], center.Log);
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-067")] // AppModel.swift:1240-1246, :1249-1252
    public void A_toast_for_a_thread_in_view_asks_for_nothing_and_the_first_toast_that_is_shown_does() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("permission"));
        var visible = AppModelSamples.Thread(project);
        var hidden = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [visible, hidden]);
        var center = new OrderedToastCenter();
        using var binding = ToastNotifications.Center.Override(center);
        using var active = NSApplication.IsActive.Override(() => true);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.SelectedThreadID = visible.Id);

        h.Main.OnMain(() => model.Notify(visible.Id, "Title", "in view"));
        h.Main.Loop.RunUntilIdle();
        Assert.Empty(center.Log);

        h.Main.OnMain(() => model.Notify(hidden.Id, "Title", "out of view"));
        h.Main.Loop.RunUntilIdle();
        Assert.Equal(["permission", "add:out of view"], center.Log);
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-067")] // AppModel.swift:1240-1246
    public void A_refusal_is_swallowed_is_not_asked_again_and_later_toasts_are_still_added() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var center = new OrderedToastCenter { FailPermission = true };
        using var binding = ToastNotifications.Center.Override(center);
        using var active = NSApplication.IsActive.Override(() => false);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            model.Notify(SomeThread, "Title", "one");
            model.Notify(SomeThread, "Title", "two");
        });
        h.Main.Loop.RunUntilIdle();

        Assert.Equal(["permission", "add:one", "add:two"], center.Log);
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-067")] // AppModel.swift:118, :1240-1246
    public void Each_model_asks_once_for_itself() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var center = new OrderedToastCenter();
        using var binding = ToastNotifications.Center.Override(center);
        var first = h.MakeModel();
        var second = h.MakeModel();

        h.Main.OnMain(() =>
        {
            first.RequestNotificationPermission();
            first.RequestNotificationPermission();
            second.RequestNotificationPermission();
            second.RequestNotificationPermission();
        });

        Assert.Equal(2, center.Permissions);
    });
}
