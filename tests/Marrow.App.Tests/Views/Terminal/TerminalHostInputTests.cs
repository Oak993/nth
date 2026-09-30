using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

// UNVERIFIED drafts: these exercise routed keys and the production ProcessPipe write
// boundary without a shell or provider. Owner deferred all execution to Claude.
[Collection("Headless")]
public sealed class TerminalHostInputTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm AppleTerminalView.swift:2570-2600; KittyKeyboardEncoder.swift:572-767.
    public void Unselected_control_C_and_D_backtab_and_function_keys_reach_the_input_pipe()
    {
        var bytes = Capture((view, window) =>
        {
            Press(window, Key.C, RawInputModifiers.Control);
            Press(window, Key.D, RawInputModifiers.Control);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Press(window, Key.F1);
            Press(window, Key.F12, RawInputModifiers.Control);
            view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "e\U00000301\U0001f642" });
        });
        Assert.Equal(Encoding.UTF8.GetBytes("\U00000003\U00000004\U0000001b[Z\U0000001bOP\U0000001b[24;5~e\U00000301\U0001f642"), bytes);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm Terminal.swift:5236-5237,5470-5471; KittyKeyboardEncoder.swift:650-690.
    public void Split_child_mode_sequences_change_the_next_routed_navigation_key()
    {
        var bytes = Capture((view, window) =>
        {
            Press(window, Key.Up);
            Output(view, "\U0000001b[?");
            Output(view, "1h");
            Press(window, Key.Up);
            Press(window, Key.Home);
            Press(window, Key.Left, RawInputModifiers.Control);
            Output(view, "\U0000001b[?1l");
            Press(window, Key.End);
        });
        Assert.Equal("\U0000001b[A\U0000001bOA\U0000001bOH\U0000001b[1;5D\U0000001b[F"u8.ToArray(), bytes);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm MacTerminalView.swift:1925-1931,2723-2727; ED-050 Ctrl+V.
    public void Clipboard_paste_preserves_Unicode_and_newlines_inside_child_requested_brackets()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, "\U0000001b[?2004h");
            var write = window.Clipboard!.SetTextAsync("a\r\nb\n\U0001f642");
            HeadlessSession.RunDispatcherUntil(write, TimeSpan.FromSeconds(5));
            write.GetAwaiter().GetResult();
            Press(window, Key.V, RawInputModifiers.Control);
            var paste = Assert.IsAssignableFrom<Task>(Field(view, "pendingPaste"));
            HeadlessSession.RunDispatcherUntil(paste, TimeSpan.FromSeconds(5));
            paste.GetAwaiter().GetResult();
        });
        Assert.Equal(Encoding.UTF8.GetBytes("\U0000001b[200~a\r\nb\n\U0001f642\U0000001b[201~"), bytes);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm MacTerminalView.swift:1871-1875,1925-1931.
    public void Typed_text_is_never_bracketed_and_reset_mode_restores_plain_paste()
    {
        var bytes = Capture((view, _) =>
        {
            Output(view, "\U0000001b[?2004h");
            view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "typed" });
            view.PasteText("");
            view.PasteText("one\ntwo");
            Output(view, "\U0000001b[?2004l");
            view.PasteText("plain\r\n");
        });
        Assert.Equal("typed\U0000001b[200~\U0000001b[201~\U0000001b[200~one\ntwo\U0000001b[201~plain\r\n"u8.ToArray(), bytes);
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm MacTerminalView.swift:1583-1593; AppleTerminalView.swift:2710-2744.
    public void Page_keys_scroll_normal_history_and_input_reveals_an_offscreen_caret()
    {
        var bytes = Capture((view, window) =>
        {
            Output(view, string.Join("\r\n", Enumerable.Range(0, 75)));
            Press(window, Key.PageUp);
            Assert.Equal(25, HistoryOffset(view));
            Press(window, Key.PageUp);
            Assert.Equal(50, HistoryOffset(view));
            Press(window, Key.PageDown);
            Assert.Equal(25, HistoryOffset(view));
            Press(window, Key.Up);
            Assert.Equal(0, HistoryOffset(view));
            Output(view, "\U0000001b[?1h");
            Press(window, Key.PageUp);
            Output(view, "\U0000001b[?1l\U0000001b[?1049h");
            Press(window, Key.PageDown);
        });
        Assert.Equal("\U0000001b[A\U0000001b[5~\U0000001b[6~"u8.ToArray(), bytes);
    }

    private byte[] Capture(Action<TerminalHostView, Window> action)
    {
        byte[] result = [];
        headless.OnUiThread(() =>
        {
            var pipe = new ProcessPipe();
            var view = new TerminalHostView
            {
                Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular),
                OptionAsMetaKey = true,
            };
            var input = typeof(TerminalHostView).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)!;
            input.SetValue(view, pipe);
            var window = new Window { Width = 640, Height = 240, Content = view };
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

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers);
        window.KeyRelease(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Output(TerminalHostView view, string text)
    {
        typeof(TerminalHostView).GetMethod("OnOutput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, [Encoding.UTF8.GetBytes(text)]);
        Dispatcher.UIThread.RunJobs();
    }

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static int HistoryOffset(TerminalHostView view) => (int)Field(view.Child!, "historyOffset")!;
}
