using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Window;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.DigestRigs;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-11 (the lead runs it alone, in a quiet window, on a real Avalonia platform with a real HWND; every unit filter
// excludes Native). The headless platform has a manual frame clock and a scale of 1, so two things only settle here. First, the digest card in a real window: it draws inside
// its frame, the lane's rows do not overlap, and answering a suggestion turns its row into the line that took its place with the fade really running on the real clock
// (it ends at full opacity) while nothing above the lane moves. Second, a digest composed while the window is closed (the hidden window of AS-10) is on the card of the
// window that comes back. A Windows toast cannot be observed from a test: that nothing toasts when the Morning digest is composed is the owner's look at the notification
// area on the final installer (ED-NEW-dextra-as11-12 proves the structure).
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-dextra-as11-11")]
[Trait("Feature", "AS-11")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class DigestNativeTests(WindowManagerNativeSession native)
{
    private static ChatHabit Habit(string key, string label)
    {
        DateTimeOffset Friday(int weeksAgo) => new DateTimeOffset(2026, 10, 9, 16, 5, 0, TimeSpan.Zero).AddDays(-7 * weeksAgo);
        return new ChatHabit("app:" + key, label, MissionRig.Project, "App", [Friday(2), Friday(1), Friday(0)]);
    }

    [Fact]
    public void The_card_in_a_real_window_draws_inside_its_frame_and_an_answered_row_fades_in_on_the_real_clock_without_moving_what_is_above_it()
    {
        native.Run(subject =>
        {
            _ = subject;
            using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings"), Habit("b", "File the invoices")], logins: () => ["Channel B"]));
            _ = rig.Host.Digests!.Compose(DigestKind.Scheduled);
            MissionRig.Until(() => rig.Host.Digests.Latest is not null, "the digest");
            var (window, card, model) = native.OnUi(() =>
            {
                var made = new DigestModel(rig.Host.Digests, () => TimeZoneInfo.Utc) { OpenLink = _ => { } };
                var shown = new DigestCard(made);
                var host = new AvaloniaWindow { Content = shown, Width = 520, Height = 760, ShowInTaskbar = false };
                native.Register(host);
                host.Show();
                return (host, shown, made);
            });
            WindowManagerNativeMethods.Until(() => native.OnUi(() => window.IsVisible && card.IsMeasureValid && card.LaneRows.Count == 2), "the card did not show", 10000);

            var before = native.OnUi(() => (Height: card.Bounds.Height, First: card.LaneRows[0].Bounds, Second: card.LaneRows[1].Bounds, Top: card.Links[0].TranslatePoint(new Point(0, 0), window)!.Value));
            Assert.True(native.OnUi(() => card.Bounds.Right <= window.ClientSize.Width + 0.5 && card.Bounds.Bottom <= window.ClientSize.Height + 0.5), "the card is not inside the window");
            Assert.True(before.First.Bottom <= before.Second.Top + 0.5, "two suggestions overlap");

            native.OnUi(() => model.Dismiss(card.LaneRows[0].Key));
            WindowManagerNativeMethods.Until(() => native.OnUi(() => card.LaneRows[0].AcceptButton is null && card.LaneRows[0].Opacity >= 0), "the row did not turn into its answer", 5000);
            // The fade runs on the real clock and ends at full opacity.
            WindowManagerNativeMethods.Until(() => native.OnUi(() => card.LaneRows[0].Children.OfType<StackPanel>().Single().Children[0].Opacity >= 1), "the answer did not fade in", 5000);

            var after = native.OnUi(() => (Height: card.Bounds.Height, Top: card.Links[0].TranslatePoint(new Point(0, 0), window)!.Value, Second: card.LaneRows[1].Bounds));
            Assert.Equal(before.Top, after.Top);                                  // nothing above the lane moved
            Assert.InRange(after.Height, before.Height - 60, before.Height + 4);  // the buttons gave way to one line: the card does not grow
            native.OnUi(() => model.Dispose());
        });
    }

    [Fact]
    public void A_line_the_brain_adds_from_another_thread_shows_on_the_card_without_a_cross_thread_error()
    {
        native.Run(subject =>
        {
            _ = subject;
            using var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings")]));
            _ = rig.Host.Digests!.Compose(DigestKind.Scheduled);
            MissionRig.Until(() => rig.Host.Digests.Latest is not null, "the digest");
            var (window, card) = native.OnUi(() =>
            {
                var shown = new DigestCard(new DigestModel(rig.Host.Digests, () => TimeZoneInfo.Utc));
                var host = new AvaloniaWindow { Content = shown, Width = 520, Height = 760, ShowInTaskbar = false };
                native.Register(host);
                host.Show();
                return (host, shown);
            });
            WindowManagerNativeMethods.Until(() => native.OnUi(() => window.IsVisible && card.LaneRows.Count == 1), "the card did not show", 10000);
            // The test's own thread is not the UI thread: the service says it changed here, and the card must draw on the UI thread.
            Assert.False(rig.Host.Digests.Suggest("dextra", "Back up the invoices?", null).IsError);
            WindowManagerNativeMethods.Until(() => native.OnUi(() => card.LaneRows.Count == 2), "the line the brain added did not show on the card", 5000);
        });
    }

    [Fact]
    public void A_digest_composed_while_the_window_is_closed_is_on_the_card_of_the_window_that_comes_back()
    {
        native.Run(subject =>
        {
            using var rig = Quiet(Setup(logins: () => ["Channel B"]));
            var manager = native.OnUi(() =>
            {
                subject.Main.Hide();
                subject.Settings.Hide();
                var made = new WindowManager
                {
                    Park = WindowManager.ParkKind.Close,
                    MainContent = () => new DigestCard(new DigestModel(rig.Host.Digests!, () => TimeZoneInfo.Utc)),
                };
                var main = made.PrepareMain();
                main.ShowInTaskbar = false;
                native.Register(main);
                made.ShowMain();
                return made;
            });
            WindowManagerNativeMethods.Until(() => native.OnUi(() => manager.MainWindow is { IsVisible: true }), "the window did not show", 10000);
            Assert.True(native.OnUi(manager.ParkMain));

            _ = rig.Host.Digests!.Compose(DigestKind.Scheduled);
            MissionRig.Until(() => rig.Host.Digests.Latest is not null, "the digest composed with the window closed");

            native.OnUi(manager.PrepareMain);
            native.OnUi(manager.ShowMain);
            WindowManagerNativeMethods.Until(() => native.OnUi(() => manager.MainWindow is { IsVisible: true }), "the window did not come back", 10000);
            var card = native.OnUi(() => manager.MainWindow!.GetVisualDescendants().OfType<DigestCard>().Single());
            WindowManagerNativeMethods.Until(() => native.OnUi(() => card.IsVisible && card.Header == rig.Host.Digests.Latest!.Header), "the card did not show the digest", 5000);
            Assert.Equal(["Needs you: nothing.", "Unusual: Your sign-in for Channel B expired."], native.OnUi(() => card.SectionTexts.ToArray()));
            native.OnUi(() => manager.MainWindow!.Hide());
        });
    }
}
