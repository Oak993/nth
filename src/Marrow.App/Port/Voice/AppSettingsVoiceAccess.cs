// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15): the preparation's settings on the app's AppSettings.
using System.Globalization;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary><see cref="IVoicePreparationSettings"/> over <see cref="AppSettings"/> (voiceHardware, voiceTyping, voiceOrb) and the Windows display language.</summary>
public sealed class AppSettingsVoiceAccess(AppSettings settings) : IVoicePreparationSettings
{
    public VoiceHardwareRecord? Record
    {
        get => settings.VoiceHardware;
        set => settings.VoiceHardware = value;
    }

    public VoiceTypingSettings Typing
    {
        get => settings.VoiceTyping;
        set => settings.VoiceTyping = value;
    }

    public VoiceOrbSettings Orb => settings.VoiceOrb;

    public string? UiLanguage => CultureInfo.CurrentUICulture.Name;
}
