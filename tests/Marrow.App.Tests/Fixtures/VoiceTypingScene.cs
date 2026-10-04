using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>What a Voice typing page test sees: the page, the model behind it, the scripted microphones and the clocks (VT-13).</summary>
internal sealed record VoiceTypingScene(VoiceTypingSettingsPage Page, AppModel Model, ICaptureHost Host, Avalonia.Controls.Window Window, ManualFrameSource Frames, TestRuntime Runtime)
{
    /// <summary>Moves the clock and the frame source on, so a transition reaches its target.</summary>
    public void Advance(double milliseconds)
    {
        Runtime.AdvanceMilliseconds(milliseconds);
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Runs what was posted to the UI thread (device notices, tracked settings).</summary>
    public static void Sync() => Dispatcher.UIThread.RunJobs();
}
