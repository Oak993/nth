using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>What a Voice typing page test sees: the page, the model behind it, the scripted microphones and the clocks (VT-13), and the Speech model section's controller and backend (VT-14).</summary>
internal sealed record VoiceTypingScene(VoiceTypingSettingsPage Page, AppModel Model, ICaptureHost Host, Avalonia.Controls.Window Window, ManualFrameSource Frames, TestRuntime Runtime,
    SpeechModelController Controller, ISpeechModelBackend Backend)
{
    /// <summary>Moves the clock and the frame source on, so a transition reaches its target.</summary>
    public void Advance(double milliseconds)
    {
        Runtime.AdvanceMilliseconds(milliseconds);
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Runs the UI thread's jobs until <paramref name="condition"/> holds: the probe, the downloads and the self-test finish on other threads and post their news here. Bounded, so work that
    /// never finishes fails the test instead of hanging it.
    /// </summary>
    public void Until(Func<bool> condition)
    {
        var bound = System.Diagnostics.Stopwatch.StartNew();
        Dispatcher.UIThread.RunJobs();
        while (!condition())
        {
            if (bound.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("the condition never held");
            Thread.Sleep(1);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Runs what was posted to the UI thread (device notices, tracked settings).</summary>
    public static void Sync() => Dispatcher.UIThread.RunJobs();
}
