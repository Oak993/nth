using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Toasts are suppressed while the Assistant home or its pane is on screen", critique 17: ToastNotifications
// .IsSurfaceOnScreen; ED-NEW-dextra-as9-3): the non-thread twin of the chat check. The Assistant home is on screen when the app is active and the
// home is in the main area; picking a chat leaves it. The delegate asks once more right before the Windows call, so a home opened between the
// request and the call still wins; a chat toast is untouched.
[Collection("Headless")]
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-3")]
public sealed class DecisionToastSurfaceTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void The_home_is_on_screen_only_when_the_app_is_active_and_the_home_is_in_the_main_area(bool active, bool shown, bool expected) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var activity = NSApplication.IsActive.Override(() => active);
        h.Main.OnMain(() =>
        {
            if (shown) model.ShowAssistantHome();
            Assert.Equal(expected, ToastNotifications.IsSurfaceOnScreen(model, AppSurface.AssistantHome));
        });
    });

    [Fact]
    public void Picking_a_chat_takes_the_home_off_the_screen_and_the_chat_check_is_unchanged() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        using var activity = NSApplication.IsActive.Override(() => true);
        h.Main.OnMain(() =>
        {
            model.ShowAssistantHome();
            Assert.True(ToastNotifications.IsSurfaceOnScreen(model, AppSurface.AssistantHome));
            Assert.False(ToastNotifications.IsOnScreen(model, thread));
            model.SelectedThreadID = thread.Id;
            Assert.False(ToastNotifications.IsSurfaceOnScreen(model, AppSurface.AssistantHome));
            Assert.True(ToastNotifications.IsOnScreen(model, thread));
        });
    });

    [Fact]
    public void An_unknown_surface_is_never_on_screen_and_a_missing_model_is_refused() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var activity = NSApplication.IsActive.Override(() => true);
        h.Main.OnMain(() => Assert.False(ToastNotifications.IsSurfaceOnScreen(model, (AppSurface)99)));
        Assert.Throws<ArgumentNullException>(() => ToastNotifications.IsSurfaceOnScreen(null!, AppSurface.AssistantHome));
    });

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void A_decision_toast_is_not_presented_while_the_home_is_in_front_and_is_otherwise(bool active, bool shown, bool presented) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var activity = NSApplication.IsActive.Override(() => active);
        h.Main.OnMain(() => { if (shown) model.ShowAssistantHome(); });
        var info = new Dictionary<string, object> { ["assistantDecision"] = Guid.NewGuid().ToString("D"), ["action"] = "open", ["nonce"] = "n" };
        var options = h.Main.Loop.Run(() => new AppDelegate { Model = model }.UserNotificationCenter(new UNNotification(info)), AppModelHarness.Bound);
        Assert.Equal(presented ? UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound : UNNotificationPresentationOptions.None, options);
    });

    [Fact]
    public void A_decision_toast_before_the_model_exists_is_presented() => headless.OnUiThread(() =>
    {
        using var runtime = new Marrow.Testing.TestRuntime();
        var info = new Dictionary<string, object> { ["assistantDecision"] = Guid.NewGuid().ToString("D") };
        var options = runtime.Loop.Run(() => new AppDelegate().UserNotificationCenter(new UNNotification(info)));
        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    });
}
