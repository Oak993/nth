using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// ED-NEW-session-hotkeys: SessionHotkeysWindowTests' thread window with the Ctrl-hold seams driven by hand.
internal sealed class SessionHotkeysRig : IDisposable
{
    private readonly TestRuntime runtime = new();
    private readonly IDisposable actor;
    private readonly IDisposable keyState;

    public SessionHotkeysRig()
    {
        actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        keyState = ThreadWindow.ControlKeyState.Override(() => ControlDown);
        Window = new ThreadWindow { Width = 400, Height = 300 };
        Window.ShowSessionNumbers = shown => Calls.Add(shown);
        Window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    public ThreadWindow Window { get; }

    /// <summary>The keyboard's real Ctrl state the safety net reads.</summary>
    public bool? ControlDown { get; set; }

    /// <summary>Every ShowSessionNumbers call, in order.</summary>
    public List<bool> Calls { get; } = [];

    public bool Shown => Window.AreSessionNumbersShown;

    public void Advance(double milliseconds)
    {
        Dispatcher.UIThread.RunJobs();
        runtime.Time.Advance(TimeSpan.FromMilliseconds(milliseconds), () => Dispatcher.UIThread.RunJobs());
        Dispatcher.UIThread.RunJobs();
    }

    public void ControlPress(RawInputModifiers extra = RawInputModifiers.None)
    {
        ControlDown = true;
        Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control | extra, PhysicalKey.ControlLeft, null);
        Dispatcher.UIThread.RunJobs();
    }

    public void ControlRelease()
    {
        ControlDown = false;
        Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
    }

    public void Dispose()
    {
        Window.Close();
        Dispatcher.UIThread.RunJobs();
        keyState.Dispose();
        actor.Dispose();
        runtime.Dispose();
    }
}
