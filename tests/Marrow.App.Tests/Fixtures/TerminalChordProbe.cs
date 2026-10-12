using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Web W4: a real terminal view (no shell: its input is a pipe the test reads) and a text box in one headless window that
/// carries the app's chord dispatch. The dispatch takes every chord it is offered, or none, as the test says; what it was
/// offered and what the terminal sent to its shell are read back. Helper types live here, never inside a test class.
/// </summary>
public sealed class TerminalChordProbe
{
    private readonly ProcessPipe pipe;

    private TerminalChordProbe(AvaloniaWindow window, TerminalHostView view, TextBox box, ProcessPipe pipe, List<KeyboardShortcut?> offered)
    {
        Window = window;
        Terminal = view;
        Box = box;
        Offered = offered;
        this.pipe = pipe;
    }

    public AvaloniaWindow Window { get; }

    public TerminalHostView Terminal { get; }

    public TextBox Box { get; }

    /// <summary>Every chord the window's dispatch was offered, in order.</summary>
    public List<KeyboardShortcut?> Offered { get; }

    /// <summary>What the terminal sent its shell so far (the pipe is then closed, so read it once).</summary>
    public byte[] Sent()
    {
        Dispatcher.UIThread.RunJobs();
        pipe.CloseWriting();
        return pipe.ReadDataToEndOfFile();
    }

    public void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None, string? symbol = null)
    {
        Window.KeyPress(key, modifiers, physical, symbol);
        Window.KeyRelease(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Runs <paramref name="body"/> against a fresh probe (on the UI thread), then closes it.</summary>
    public static void Run(bool takesEverything, Action<TerminalChordProbe> body)
    {
        var pipe = new ProcessPipe();
        var view = new TerminalHostView { Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular), OptionAsMetaKey = true };
        var input = typeof(TerminalHostView).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)!;
        input.SetValue(view, pipe);
        var box = new TextBox();
        var window = new AvaloniaWindow { Width = 640, Height = 360, Content = new StackPanel { Children = { box, new Border { Height = 240, Child = view } } } };
        var offered = new List<KeyboardShortcut?>();
        using var dispatch = MenuKeyEquivalents.Attach(window, (positional, _) =>
        {
            offered.Add(positional);
            return takesEverything;
        });
        try
        {
            window.Show();
            window.UpdateLayout();
            body(new TerminalChordProbe(window, view, box, pipe, offered));
        }
        finally
        {
            window.Close();
            input.SetValue(view, null);
            pipe.CloseWriting();
            typeof(ProcessPipe).GetMethod("CloseReadEndInParent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pipe, null);
        }
    }
}
