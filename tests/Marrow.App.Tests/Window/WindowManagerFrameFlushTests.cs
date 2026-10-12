using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;

namespace Marrow.App.Tests.Window;

// ED-NEW-perf-startup-structure-1: a move or size drag raises PositionChanged at the display rate and used to rewrite the whole settings file for each one. The main window now
// saves its frame at most every half second; the held frame is written when the window is hidden and when the app ends (AppDelegate.ApplicationWillTerminate).
[Collection("Headless")]
public sealed class WindowManagerFrameFlushTests(HeadlessSession headless)
{
    private const string FrameName = "DroppyCodeMainWindow";

    private static int FrameWrites(AppModelHarness harness) => harness.Defaults.Writes.Count(key => key == FrameName);

    private static IReadOnlyDictionary<string, object> Saved(AppModelHarness harness) =>
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(harness.Defaults.Object(FrameName));

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void A_burst_of_moves_writes_far_fewer_times_than_it_moves_and_FlushPendingFrames_saves_the_last_frame() => WindowManagerMutationScope.Run(headless, (harness, manager) =>
    {
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        WindowManagerMutationScope.Settle(window);
        manager.FlushPendingFrames();
        harness.Defaults.Writes.Clear();
        const int moves = 40;
        for (var i = 0; i < moves; i++)
        {
            window.Position = new PixelPoint(300 + i, 200 + i);
            WindowManagerMutationScope.Settle(window);
        }
        Assert.True(FrameWrites(harness) <= 8, $"a {moves}-move burst wrote the frame {FrameWrites(harness)} times");
        manager.FlushPendingFrames();
        Assert.Equal(300L + moves - 1, Saved(harness)["x"]);
        Assert.Equal(200L + moves - 1, Saved(harness)["y"]);
        var after = FrameWrites(harness);
        manager.FlushPendingFrames();
        Assert.Equal(after, FrameWrites(harness)); // nothing is held any more
    });

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void Hiding_the_main_window_writes_the_frame_it_was_holding() => WindowManagerMutationScope.Run(headless, (harness, manager) =>
    {
        manager.ShowMain();
        var window = Assert.IsType<ThreadWindow>(manager.MainWindow);
        WindowManagerMutationScope.Settle(window);
        manager.FlushPendingFrames();
        window.Position = new PixelPoint(411, 322);
        WindowManagerMutationScope.Settle(window);
        window.Position = new PixelPoint(412, 323);
        WindowManagerMutationScope.Settle(window);
        window.Hide();
        WindowManagerMutationScope.Settle(window);
        Assert.Equal(412L, Saved(harness)["x"]);
        Assert.Equal(323L, Saved(harness)["y"]);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-1")] // ED-NEW-perf-startup-structure-1
    public void The_app_ending_flushes_the_window_frames_before_the_settings_are_written_out()
    {
        var calls = MethodCalls.Of(typeof(AppDelegate).GetMethod(nameof(AppDelegate.ApplicationWillTerminate))!).Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var frames = calls.IndexOf("WindowManager.FlushPendingFrames");
        Assert.True(frames >= 0, "ApplicationWillTerminate never flushes the window frames: " + string.Join(", ", calls));
        Assert.True(frames < calls.IndexOf("AppSettings.FlushPendingWrites"), "before the settings the drags held");
    }
}
