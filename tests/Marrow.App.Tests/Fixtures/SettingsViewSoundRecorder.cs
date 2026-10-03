using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

public sealed class SettingsViewSoundRecorder : ISoundOutput
{
    public int Starts { get; private set; }
    /// <summary>Every playback started, with the sound that was played and the volume it was played at: which chime, how loud.</summary>
    public List<(SwiftSound Sound, float Volume)> Plays { get; } = [];
    public ISoundPlayback? Start(SwiftSound sound, float volume, Action finished) { Starts++; Plays.Add((sound, volume)); return null; }
}
