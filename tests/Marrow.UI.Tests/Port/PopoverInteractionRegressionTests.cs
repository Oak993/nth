using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class PopoverInteractionRegressionTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-063")]
    public void A_nonclient_press_closes_application_defined_popovers_but_keeps_sheets(bool sheet) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
        var anchor = new Border { Width = 20, Height = 20 };
        var window = HeadlessPopupHost.OverlayWindow(new Panel { Children = { anchor } });
        window.Show(); Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, Child = new Border { Width = 100, Height = 40 }, ShouldUseOverlayLayer = true };
        PopoverHost.SetIsSheet(popup, sheet);
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true); Step(300);
            PopoverHost.DismissOutside(window); Assert.True(popup.IsOpen);
            Step(200); Assert.Equal(sheet, popup.IsOpen);
        }
        finally { popup.IsOpen = false; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_popup_reanchored_while_open_listens_to_its_new_owner_and_releases_the_old_one() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
        var first = new Border { Width = 20, Height = 20 };
        var second = new Border { Width = 20, Height = 20 };
        var firstWindow = HeadlessPopupHost.OverlayWindow(new Panel { Children = { first } });
        var secondWindow = HeadlessPopupHost.OverlayWindow(new Panel { Children = { second } });
        firstWindow.Show(); secondWindow.Show(); Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = first, Child = new Border { Width = 100, Height = 40 }, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true); Step(300);
            Assert.True(popup.IsOpen);
            Presentation.Presenter.Current.SetPresented(popup, false); Step(40);
            popup.PlacementTarget = second;
            Presentation.Presenter.Current.SetPresented(popup, true); Step(300);
            Assert.True(popup.IsOpen);
            firstWindow.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            secondWindow.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.Equal(0, frames.Pending);
            Assert.Null(WindowTransients.Newest(firstWindow));
            Assert.Null(WindowTransients.Newest(secondWindow));
        }
        finally { popup.IsOpen = false; firstWindow.Close(); secondWindow.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-107")]
    public void Reanchoring_preserves_animated_outside_dismissal_and_restores_the_configured_policy(bool closing) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
        var first = new Border { Width = 20, Height = 20 };
        var second = new Border { Width = 20, Height = 20 };
        var outside = new Border { Width = 20, Height = 20, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        var firstWindow = HeadlessPopupHost.OverlayWindow(new Panel { Children = { first } });
        var secondWindow = HeadlessPopupHost.OverlayWindow(new Panel { Children = { second, outside } });
        firstWindow.Show(); secondWindow.Show(); Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = first, Child = new Border { Width = 100, Height = 40 }, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true); Step(300);
            if (closing) { Presentation.Presenter.Current.SetPresented(popup, false); Step(40); }
            popup.PlacementTarget = second;
            Presentation.Presenter.Current.SetPresented(popup, true); Step(300);
            Assert.True(popup.IsOpen); Assert.False(popup.IsLightDismissEnabled);
            secondWindow.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var point = outside.TranslatePoint(new Point(10, 10), secondWindow)!.Value;
            Assert.Same(outside, secondWindow.InputHitTest(point));
            secondWindow.MouseDown(point, MouseButton.Left); secondWindow.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Step(80); Assert.InRange(popup.Child!.Opacity, .1, .9);
            Step(100); Assert.False(popup.IsOpen); Assert.True(popup.IsLightDismissEnabled);
            Assert.Null(WindowTransients.Newest(firstWindow)); Assert.Null(WindowTransients.Newest(secondWindow)); Assert.Equal(0, frames.Pending);
        }
        finally { popup.IsOpen = false; firstWindow.Close(); secondWindow.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Closing_during_open_cancels_the_open_frames_and_keeps_fading_out() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var card = new Border { Width = 100, Height = 100 };
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); }
        PopoverAppear.Play(card, PlacementMode.Top);
        Step(40);
        var closing = false;
        PopoverAppear.Dismiss(card, PlacementMode.Top, () => closing = true);
        Step(80);
        var previous = card.Opacity;
        Step(40);
        Assert.True(card.Opacity < previous, "An old opening frame must not restore opacity during close.");
        Step(50);
        Assert.True(closing);
        Assert.Equal(0, frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Outside_press_keeps_the_default_popover_visible_until_its_close_transition_finishes() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var state = new ShownFlag();
        var anchor = new Border { Width = 20, Height = 20 };
        var outside = new Border { Width = 20, Height = 20, Background = Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        var popup = PlainControls.Popover(anchor, new Binding<bool>(() => state.Value, v => state.Value = v), () => new Border { Width = 100, Height = 40 });
        popup.ShouldUseOverlayLayer = true;
        var window = HeadlessPopupHost.OverlayWindow(new Panel { Children = { anchor, outside, popup } });
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            state.Value = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            clock.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1));
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var point = outside.TranslatePoint(new Point(10, 10), window)!.Value;
            Assert.Same(outside, window.InputHitTest(point));
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(state.Value);
            Assert.True(popup.IsOpen, "Native light dismiss must not cut off the closing frames.");
            clock.Advance(TimeSpan.FromMilliseconds(80)); frames.Advance(TimeSpan.FromMilliseconds(80));
            Assert.InRange(popup.Child!.Opacity, .1, .9);
            clock.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100));
            Assert.False(popup.IsOpen);
        }
        finally { popup.IsOpen = false; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Reopening_during_close_keeps_the_current_pose_and_direct_popovers_receive_Escape() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); }
        var anchor = new Border { Width = 20, Height = 20, Background = Brushes.Transparent };
        var popup = new Popup { PlacementTarget = anchor, Child = new Border { Width = 100, Height = 40 }, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        var window = HeadlessPopupHost.OverlayWindow(new Panel { Children = { anchor, popup } });
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true); Step(1000);
            Assert.Same(popup, WindowTransients.Newest(window));
            Presentation.Presenter.Current.SetPresented(popup, false); Step(80);
            var opacity = popup.Child!.Opacity;
            var scale = ((ScaleTransform)popup.Child.RenderTransform!).ScaleX;
            Presentation.Presenter.Current.SetPresented(popup, true);
            Assert.Equal(opacity, popup.Child.Opacity);
            Assert.Equal(scale, ((ScaleTransform)popup.Child.RenderTransform!).ScaleX);
            Step(1000);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Step(200); Assert.False(popup.IsOpen);
            Assert.Null(WindowTransients.Newest(window));
            Assert.Equal(0, frames.Pending);
        }
        finally { popup.IsOpen = false; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Help_tags_wait_for_a_long_dwell_even_after_another_tag_was_shown() => headless.OnUiThread(() =>
    {
        var button = PlainControls.Button(new TextBlock { Text = "Model" }, () => { });
        ToolTip.SetTip(button, "Choose a model");
        var window = new Window { Content = button };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(1500, ToolTip.GetShowDelay(button));
            Assert.Equal(0, ToolTip.GetBetweenShowDelay(button));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("hide", false)]
    [InlineData("hide", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("anchor", false)]
    [InlineData("anchor", true)]
    [Trait("Decision", "ED-107")]
    public void Removing_the_owner_or_anchor_cancels_every_transition_frame(string removal, bool closing) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); }
        var anchor = new Border { Width = 20, Height = 20 };
        var popup = new Popup { PlacementTarget = anchor, Child = new Border { Width = 100, Height = 40 }, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        var panel = new Panel { Children = { anchor, popup } };
        var window = HeadlessPopupHost.OverlayWindow(panel);
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Presentation.Presenter.Current.SetPresented(popup, true); Step(40);
            if (closing) { Presentation.Presenter.Current.SetPresented(popup, false); Step(40); }
            Assert.True(frames.Pending > 0);
            if (removal == "hide") window.Hide();
            else if (removal == "close") window.Close();
            else panel.Children.Remove(anchor);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.Equal(0, frames.Pending);
            Assert.Null(WindowTransients.Newest(window));
        }
        finally { popup.IsOpen = false; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
