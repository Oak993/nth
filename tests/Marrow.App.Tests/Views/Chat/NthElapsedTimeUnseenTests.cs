using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-6 (e): the working line's elapsed time does not tick while its window cannot be seen, and shows the current
// time again when it is seen. Headless, Debug, a manual clock.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class NthElapsedTimeUnseenTests(HeadlessSession headless)
{
    // Text changes in 10 simulated seconds while unseen. Measured (Debug, headless): 10 before the fix, so the
    // first limit was 15; 0 after, the limit now.
    private const int UnseenTicks = 0;

    [Fact]
    public void The_elapsed_time_stops_ticking_while_unseen_and_shows_the_current_time_when_seen() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        WindowPresence.TestFactory = w => new WindowPresence(w, true);
        var elapsed = new NthElapsedTime(SwiftDate.Now, null, true);
        var window = new Avalonia.Controls.Window { Width = 120, Height = 40, Content = elapsed };
        void Advance(double seconds)
        {
            for (var at = 0.0; at < seconds - 1e-9; at += .25)
            {
                time.Advance(TimeSpan.FromSeconds(.25));
                Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            }
        }
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Advance(2);
            Assert.Equal(RelativeTime.Duration(TimeSpan.FromSeconds(2)), elapsed.Text);
            var presence = WindowPresence.For(elapsed)!;
            presence.SetSeen(false); Dispatcher.UIThread.RunJobs();
            var changes = 0; var last = elapsed.Text;
            for (var step = 0; step < 40; step++)
            {
                Advance(.25);
                if (elapsed.Text != last) { changes++; last = elapsed.Text; }
            }
            Assert.InRange(changes, 0, UnseenTicks);
            presence.SetSeen(true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(RelativeTime.Duration(TimeSpan.FromSeconds(12)), elapsed.Text);
            // shown at once (the digits do not roll from the old time: as a window that kept ticking would show it)
            Assert.Null(Assert.IsType<Marrow.UI.Port.Nth.NthLettersState>(elapsed.Letters.Snapshot().State).Previous);
            Advance(1);
            Assert.Equal(RelativeTime.Duration(TimeSpan.FromSeconds(13)), elapsed.Text);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null; }
    });
}
