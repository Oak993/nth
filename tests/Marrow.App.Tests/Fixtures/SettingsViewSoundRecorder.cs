using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

public sealed class SettingsViewSoundRecorder : ISoundOutput
{
    public int Starts { get; private set; }
    public ISoundPlayback? Start(SwiftSound sound, float volume, Action finished) { Starts++; return null; }
}
