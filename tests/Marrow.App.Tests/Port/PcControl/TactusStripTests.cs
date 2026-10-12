using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the chat's strip (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.7). Closed while the chat has no Tactus
// stream; open at 32 DIP with "Tactus. Using Excel", a chip per app and Pause; Pause holds that chat's apps and the button
// becomes Resume; the asks badge counts the chat's Tactus asks; a chip's x stops that app; the strip closes when the turn
// ends with nothing paused. It opens and closes by its height under a clip, never by a fade.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-27")]
public sealed class TactusStripTests(HeadlessSession headless)
{
    private const string Excel = "exe:c:\\office\\excel.exe";

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void The_strip_follows_the_chats_stream_pauses_its_apps_and_stops_one() => headless.OnUiThread(() =>
    {
        var clock = new PcManualClock();
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var broker = new PcAskBroker(clock, control);
        using var feed = new PcChatFeed(pause, new PcStopControl(control), broker, clock);
        var strip = new TactusStrip("chat-1", () => feed, reduceMotion: () => true);
        var window = new Avalonia.Controls.Window { Width = 800, Height = 200, Content = strip };
        window.Show();
        try
        {
            Assert.False(strip.IsVisible);
            Assert.Null(strip.Shown);

            var joined = pause.Join("chat-1", Excel, new PcWaitState());
            Dispatcher.UIThread.RunJobs();
            Assert.True(strip.IsVisible);
            Assert.Equal(TactusStrip.StripHeight, strip.Height);
            Assert.Equal("Tactus. Using Excel", strip.Line);
            Assert.Equal("Pause", strip.Action.Content);
            Assert.Equal("", strip.AsksText);
            Assert.Equal(1, strip.Opacity);

            Click(strip.Action);
            Assert.True(pause.IsPaused(Excel));
            Assert.Equal("Resume", strip.Action.Content);
            Assert.Equal("Tactus. Excel paused", strip.Line);

            broker.Add(new PcAskDraft(PcAskKind.ShowWindow, "chat-1", new PcShowAsk("Book1 - Excel")) { App = Excel, AppName = "Excel" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1 ask", strip.AsksText);

            Click(strip.Action);
            Assert.False(pause.IsPaused(Excel));

            // a chip's x stops that app (stop path 7)
            using (var watch = control.Watch(CancellationToken.None, "chat-1", Excel, 1))
            {
                Click(Assert.Single(strip.AppButtons));
                Assert.True(watch.Token.IsCancellationRequested);
            }

            // the turn ended with nothing paused: the strip closes
            joined.Dispose();
            pause.EndTurn("chat-1");
            Dispatcher.UIThread.RunJobs();
            Assert.False(strip.IsVisible);
            Assert.Equal(0, strip.Height);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_strip_opens_and_closes_by_its_height_not_a_fade() => headless.OnUiThread(() =>
    {
        var clock = new PcManualClock();
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var broker = new PcAskBroker(clock, control);
        using var feed = new PcChatFeed(pause, new PcStopControl(control), broker, clock);
        var strip = new TactusStrip("chat-1", () => feed, reduceMotion: () => false);
        var window = new Avalonia.Controls.Window { Width = 800, Height = 200, Content = strip };
        window.Show();
        try
        {
            using var joined = pause.Join("chat-1", Excel, new PcWaitState());
            Dispatcher.UIThread.RunJobs();
            var transition = Assert.IsType<Avalonia.Animation.DoubleTransition>(Assert.Single(strip.Transitions!));
            Assert.Equal(Avalonia.Layout.Layoutable.HeightProperty, transition.Property);
            Assert.Equal(TimeSpan.FromSeconds(TactusStrip.OpenSeconds), transition.Duration);
            Assert.True(strip.ClipToBounds);
            Assert.Equal(1, strip.Opacity);
        }
        finally
        {
            window.Close();
        }
    });
}
