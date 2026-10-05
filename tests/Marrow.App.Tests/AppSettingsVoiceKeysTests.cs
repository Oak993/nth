using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests;

// SA-1 (voice-SPEC V-16.1, V-16.2, V-16.4, V-16.5, V-T10 wiring; customization-SPEC CZ-13.3, CZ-13.4, CZ-13.5): the AppSettings keys
// voiceOrb, voiceTyping, voiceHardware, voiceNotes and workingDots, stored as the VT-1 records write them, and the
// write coalescing every one of them (and composerLight) goes through. Built on AppSettingsTests' throwaway settings file.
[Collection("AppSettings")]
public sealed class AppSettingsVoiceKeysTests
{
    private static string Text(object? stored) => Encoding.UTF8.GetString((byte[])stored!);

    private static readonly VoiceHardwareRecord Hardware = new()
    {
        Key = "k1", Origin = HardwareOrigin.Manual, Cpu = "Test CPU", Cores = 8, RamGB = 16, Gpu = "Test GPU", VramGB = 8, FreeVramGB = 6,
        Backend = VoiceBackend.Vulkan, Candidate = "large-v3-turbo-q8_0", Model = "large-v3-turbo-q8_0", Rtf = 0.06, LiveWords = true,
        At = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero), App = "1.0.0",
    };

    [Fact]
    [Trait("Decision", "ED-289")] // ED-289 (11): the AppSettings keys are SA-1's; nothing stored is the VT-1 defaults, and a launch writes nothing
    public void Nothing_stored_is_every_keys_default_and_a_launch_writes_nothing()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal(VoiceOrbSettings.Default, settings.VoiceOrb);
            Assert.Equal(VoiceTypingSettings.Default, settings.VoiceTyping);
            Assert.Null(settings.VoiceHardware);
            Assert.Equal(VoiceNotesSettings.Default, settings.VoiceNotes);
            Assert.Equal(WorkingDotsSettings.Default, settings.WorkingDots);
        });
        Assert.DoesNotContain(scope.Defaults.Writes, w => w.Key is "voiceOrb" or "voiceTyping" or "voiceHardware" or "voiceNotes" or "workingDots");
    }

    [Fact]
    [Trait("Decision", "ED-289")] // V-T10 wiring: each key stores the JSON bytes of its record, under the key name the spec gives
    public void Each_key_stores_its_records_json_under_its_spec_name_and_reads_back_after_a_relaunch()
    {
        using var scope = AppSettingsScope.Create();
        var orb = VoiceOrbSettings.Default with { Orb = VoiceOrbKind.Membrane, OrbSize = 26, Labels = VoicePillWords.L3, LangMode = VoiceLangMode.Ask };
        var typing = VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud, Lang = "hi", Mic = "{0.0.1.00000000}.{abc}", SlowChoice = LiveWordsChoice.Accurate, SlowChoiceKey = "hw-part" };
        var notes = new VoiceNotesSettings(true, false);
        var dots = new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = "teal" };
        scope.OnMain(() =>
        {
            var settings = new AppSettings { VoiceOrb = orb, VoiceTyping = typing, VoiceHardware = Hardware, VoiceNotes = notes, WorkingDots = dots };
            Assert.Equal(Encoding.UTF8.GetString(orb.ToJson()), Text(scope.Defaults.Object("voiceOrb")));
            Assert.Equal(Encoding.UTF8.GetString(typing.ToJson()), Text(scope.Defaults.Object("voiceTyping")));
            Assert.Equal(Encoding.UTF8.GetString(Hardware.ToJson()), Text(scope.Defaults.Object("voiceHardware")));
            Assert.Equal("{\"local\":true,\"cloud\":false}", Text(scope.Defaults.Object("voiceNotes")));
            Assert.Equal("{\"mode\":\"custom\",\"custom\":\"teal\"}", Text(scope.Defaults.Object("workingDots")));
        });
        scope.Relaunch();
        scope.OnMain(() =>
        {
            var again = new AppSettings();
            Assert.Equal(orb, again.VoiceOrb);
            Assert.Equal(typing, again.VoiceTyping);
            Assert.Equal(Hardware, again.VoiceHardware);
            Assert.Equal(notes, again.VoiceNotes);
            Assert.Equal(dots, again.WorkingDots);
        });
    }

    [Fact]
    [Trait("Decision", "ED-289")] // V-16.6: all keys read leniently; unreadable data is the default
    public void Unreadable_data_is_the_default_and_a_cleared_hardware_record_removes_its_key()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            foreach (var key in new[] { "voiceOrb", "voiceTyping", "voiceHardware", "voiceNotes", "workingDots" })
                scope.Defaults.Set(key, Encoding.UTF8.GetBytes("{oops"));
        });
        scope.Relaunch();
        scope.OnMain(() =>
        {
            var settings = new AppSettings();
            Assert.Equal(VoiceOrbSettings.Default, settings.VoiceOrb);
            Assert.Equal(VoiceTypingSettings.Default, settings.VoiceTyping);
            Assert.Null(settings.VoiceHardware);
            Assert.Equal(VoiceNotesSettings.Default, settings.VoiceNotes);
            Assert.Equal(WorkingDotsSettings.Default, settings.WorkingDots);
            settings.VoiceHardware = Hardware;
            Assert.NotNull(scope.Defaults.Object("voiceHardware"));
            settings.VoiceHardware = null;
            Assert.Null(settings.VoiceHardware);
            settings.FlushPendingWrites(); // the removal followed the write within 250 ms, so it waited like any change
            Assert.Null(scope.Defaults.Object("voiceHardware"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T27: a 2 s drag at 60 Hz writes at most 9 times plus once at the end; the in-memory value is current each frame
    [Trait("Spec", "CZ-T27")]
    public void A_two_second_composer_light_drag_writes_at_most_9_times_plus_one_and_the_value_is_current_each_frame()
    {
        using var scope = AppSettingsScope.Create();
        AppSettings? settings = null;
        scope.OnMain(() => settings = new AppSettings());
        const int frames = 120;
        for (var frame = 0; frame < frames; frame++)
        {
            var wanted = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Duration = 3 + frame * 0.01 } };
            scope.OnMain(() =>
            {
                settings!.ComposerLight = wanted;
                Assert.Equal(wanted.Clamped(), settings.ComposerLight); // current at once, whatever the store holds
            });
            scope.Runtime.Advance(TimeSpan.FromSeconds(1.0 / 60));
        }
        var duringDrag = scope.Defaults.Writes.Count(w => w == ("set", "composerLight"));
        Assert.InRange(duringDrag, 2, 9);
        scope.OnMain(() => settings!.FlushPendingWrites()); // the drag ends
        Assert.Equal(duringDrag + 1, scope.Defaults.Writes.Count(w => w == ("set", "composerLight")));
        var last = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Duration = 3 + (frames - 1) * 0.01 } };
        Assert.Equal(Encoding.UTF8.GetString(last.Clamped().ToJson()), Text(scope.Defaults.Object("composerLight")));
        scope.Runtime.AdvanceMilliseconds(1000);
        Assert.Equal(duringDrag + 1, scope.Defaults.Writes.Count(w => w == ("set", "composerLight")));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-13.5: every new key is written at once on its first change, and a second change within 250 ms waits for the interval
    public void Every_new_key_is_coalesced_and_the_in_memory_value_is_current()
    {
        using var scope = AppSettingsScope.Create();
        AppSettings? settings = null;
        scope.OnMain(() => settings = new AppSettings());
        var changes = new (string Key, Action<AppSettings> First, Action<AppSettings> Second)[]
        {
            ("voiceOrb", s => s.VoiceOrb = VoiceOrbSettings.Default with { OrbSize = 26 }, s => s.VoiceOrb = VoiceOrbSettings.Default with { Orb = VoiceOrbKind.Membrane }),
            ("voiceTyping", s => s.VoiceTyping = VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud }, s => s.VoiceTyping = VoiceTypingSettings.Default with { Lang = "fr" }),
            ("voiceHardware", s => s.VoiceHardware = Hardware, s => s.VoiceHardware = Hardware with { Cores = 12 }),
            ("voiceNotes", s => s.VoiceNotes = new VoiceNotesSettings(true, false), s => s.VoiceNotes = new VoiceNotesSettings(true, true)),
            ("workingDots", s => s.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model }, s => s.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Custom, Custom = "red" }),
        };
        foreach (var (key, first, second) in changes)
        {
            scope.OnMain(() => first(settings!));
            Assert.Equal(1, scope.Defaults.Writes.Count(w => w == ("set", key)));
            scope.Runtime.AdvanceMilliseconds(100);
            scope.OnMain(() => second(settings!));
            Assert.Equal(1, scope.Defaults.Writes.Count(w => w == ("set", key))); // held back
            scope.Runtime.AdvanceMilliseconds(150);
            Assert.Equal(2, scope.Defaults.Writes.Count(w => w == ("set", key))); // written when the interval ended
        }
        scope.OnMain(() =>
        {
            Assert.Equal(VoiceOrbKind.Membrane, settings!.VoiceOrb.Orb);          // the in-memory values are the second changes
            Assert.Equal("fr", settings.VoiceTyping.Lang);
            Assert.Equal(12, settings.VoiceHardware!.Cores);
            Assert.Equal(new VoiceNotesSettings(true, true), settings.VoiceNotes);
            Assert.Equal(new WorkingDotsSettings { Mode = WorkingDotsMode.Custom, Custom = "red" }, settings.WorkingDots);
        });
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-13.4: "Spoken language" writes voiceOrb.langMode and voiceTyping.lang = null together; both reach the store in the same turn
    public void The_spoken_language_pair_of_keys_reaches_the_store_together()
    {
        using var scope = AppSettingsScope.Create();
        AppSettings? settings = null;
        scope.OnMain(() => settings = new AppSettings());
        scope.OnMain(() => { settings!.VoiceOrb = settings.VoiceOrb with { LangMode = VoiceLangMode.Show }; settings.VoiceTyping = settings.VoiceTyping with { Lang = "ur" }; });
        scope.Runtime.AdvanceMilliseconds(20);
        scope.OnMain(() =>
        {
            var orb = VoiceLanguage.WithLanguageDefault(settings!.VoiceOrb, settings.VoiceTyping, VoiceLangMode.Windows);
            settings.VoiceOrb = orb.Orb;
            settings.VoiceTyping = orb.Typing;
        });
        Assert.Contains("\"langMode\":\"show\"", Text(scope.Defaults.Object("voiceOrb")), StringComparison.Ordinal);
        scope.Runtime.AdvanceMilliseconds(250);
        Assert.Contains("\"langMode\":\"windows\"", Text(scope.Defaults.Object("voiceOrb")), StringComparison.Ordinal);
        Assert.Contains("\"lang\":null", Text(scope.Defaults.Object("voiceTyping")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-13.5: held values are also written on app exit
    public void Held_values_are_written_when_the_app_exits()
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        var appDelegate = new AppDelegate { Model = model };
        h.Main.OnMain(() =>
        {
            model.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Model };
            model.Settings.WorkingDots = WorkingDotsSettings.Default with { Mode = WorkingDotsMode.Custom, Custom = "green" };
        });
        Assert.Equal("{\"mode\":\"model\",\"custom\":\"blue\"}", Text(h.Defaults.Object("workingDots")));
        h.Main.OnMain(appDelegate.ApplicationWillTerminate);
        Assert.Equal("{\"mode\":\"custom\",\"custom\":\"green\"}", Text(h.Defaults.Object("workingDots")));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-13.3: Core cannot see the UI palette, so its list of custom ids is pinned to AccentPalette here
    public void The_custom_colour_ids_Core_knows_are_the_accent_palettes_ids_in_order() =>
        Assert.Equal(Marrow.UI.Port.AccentPalette.All.Select(swatch => swatch.Id), WorkingDotsSettings.KnownCustomIds);
}
