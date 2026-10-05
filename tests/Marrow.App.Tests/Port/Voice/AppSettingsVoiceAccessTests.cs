using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// VT-15: the preparation's settings on AppSettings (voiceHardware, voiceTyping, voiceOrb).
[Collection("AppSettings")]
public sealed class AppSettingsVoiceAccessTests
{
    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void The_record_and_the_typing_settings_read_and_write_through_AppSettings_and_the_orb_is_read()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var settings = new AppSettings { VoiceOrb = VoiceOrbSettings.Default with { LangMode = VoiceLangMode.Ask } };
            var access = new AppSettingsVoiceAccess(settings);
            Assert.Null(access.Record);
            var record = new VoiceHardwareRecord { Key = "k", Model = "base.en", LiveWords = true };
            access.Record = record;
            Assert.Equal(record, settings.VoiceHardware);
            Assert.Equal(record, access.Record);
            var typing = VoiceTypingSettings.Default with { Model = "base.en" };
            access.Typing = typing;
            Assert.Equal(typing, settings.VoiceTyping);
            Assert.Equal(VoiceLangMode.Ask, access.Orb.LangMode);
            Assert.Equal(System.Globalization.CultureInfo.CurrentUICulture.Name, access.UiLanguage);
        });
    }
}
