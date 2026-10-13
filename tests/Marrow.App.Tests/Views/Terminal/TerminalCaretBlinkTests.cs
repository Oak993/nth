using System.Reflection;
using System.Text;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// UI-TERM-010 (added to the record by rec-settings-win): the terminal's block caret blinks while the terminal has the keyboard. SwiftTerm Mac/MacCaretView.swift:104-123 runs an
// opacity animation (1 to 0 and back, 0.7 s each way, forever) while the caret is shown in a focused terminal and none otherwise; the catalogue measured it on the Mac app
// (terminal.block-cursor-blink, 2-chat-use.mov S 132.237-134.31, red channel 208 peak, 20 off at 132.908, 245 at 133.55-133.64, 20 at 134.308): a near-linear fade out over
// about 0.64 s, a near-linear fade in over about 0.64 s, a hold of about 0.1 s, a cycle of about 1.4 s; unfocused the caret is a dim outline and does not blink. The frame
// clock and the clock are driven by hand.
[Collection("Headless")]
public sealed class TerminalCaretBlinkTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Req", "UI-TERM-010")] // the catalogue's measured cycle: down 0.64 s, up 0.64 s, hold to 1.4 s
    [InlineData(0.0, 1.0)]
    [InlineData(0.32, 0.5)]
    [InlineData(0.64, 0.0)]
    [InlineData(0.96, 0.5)]
    [InlineData(1.28, 1.0)]
    [InlineData(1.34, 1.0)]
    [InlineData(1.4, 1.0)]
    [InlineData(1.72, 0.5)]
    [InlineData(2.04, 0.0)]
    public void The_caret_fades_out_and_in_linearly_over_a_cycle_of_one_point_four_seconds(double seconds, double opacity) =>
        Assert.Equal(opacity, TerminalSurface.BlinkOpacity(seconds), 6);

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacCaretView.swift:104-123 (the animation runs while the caret is shown in a focused terminal)
    public void A_focused_terminal_blinks_its_caret_from_full_opacity_on_the_compositor_and_an_unfocused_one_does_not()
    {
        Run((view, window, time, frames, other) =>
        {
            var loop = new RecordingOpacityLoop();
            using var driver = OpacityLoops.Driver.Override(loop);
            var surface = (TerminalSurface)view.Child!;
            Assert.False(surface.IsBlinking);
            Assert.True(view.Focus());
            Assert.True(surface.IsBlinking);
            Assert.Equal(1, surface.CaretOpacity); // from full opacity
            var start = Assert.Single(loop.Starts);
            Assert.Contains(start.Target, Avalonia.VisualTree.VisualExtensions.GetVisualChildren(surface));
            Assert.Equal(TerminalSurface.BlinkCycle, start.Duration);
            Assert.Null(start.Ease); // linear
            // the key frames are the catalogue's cycle, down 0.64 s, up 0.64 s and a hold to 1.4 s ...
            foreach (var seconds in new[] { 0.0, 0.32, 0.64, 0.96, 1.28, 1.34, 1.399 })
                Assert.Equal(TerminalSurface.BlinkOpacity(seconds), RecordingOpacityLoop.At(start.Keys, seconds / 1.4), 3);
            // ... and nothing on main moves it: no frame is asked for, or delivered, in four simulated seconds
            for (var i = 0; i < 40; i++) Step(time, frames, 0.1);
            Assert.Equal(0, frames.Pending);
            Assert.Equal(0, frames.Delivered);
            Assert.Equal(1, surface.CaretOpacity);

            Assert.True(other.Focus()); // the keyboard goes elsewhere
            Dispatcher.UIThread.RunJobs();
            Assert.False(surface.IsBlinking);
            var stop = Assert.Single(loop.Stops);
            Assert.Equal(1, stop.Opacity);
            Assert.Equal(1, surface.CaretOpacity); // the outline stands still
            Step(time, frames, 0.32);
            Assert.Equal(1, surface.CaretOpacity);
            Assert.Equal(0, frames.Pending);

            Assert.True(view.Focus()); // focused again: from the start of the cycle
            Assert.True(surface.IsBlinking);
            Assert.Equal(1, surface.CaretOpacity);
            Assert.Equal(2, loop.Starts.Count);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // a window that is not the active one keeps the block steady, and the blink comes back with the window
    public void The_caret_blinks_only_in_the_active_window()
    {
        Run((view, window, time, frames, other) =>
        {
            var loop = new RecordingOpacityLoop();
            using var driver = OpacityLoops.Driver.Override(loop);
            var surface = (TerminalSurface)view.Child!;
            Assert.True(view.Focus());
            Assert.True(surface.IsBlinking);

            // headless Activate never deactivates the window that was in front, so the platform's callback is supplied (AppCommandsRig.Deactivate)
            AppCommandsRig.Deactivate(window);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsActive, "the window is deactivated");
            Assert.False(surface.IsBlinking, "a deactivated window shows a steady block");
            Assert.Equal(1, surface.CaretOpacity);
            Assert.Single(loop.Stops);
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsActive);
            Assert.True(surface.IsBlinking);
            Assert.Equal(2, loop.Starts.Count);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void A_caret_that_takes_the_keyboard_before_it_has_a_composition_visual_starts_its_blink_after_the_next_frames()
    {
        Run((view, window, time, frames, other) =>
        {
            var loop = new RecordingOpacityLoop { HasVisual = false };
            using var driver = OpacityLoops.Driver.Override(loop);
            var surface = (TerminalSurface)view.Child!;
            Assert.True(view.Focus());
            Assert.False(surface.IsBlinking);
            Assert.Empty(loop.Starts);
            loop.HasVisual = true; // the first render has made the element's visual
            Dispatcher.UIThread.RunJobs();
            Assert.True(surface.IsBlinking);
            Assert.Single(loop.Starts);
            Assert.Equal(0, frames.Pending);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // Terminal.swift DECTCEM (CSI ? 25 l hides the caret): MacCaretView is hidden, nothing animates
    public void A_hidden_caret_does_not_blink_and_a_shown_one_starts_again()
    {
        Run((view, window, time, frames, other) =>
        {
            var surface = (TerminalSurface)view.Child!;
            Assert.True(view.Focus());
            Assert.True(surface.IsBlinking);
            Output(view, "\u001b[?25l");
            Assert.False(surface.IsBlinking);
            Assert.Equal(1, surface.CaretOpacity);
            Output(view, "\u001b[?25h");
            Assert.True(surface.IsBlinking);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:1256-1258 and :3271-3273: a terminal view that is focused keeps showing its I-beam; the caret layer takes no pointer
    public void The_caret_layer_is_a_visual_of_the_surface_that_takes_no_pointer_input()
    {
        Run((view, window, time, frames, other) =>
        {
            var surface = (TerminalSurface)view.Child!;
            Assert.Equal(2, Avalonia.VisualTree.VisualExtensions.GetVisualChildren(surface).Count()); // the text control and the caret
            Assert.Equal("Ibeam", surface.Cursor!.ToString());
        });
    }

    private void Run(Action<TerminalHostView, AvaloniaWindow, ManualTimeProvider, ManualFrameSource, Avalonia.Controls.Button> action) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var view = new TerminalHostView
        {
            Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular),
            NativeForegroundColor = new SwiftColor(1, 1),
            NativeBackgroundColor = new SwiftColor(0, 1),
            CaretColor = new SwiftColor(0, 1),
        };
        var other = new Avalonia.Controls.Button { Width = 10, Height = 10, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var window = new AvaloniaWindow { Width = 640, Height = 240, Content = new Avalonia.Controls.Grid { Children = { view, other } } };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            action(view, window, time, frames, other);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void Step(ManualTimeProvider time, ManualFrameSource frames, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Output(TerminalHostView view, string text)
    {
        typeof(TerminalHostView).GetMethod("OnOutput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, [Encoding.UTF8.GetBytes(text)]);
        Dispatcher.UIThread.RunJobs();
    }
}
