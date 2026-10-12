using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Web W4: a real terminal view (no shell: its input is a pipe the test reads) hosted as the app hosts it, and a text box, in one
/// headless window that carries the app's own chord dispatch over a real model with a selected thread. What the commands did and
/// what the terminal sent its shell are read back. Helper types live here, never inside a test class.
/// </summary>
public sealed class TerminalChordProbe : IDisposable
{
    private readonly ProcessPipe pipe = new();
    private readonly FieldInfo input = typeof(TerminalHostView).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly IDisposable dispatch;
    private readonly IDisposable shortcuts;

    public TerminalChordProbe()
    {
        H = new AppModelHarness();
        // Every chord at its default, whatever the machine's settings say (the harness binds the settings the store reads).
        shortcuts = ShortcutStore.Shared.Override((ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
        Model = H.MakeModel();
        H.Main.OnMain(() =>
        {
            var project = Model.AddProject(new Project("Chords", H.NewFolder("chords")).Url);
            Model.SelectedThreadID = Model.NewThread(project)!.Id;
        });
        Terminal = new TerminalHostView { Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular), OptionAsMetaKey = true };
        input.SetValue(Terminal, pipe);
        Box = new TextBox();
        var hosted = new TerminalHostContainer { HostedView = Terminal, Height = 240 };
        Window = new AvaloniaWindow { Width = 640, Height = 360, Content = new StackPanel { Children = { Box, hosted } } };
        dispatch = MenuKeyEquivalents.Attach(Window, (positional, typed, scope) => new AppCommands(Model).PerformKeyDown(positional, typed, scope));
        Window.Show();
        Window.UpdateLayout();
    }

    public AppModelHarness H { get; }

    public AppModel Model { get; }

    public ThreadRuntime Runtime => Model.Runtime(Model.SelectedThreadID!.Value);

    public AvaloniaWindow Window { get; }

    public TerminalHostView Terminal { get; }

    public TextBox Box { get; }

    /// <summary>What the terminal sent its shell so far (the pipe is then closed, so read it once).</summary>
    public byte[] Sent()
    {
        Dispatcher.UIThread.RunJobs();
        pipe.CloseWriting();
        return pipe.ReadDataToEndOfFile();
    }

    /// <summary>A key down and up on the window, on the main actor (the commands it runs change observable state).</summary>
    public void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None, string? symbol = null)
    {
        H.Main.OnMain(() =>
        {
            Window.KeyPress(key, modifiers, physical, symbol);
            Window.KeyRelease(key, modifiers, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        });
    }

    public void Dispose()
    {
        H.Main.OnMain(() =>
        {
            Window.Close();
            input.SetValue(Terminal, null);
        });
        pipe.CloseWriting();
        typeof(ProcessPipe).GetMethod("CloseReadEndInParent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pipe, null);
        dispatch.Dispose();
        shortcuts.Dispose();
        H.Dispose();
    }
}
