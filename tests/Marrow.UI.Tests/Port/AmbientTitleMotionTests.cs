using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
[Trait("Decision", "ED-248")]
public sealed class AmbientTitleMotionTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decorative_title_stops_after_idle_and_while_unavailable_and_eases_back_without_replaying_the_gap(bool popupHost) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        AmbientWindowState? state = null;
        AmbientWindowState.TestFactory = w => { state = new(w, true); state.Touch(0); return state; };
        var label = new MorphingTextBlock { Text = "Extra High", DriftPeriod = 7, Foreground = new LinearGradientBrush { GradientStops = { new(Colors.Red, 0), new(Colors.Blue, 1) } } };
        var anchor = new Button();
        var window = new Window { Width = 220, Height = 100, Content = popupHost ? anchor : label };
        PopupRoot? root = null;
        void Step(double seconds) { clock.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.FromSeconds(seconds)); Dispatcher.UIThread.RunJobs(); }
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (popupHost) { root = HeadlessPopupHost.Open(new Popup { PlacementTarget = anchor }, window, label); root.Show(); Dispatcher.UIThread.RunJobs(); }
            for (var i = 0; i < 1035; i++) Step(.02);
            Assert.NotNull(state); Assert.Equal(0, frames.Pending);
            var phase = label.DriftPhase; Step(30); Assert.Equal(phase, label.DriftPhase);
            if (root is not null) { root.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right }); Assert.Equal(clock.Elapsed.TotalSeconds, state!.LastInput); }
            else state!.Touch(clock.Elapsed.TotalSeconds);
            Dispatcher.UIThread.RunJobs(); Step(.02);
            Assert.InRange(label.DriftPhase - phase, 0, .001);
            for (var i = 0; i < 30; i++) Step(.02);
            Assert.True(label.DriftPhase > phase);
            state.SetAvailable(false); Dispatcher.UIThread.RunJobs(); Step(.02);
            Assert.Equal(0, frames.Pending); phase = label.DriftPhase;
            Step(30); Assert.Equal(phase, label.DriftPhase);
            state.SetAvailable(true); state.Touch(clock.Elapsed.TotalSeconds); Dispatcher.UIThread.RunJobs(); Step(.02);
            Assert.InRange(label.DriftPhase - phase, 0, .001);
        }
        finally { root?.Hide(); window.Close(); Dispatcher.UIThread.RunJobs(); AmbientWindowState.TestFactory = null; }
        Assert.Equal(0, clock.ActiveTimers);
    });
}
