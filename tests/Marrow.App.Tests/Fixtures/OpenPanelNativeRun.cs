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

    internal static OpenPanelNativeRun Show(OpenPanel panel) => new(panel);

    /// <summary>Waits for the dialog to close and rethrows what it threw, if anything.</summary>
    internal void Finish()
    {
        Assert.True(thread.Join(15000), "The folder chooser did not close.");
        if (failure is not null) throw new InvalidOperationException("The folder chooser failed.", failure);
    }
}
