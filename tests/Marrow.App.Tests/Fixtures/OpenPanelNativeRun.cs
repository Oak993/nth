using System.Runtime.InteropServices;
using System.Text;
using Marrow.App.Port;
using Marrow.Core.Port;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// One showing of the real folder chooser (WindowsOpenPanelRunner) on a thread of its own, for OpenPanelNativeTests: RunModal blocks its caller
// until the dialog closes, so the test drives the dialog from its own thread while this one waits in it.
internal sealed class OpenPanelNativeRun
{
    private readonly Thread thread;
    private Exception? failure;

    private OpenPanelNativeRun(OpenPanel panel)
    {
        var started = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            try
            {
                ThreadId = N.GetCurrentThreadId();
                started.Set();
                Urls = new WindowsOpenPanelRunner().RunModal(panel);
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true, Name = "Open panel under test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(started.Wait(5000));
    }

    /// <summary>The native id of the thread the dialog runs on: the dialog is one of its windows.</summary>
    internal uint ThreadId { get; private set; }

    /// <summary>What the dialog answered: the chosen folders, or null when it was cancelled.</summary>
    internal IReadOnlyList<SwiftURL>? Urls { get; private set; }

    /// <summary>Whether the dialog has closed and the panel has answered.</summary>
    internal bool Done => !thread.IsAlive;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, NativeWindowEnumerator callback, nint parameter);

    /// <summary>The text of a window's own caption.</summary>
    internal static string TitleOf(nint window)
    {
        var text = new StringBuilder(512);
        _ = GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    /// <summary>Every visible top-level window of the dialog thread: the dialog, and anything else that thread put on the screen (an error box).</summary>
    internal List<(nint Handle, string Class, string Title)> VisibleWindows()
    {
        var found = new List<(nint, string, string)>();
        NativeWindowEnumerator callback = (window, _) =>
        {
            if (N.IsWindowVisible(window)) found.Add((window, N.ClassName(window), TitleOf(window)));
            return true;
        };
        _ = N.EnumThreadWindows(ThreadId, callback, 0);
        GC.KeepAlive(callback);
        return found;
    }

    /// <summary>The captions of a window's child windows (a message box's text), joined.</summary>
    internal static string ChildTexts(nint window)
    {
        var texts = new List<string>();
        NativeWindowEnumerator callback = (child, _) =>
        {
            if (TitleOf(child) is { Length: > 0 } text) texts.Add(text);
            return true;
        };
        _ = EnumChildWindows(window, callback, 0);
        GC.KeepAlive(callback);
        return string.Join(" | ", texts);
    }

    /// <summary>
    /// Fails if anything but <paramref name="dialog"/> is on the screen from the dialog thread. What is there is named in the failure (its title and the
    /// text it carries) and closed, so that nothing is left on the desktop.
    /// </summary>
    internal void RequireNoOtherWindow(nint dialog, string when)
    {
        var strays = VisibleWindows().Where(window => window.Handle != dialog).ToList();
        if (strays.Count == 0) return;
        var description = string.Join("; ", strays.Select(window => $"\"{window.Title}\" ({window.Class}): {ChildTexts(window.Handle)}"));
        foreach (var stray in strays) N.Post(stray.Handle, 0x0010, 0, 0); // WM_CLOSE
        Assert.Fail($"Another window is on the screen {when}: {description}");
    }

    internal static OpenPanelNativeRun Show(OpenPanel panel) => new(panel);

    /// <summary>Waits for the dialog to close and rethrows what it threw, if anything.</summary>
    internal void Finish()
    {
        Assert.True(thread.Join(15000), "The folder chooser did not close.");
        if (failure is not null) throw new InvalidOperationException("The folder chooser failed.", failure);
    }
}
