using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class PopoverMotionDismissalTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")]
    public void Reopening_during_custom_close_restarts_open_motion_on_the_existing_surface() => headless.OnUiThread(() =>
    {
        var state = new ShownFlag();
        var anchor = new Border { Width = 20, Height = 20 };
        var motion = new DeferredPopoverMotion();
        var popup = PlainControls.Popover(anchor, new Binding<bool>(() => state.Value, v => state.Value = v), () => new Border { Width = 100, Height = 40 });
        popup.SetValue(Presentation.MotionProperty, motion);
        popup.ShouldUseOverlayLayer = true;
        var window = HeadlessPopupHost.OverlayWindow(new Panel { Children = { anchor, popup } });
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            state.Value = true; Dispatcher.UIThread.RunJobs();
            var surface = popup.Child;
            state.Value = false; Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, motion.Closes);
            Assert.True(popup.IsOpen);
            state.Value = true; Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, motion.Opens);
            Assert.Same(surface, popup.Child);
            Assert.Null(motion.Finished);
            Assert.True(popup.IsOpen);
        }
        finally { popup.IsOpen = false; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Clicking_outside_a_custom_motion_popover_routes_dismissal_through_its_close_motion() => headless.OnUiThread(() =>
    {
        var state = new ShownFlag();
        var anchor = new Border { Width = 20, Height = 20 };
        var outside = new Border { Width = 20, Height = 20 };
        var motion = new DeferredPopoverMotion();
        var popup = PlainControls.Popover(anchor, new Binding<bool>(() => state.Value, v => state.Value = v), () => new Border { Width = 100, Height = 40 });
        popup.SetValue(Presentation.MotionProperty, motion);
        popup.ShouldUseOverlayLayer = true;
        var window = HeadlessPopupHost.OverlayWindow(new StackPanel { Children = { anchor, outside, popup } });
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            state.Value = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(popup.IsOpen);
            Assert.False(popup.IsLightDismissEnabled); // Native instant close must not bypass the locked .22s opacity.
            Assert.Equal(1, motion.Opens);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            outside.RaiseEvent(new PointerPressedEventArgs(outside, pointer, window, new Point(5, 30), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.False(state.Value);
            Assert.True(popup.IsOpen);
            Assert.Equal(1, motion.Closes);
            motion.Finished!();
            Assert.False(popup.IsOpen);
        }
        finally { popup.IsOpen = false; window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
