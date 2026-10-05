// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15, voice-SPEC V-10.5, V-16.2, V-16.4): the settings the preparation reads and writes.
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary>The stored hardware result, the voiceTyping settings, the Customization language default and the Windows display language. Production: <see cref="AppSettingsVoiceAccess"/>.</summary>
public interface IVoicePreparationSettings
{
    VoiceHardwareRecord? Record { get; set; }

    VoiceTypingSettings Typing { get; set; }

    VoiceOrbSettings Orb { get; }

    string? UiLanguage { get; }
}
