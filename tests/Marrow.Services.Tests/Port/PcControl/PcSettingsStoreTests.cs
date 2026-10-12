// PORT-ONLY: Tactus (ED-NEW-pcctl-40; DESIGN.md 4.7, 10.10). Tactus's settings in nth's settings store: off, confirmations on
// and the real mouse and keyboard off when nothing is saved; every value read back as written; mistyped values and a store
// that throws read as the defaults (Tactus off).
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-40")]
public sealed class PcSettingsStoreTests
{
    [Fact]
    public void An_empty_store_reads_as_off_with_confirmations_on_and_real_input_off()
    {
        var settings = PcSettingsStore.Read(new InMemorySettingsStore());

        Assert.False(settings.Enabled);
        Assert.True(settings.ConfirmGuardedActions);
        Assert.False(settings.RealInput);
        Assert.True(settings.ShowPointer);
        Assert.False(settings.KeepScreenshots);
        Assert.False(settings.LogTypedText);
        Assert.False(settings.FastHelper);
        Assert.Equal(PcDetail.Standard, settings.Detail);
        Assert.Empty(settings.UserDeniedApps);
        Assert.Equal("Ctrl+Alt+Esc", settings.StopChord);
        Assert.Equal("Ctrl+Alt+Space", settings.PauseChord);
        Assert.Equal("", settings.AnswerChord);
    }

    [Fact]
    public void Every_setting_reads_back_as_written()
    {
        var store = new InMemorySettingsStore();
        var written = new PcSettings
        {
            Enabled = true,
            ConfirmGuardedActions = false,
            ShowPointer = false,
            Detail = PcDetail.High,
            ReadTextFromImages = true,
            LogTypedText = true,
            KeepScreenshots = true,
            RealInput = true,
            FastHelper = true,
            UserDeniedApps = ["slack.exe", "outlook.exe"],
            StopChord = "Ctrl+F12",
            PauseChord = "Ctrl+Alt+P",
            AnswerChord = "Ctrl+Alt+A",
        };

        PcSettingsStore.Write(store, written);
        var read = PcSettingsStore.Read(store);

        Assert.Equal(written with { UserDeniedApps = read.UserDeniedApps }, read);
        Assert.Equal(new[] { "slack.exe", "outlook.exe" }, read.UserDeniedApps);
        Assert.Equal("pcControl.enabled", PcSettingsStore.Enabled);
        Assert.Equal("pcControl.stopChord", PcSettingsStore.StopChord);
        Assert.Equal("pcControl.pauseChord", PcSettingsStore.PauseChord);
        Assert.Equal("pcControl.answerChord", PcSettingsStore.AnswerChord);
    }

    [Fact]
    public void The_production_store_keeps_the_denied_apps_list_and_the_switch()
    {
        using var folder = new PcTestFolder("pcsettings-");
        var path = Path.Combine(folder.Path, "UserDefaults.json");
        var store = new SettingsStore(path);

        PcSettingsStore.Write(store, PcSettings.Default with { Enabled = true, UserDeniedApps = ["teams.exe"] });
        var read = PcSettingsStore.Read(store);

        Assert.True(read.Enabled);
        Assert.Equal("teams.exe", Assert.Single(read.UserDeniedApps));
        Assert.False(read.RealInput);
    }

    [Fact]
    public void Mistyped_values_read_as_the_defaults()
    {
        var store = new InMemorySettingsStore();
        store.Set(PcSettingsStore.Enabled, "yes");
        store.Set(PcSettingsStore.RealInput, 1L);
        store.Set(PcSettingsStore.ConfirmGuarded, "off");
        store.Set(PcSettingsStore.Detail, "ultra");
        store.Set(PcSettingsStore.DeniedApps, "notepad.exe");
        store.Set(PcSettingsStore.StopChord, "");

        var settings = PcSettingsStore.Read(store);

        Assert.False(settings.Enabled);
        Assert.False(settings.RealInput);
        Assert.True(settings.ConfirmGuardedActions);
        Assert.Equal(PcDetail.Standard, settings.Detail);
        Assert.Empty(settings.UserDeniedApps);
        Assert.Equal("Ctrl+Alt+Esc", settings.StopChord);
    }

    [Fact]
    public void A_store_that_throws_reads_as_tactus_off()
    {
        var settings = PcSettingsStore.Read(new ThrowingSettingsStore());

        Assert.False(settings.Enabled);
        Assert.Equal(PcSettings.Default, settings);
    }
}
