using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-perf-idle-5: "can any window of the app be seen" for the account-limits poll (it is read only while somebody could use the numbers).
// True while no window is tracked, false only when every tracked window is unseen, and the event follows each change and each close.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class WindowPresenceAppSeenTests(HeadlessSession headless)
{
    [Fact]
    public void The_app_is_seen_unless_every_tracked_window_is_unseen_and_the_event_follows_the_changes() => headless.OnUiThread(() =>
    {
        const nint Handle = 0x7B01;
        var window = new Window { Content = new Border() };
        WindowPresence.TestFactory = w => new WindowPresence(w, true, Handle);
        var raised = 0;
        void Count() => raised++;
        try
        {
            var before = WindowPresence.Counts;
            window.Show();
            var presence = WindowPresence.For(window)!;
            WindowPresence.AppSeenChanged += Count;
            Assert.True(WindowPresence.AppSeen);

            presence.SetSeen(false);
            Assert.Equal(1, raised);
            Assert.Equal(before.Seen > 0, WindowPresence.AppSeen); // unseen, unless another tracked window of the process is seen
            presence.SetSeen(false);
            Assert.Equal(1, raised); // no change, no event

            presence.SetSeen(true);
            Assert.Equal(2, raised);
            Assert.True(WindowPresence.AppSeen);

            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, raised); // a closed window leaves the counts: the answer may have changed
            Assert.Equal(before, WindowPresence.Counts);
        }
        finally
        {
            WindowPresence.AppSeenChanged -= Count;
            WindowPresence.TestFactory = null;
            if (window.IsVisible) window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
