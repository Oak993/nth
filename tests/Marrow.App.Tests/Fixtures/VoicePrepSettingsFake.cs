using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Fixtures;

/// <summary>VT-15: the preparation's settings in memory; every write is counted.</summary>
public sealed class VoicePrepSettingsFake : IVoicePreparationSettings
{
    private VoiceHardwareRecord? record;
    private VoiceTypingSettings typing = VoiceTypingSettings.Default;

    public int RecordWrites { get; private set; }

    public VoiceHardwareRecord? Record
    {
        get => record;
        set { record = value; RecordWrites++; }
    }

    public VoiceTypingSettings Typing
    {
        get => typing;
        set => typing = value;
    }

    public VoiceOrbSettings Orb { get; set; } = VoiceOrbSettings.Default;

    public string? UiLanguage { get; set; }
}
