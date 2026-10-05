using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// UI-TERM-010: "mouse selection uses SwiftTerm's own handling". The production terminal view (TerminalHostView over its cell surface), raw pointer events
// through a real window, and the bytes that reach the ConPTY input pipe (the production ProcessPipe write boundary). The expected bytes and ranges are written
// out by hand from SwiftTerm 1.20.0: MacTerminalView.swift:2878-3012 (mouseDown, mouseUp, mouseDragged), :3135-3158 (mouseMoved), :3193-3270 (scrollWheel),
// :3271-3273 (the I-beam), Terminal.swift:6916-6999 (the encoding), SelectionService.swift (the ranges).
[Collection("Headless")]
public sealed class TerminalHostMouseTests(HeadlessSession headless)
{
    private static string Esc(string tail) => "\u001b[" + tail;

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:2895-2909 with SelectionService.swift:663-702
    public void A_double_click_selects_the_word_under_the_pointer_and_a_triple_click_the_line()
    {
        Run((view, window) =>
        {
            Output(view, "hello world.foo-bar x");
            var word = At(view, 8, 0);
            Click(window, word);
            Click(window, word);
            Assert.Equal("world.foo-bar", Selected(view));
            Assert.Equal(TerminalSelectionMode.Word, Surface(view).SelectedCells!.Value.Mode);
            Click(window, word);
            Assert.Equal("hello world.foo-bar x", Selected(view));
            Assert.Equal(TerminalSelectionMode.Row, Surface(view).SelectedCells!.Value.Mode);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:2896-2903: a click drops a selection, a shift click extends it
    public void A_plain_click_drops_the_selection_and_a_shift_click_extends_it()
    {
        Run((view, window) =>
        {
            Output(view, "hello world.foo-bar x");
            Drag(window, At(view, 0, 0, 0.2), At(view, 4, 0, 0.2));
            Assert.Equal("hell", Selected(view));
            Click(window, At(view, 9, 0, 0.2), RawInputModifiers.Shift); // MacTerminalView.swift:2897-2898 shiftExtend
            Assert.Equal("hello wor", Selected(view));
            Click(window, At(view, 15, 0, 0.2)); // :2900 selection.active = false
            Assert.Equal("", Selected(view));
            Assert.Null(Surface(view).SelectedCells);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:2992-3037 with SelectionService.swift:408-444
    public void A_drag_after_a_double_click_keeps_the_word_and_snaps_to_whole_words()
    {
        Run((view, window) =>
        {
            Output(view, "hello world.foo-bar x");
            var word = At(view, 8, 0);
            Click(window, word);
            Press(window, word); // the second click is still down: a drag from it extends the word selection
            window.MouseMove(At(view, 3, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(At(view, 3, 0), MouseButton.Left);
            Assert.Equal("hello world.foo-bar", Selected(view));
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:2887-2890, :2941-2944; Terminal.swift:5520 (1000h), :6968-6971 and :6966-6967
    public void A_program_that_asked_for_the_mouse_is_told_of_the_press_and_the_release_and_nothing_is_selected()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, "hello world");
            Output(view, Esc("?1000h"));
            Click(window, At(view, 4, 2)); // column 4, row 2
            Assert.Equal("", Selected(view));
            Output(view, Esc("?1006h"));
            Click(window, At(view, 4, 2));
        });
        Assert.Equal(Esc("M") + " %#" + Esc("M") + "#%#" + Esc("<0;5;3M") + Esc("<0;5;3m"), Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:2887 (SendsButtonPress is false for X10) with :2941 (SendsButtonRelease is true): SwiftTerm reports the release only
    public void In_X10_mode_the_press_selects_and_only_the_release_is_reported()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, "hello world");
            Output(view, Esc("?9h"));
            Output(view, Esc("?1006h"));
            Click(window, At(view, 4, 0));
        });
        Assert.Equal(Esc("<0;5;1m"), Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:2996-3010 with Terminal.swift:6996-6999
    public void A_drag_is_reported_only_to_a_program_that_asked_for_button_tracking()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, Esc("?1006h"));
            Output(view, Esc("?1000h")); // normal tracking: press and release, no drag
            Drag(window, At(view, 2, 1), At(view, 6, 1));
            Output(view, Esc("?1002h")); // button-event tracking: a drag sends motion with the button down
            Drag(window, At(view, 2, 1), At(view, 6, 1));
        });
        Assert.Equal(Esc("<0;3;2M") + Esc("<0;7;2m") + Esc("<0;3;2M") + Esc("<32;7;2M") + Esc("<0;7;2m"), Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:3135-3158 mouseMoved with Terminal.swift:716-718
    public void A_program_that_asked_for_every_motion_is_told_of_the_pointer_moving_with_no_button_down()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, Esc("?1006h"));
            Output(view, Esc("?1002h"));
            window.MouseMove(At(view, 5, 3), RawInputModifiers.None); // not asked for
            Output(view, Esc("?1003h"));
            window.MouseMove(At(view, 7, 4), RawInputModifiers.None);
        });
        Assert.Equal(Esc("<35;8;5M"), Encoding.Latin1.GetString(bytes)); // a release, 3, plus the motion bit, 32
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:2991-2993 shiftBypassesMouseReporting; Terminal.swift:6895-6905 and :6938-6940
    public void Shift_selects_instead_unless_the_program_asked_for_shift_clicks_and_then_the_shift_bit_is_sent()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, "hello world");
            Output(view, Esc("?1006h"));
            Output(view, Esc("?1000h"));
            Drag(window, At(view, 0, 0, 0.2), At(view, 4, 0, 0.2), RawInputModifiers.Shift);
            Assert.Equal("hell", Selected(view)); // selected, nothing reported
            Output(view, Esc(">1s"));
            Click(window, At(view, 4, 2), RawInputModifiers.Shift);
        });
        Assert.Equal(Esc("<4;5;3M") + Esc("<4;5;3m"), Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:1178-1186 allowMouseReporting
    public void With_mouse_reporting_not_allowed_the_pointer_selects_whatever_the_program_asked()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, "hello world");
            Output(view, Esc("?1000h"));
            ((TerminalSurface)view.Child!).AllowMouseReporting = false;
            Drag(window, At(view, 0, 0, 0.2), At(view, 4, 0, 0.2));
            Assert.Equal("hell", Selected(view));
        });
        Assert.Empty(bytes);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:3204, :3243-3254 with Terminal.swift:6925-6930
    public void The_wheel_is_reported_as_buttons_4_and_5_once_per_line_to_a_program_that_asked_for_the_mouse()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, string.Join("\r\n", Enumerable.Range(0, 60)));
            Output(view, Esc("?1006h"));
            Output(view, Esc("?1000h"));
            window.MouseWheel(At(view, 4, 2), new Vector(0, 1));
            window.MouseWheel(At(view, 4, 2), new Vector(0, -3));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, HistoryOffset(view)); // reported, not scrolled
            Output(view, Esc("?1000l"));
            window.MouseWheel(At(view, 4, 2), new Vector(0, 2));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, HistoryOffset(view)); // with none asked for it scrolls the history
        });
        Assert.Equal(Esc("<64;5;3M") + Esc("<65;5;3M") + Esc("<65;5;3M") + Esc("<65;5;3M"), Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // MacTerminalView.swift:3271-3273 resetCursorRects: the I-beam over the whole body (the catalogue's pointer.ibeam-text)
    public void The_pointer_is_the_I_beam_over_the_terminal_body()
    {
        Run((view, window) => Assert.Equal("Ibeam", ((TerminalSurface)view.Child!).Cursor!.ToString()));
    }

    private void Run(Action<TerminalHostView, AvaloniaWindow> action) => Capture(action);

    private byte[] Capture(Action<TerminalHostView, AvaloniaWindow> action)
    {
        byte[] result = [];
        headless.OnUiThread(() =>
        {
            var pipe = new ProcessPipe();
            // The production view paints its whole body with the theme background, which is what the pointer hits (the painter fills every cell).
            var view = new TerminalHostView
            {
                Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular),
                NativeForegroundColor = new SwiftColor(1, 1),
                NativeBackgroundColor = new SwiftColor(0, 1),
                CaretColor = new SwiftColor(0, 1),
            };
            var input = typeof(TerminalHostView).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)!;
            input.SetValue(view, pipe);
            var window = new AvaloniaWindow { Width = 640, Height = 240, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.True(view.Focus());
                action(view, window);
                Dispatcher.UIThread.RunJobs();
                pipe.CloseWriting();
                result = pipe.ReadDataToEndOfFile();
            }
            finally
            {
                window.Close();
                input.SetValue(view, null);
                pipe.CloseWriting();
                typeof(ProcessPipe).GetMethod("CloseReadEndInParent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pipe, null);
            }
        });
        return result;
    }

    private static TerminalSurface Surface(TerminalHostView view) => (TerminalSurface)view.Child!;

    private static string Selected(TerminalHostView view) => Surface(view).TextControl.SelectedText;

    private static Size Cell(TerminalHostView view) =>
        (Size)typeof(TerminalHostView).GetMethod("MeasureCell", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;

    // A point inside the cell at (column, row), counted from the top left of the surface, which is the top left of the window.
    private static Point At(TerminalHostView view, int column, int row, double within = 0.5)
    {
        var cell = Cell(view);
        return new Point((column + within) * cell.Width, (row + 0.5) * cell.Height);
    }

    private static void Press(AvaloniaWindow window, Point at, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseMove(at, modifiers);
        window.MouseDown(at, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(AvaloniaWindow window, Point at, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Press(window, at, modifiers);
        window.MouseUp(at, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Drag(AvaloniaWindow window, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Press(window, from, modifiers);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton | modifiers);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(to, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Output(TerminalHostView view, string text)
    {
        typeof(TerminalHostView).GetMethod("OnOutput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, [Encoding.UTF8.GetBytes(text)]);
        Dispatcher.UIThread.RunJobs();
    }

    private static int HistoryOffset(TerminalHostView view) =>
        (int)view.Child!.GetType().GetField("historyOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view.Child)!;
}
