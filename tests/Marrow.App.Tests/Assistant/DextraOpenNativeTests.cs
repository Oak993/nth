using Avalonia.Controls;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.HomeSamples;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Assistant;

// Lead only: compile here, never run in CU-DX-OPEN. Real Avalonia window, fake missions, no provider/device calls.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-dextra-open-3")]
public sealed class DextraOpenNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    public void Digested_done_cards_fade_on_the_native_clock_and_leave_no_click_target()
    {
        native.Run(_ =>
        {
            var now = native.OnUi(() => Marrow.Core.Port.SwiftClock.Provider.Current.GetUtcNow());
            DateTimeOffset? through = null;
            var jobs = new FakeMissionJobs();
            var done = Job("Done · العربية", MissionState.Done) with { Created = now.AddMinutes(-10), Ended = now.AddMinutes(-1) };
            jobs.Set(done);
            var state = native.OnUi(() =>
            {
                var board = new MissionBoard(jobs, digestedThrough: () => through);
                var window = new AvaloniaWindow { Content = new ScrollViewer { Content = board }, Width = 520, Height = 640, ShowInTaskbar = false };
                native.Register(window); window.Show(); board.Refresh();
                return (Board: board, Card: board.Card(done.Id)!, Window: window);
            });
            WindowManagerNativeMethods.Until(() => native.OnUi(() => state.Card.IsMeasureValid && state.Card.Bounds.Width > 0), "card did not draw", 10_000);
            native.OnUi(() => { through = now; state.Board.Refresh(); Assert.False(state.Card.IsHitTestVisible); Assert.Null(state.Board.Card(done.Id)); });
            WindowManagerNativeMethods.Until(() => native.OnUi(() => state.Card.Parent is null), "digested card did not finish its fade", 5000);
            native.OnUi(state.Window.Close);
        });
    }
}
