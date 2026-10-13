using Marrow.App.Assistant;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1 "Toasts are suppressed while the Assistant home ... is on screen", "Duty opt-in"; ED-NEW-dextra-as9-3, -9): the delegate asks
// once more, right before the Windows call, whether the home is in front, for a duty's toast as for a decision's.
[Collection("Headless")]
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-9")]
public sealed class DutyToastSurfaceTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void A_duty_toast_is_not_presented_while_the_home_is_in_front_and_is_otherwise(bool active, bool shown, bool presented) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        using var activity = NSApplication.IsActive.Override(() => active);
        h.Main.OnMain(() => { if (shown) model.ShowAssistantHome(); });
        var options = h.Main.Loop.Run(() => new AppDelegate { Model = model }.UserNotificationCenter(new UNNotification(new Dictionary<string, object> { [DecisionToastKeys.Mission] = "b0000000-0000-4000-8000-000000000001", [DecisionToastKeys.Action] = "open" })), AppModelHarness.Bound);
        Assert.Equal(presented ? UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound : UNNotificationPresentationOptions.None, options);
    });

    [Fact]
    public void A_duty_toast_before_the_model_exists_is_presented() => headless.OnUiThread(() =>
    {
        using var runtime = new Marrow.Testing.TestRuntime();
        var info = new Dictionary<string, object> { [DecisionToastKeys.Mission] = Guid.NewGuid().ToString("D") };
        var options = runtime.Loop.Run(() => new AppDelegate().UserNotificationCenter(new UNNotification(info)));
        Assert.Equal(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound, options);
    });
}
