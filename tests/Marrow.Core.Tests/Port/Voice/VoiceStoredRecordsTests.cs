using System.Text;
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0092, OD-0097, OD-0099 Q-V-19 B and Q-V-1 C): the voiceTyping record, voiceHardware and voiceNotes, and their JSON.
public sealed class VoiceStoredRecordsTests
{
    private static VoiceTypingSettings Read(string json) => VoiceTypingSettings.FromJson(Encoding.UTF8.GetBytes(json));

    private static string Json(VoiceTypingSettings s) => Encoding.UTF8.GetString(s.ToJson());

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.2: {"engine":"local","lang":null,"model":null}
    public void The_defaults_write_the_exact_bytes_of_V_16_2()
    {
        var d = VoiceTypingSettings.Default;
        Assert.Equal((VoiceEngine.Local, (string?)null, (string?)null), (d.Engine, d.Lang, d.Model));
        Assert.Null(d.Mic);
        Assert.Null(d.SlowChoice);
        Assert.Equal("voiceTyping", VoiceTypingSettings.StoreKey);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"engine\":\"local\",\"lang\":null,\"model\":null}"), d.ToJson());
        Assert.Equal(d, Read("{\"engine\":\"local\",\"lang\":null,\"model\":null}"));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T120")]
    [Trait("Decision", "ED-267")] // OD-0097, voice-SPEC V-16.2: lang null (nothing picked), "auto", a Whisper code, a missing key and an unknown string
    public void Lang_reads_null_auto_a_code_a_missing_key_and_an_unknown_string_as_V_16_2_says()
    {
        Assert.Null(Read("{\"lang\":null}").Lang);
        Assert.Null(Read("{\"engine\":\"cloud\"}").Lang);
        Assert.Equal("auto", Read("{\"lang\":\"auto\"}").Lang);
        foreach (var language in VoiceLanguage.Languages.Where(l => l.Code != "auto"))
            Assert.Equal(language.Code, Read("{\"lang\":\"" + language.Code + "\"}").Lang);
        foreach (var unknown in new[] { "xx", "EN", "English", "", " en", "english", "Auto", "null", "en-US" })
            Assert.Null(Read("{\"lang\":\"" + unknown + "\"}").Lang);
        Assert.Null(Read("{\"lang\":5}").Lang);
        Assert.Null(Read("{\"lang\":true}").Lang);
        Assert.Null(Read("{\"lang\":[\"en\"]}").Lang);
        Assert.Equal("{\"engine\":\"local\",\"lang\":\"auto\",\"model\":null}", Json(VoiceTypingSettings.Default with { Lang = "auto" }));
        Assert.Equal("{\"engine\":\"local\",\"lang\":\"ur\",\"model\":null}", Json(VoiceTypingSettings.Default with { Lang = "ur" }));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.2: engine local | cloud; model a catalogue id or null
    public void Engine_and_model_round_trip_and_an_unknown_value_keeps_the_default()
    {
        var s = VoiceTypingSettings.Default with { Engine = VoiceEngine.Cloud, Lang = "hi", Model = "small-q5_1" };
        Assert.Equal("{\"engine\":\"cloud\",\"lang\":\"hi\",\"model\":\"small-q5_1\"}", Json(s));
        Assert.Equal(s, Read(Json(s)));
        Assert.Equal(VoiceEngine.Local, Read("{\"engine\":\"remote\"}").Engine);
        Assert.Equal(VoiceEngine.Local, Read("{\"engine\":3}").Engine);
        Assert.Null(Read("{\"model\":\"ggml-nope\"}").Model);
        Assert.Null(Read("{\"model\":7}").Model);
        foreach (var file in ModelCatalog.Files) Assert.Equal(file.Id, Read("{\"model\":\"" + file.Id + "\"}").Model);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // OD-0099 Q-V-19 B: the Microphone row; "Windows default" is the first item and the default (null)
    public void The_microphone_pick_is_stored_as_the_device_id_and_null_is_the_Windows_default()
    {
        Assert.Equal("Windows default", VoiceTypingSettings.WindowsDefaultMicrophone);
        const string id = "{0.0.1.00000000}.{8c5f8f5e-1d2e-4a3b-9c7d-0e1f2a3b4c5d}";
        var s = VoiceTypingSettings.Default with { Mic = id };
        Assert.Equal("{\"engine\":\"local\",\"lang\":null,\"model\":null,\"mic\":\"{0.0.1.00000000}.{8c5f8f5e-1d2e-4a3b-9c7d-0e1f2a3b4c5d}\"}", Json(s));
        Assert.Equal(id, Read(Json(s)).Mic);
        Assert.Null(Read("{\"mic\":null}").Mic);
        Assert.Null(Read("{\"mic\":\"\"}").Mic);
        Assert.Null(Read("{\"mic\":4}").Mic);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // OD-0098 / OD-0099 Q-V-1 C: the slow-PC answer is kept on the Voice typing page ("liveWordsChoice")
    public void The_slow_PC_answer_is_kept_as_liveWordsChoice_and_absent_until_asked()
    {
        Assert.Null(VoiceTypingSettings.Default.SlowChoice);
        Assert.DoesNotContain("liveWordsChoice", Json(VoiceTypingSettings.Default), StringComparison.Ordinal);
        var live = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Live };
        var accurate = VoiceTypingSettings.Default with { SlowChoice = LiveWordsChoice.Accurate };
        Assert.Equal("{\"engine\":\"local\",\"lang\":null,\"model\":null,\"liveWordsChoice\":\"live\"}", Json(live));
        Assert.Equal("{\"engine\":\"local\",\"lang\":null,\"model\":null,\"liveWordsChoice\":\"accurate\"}", Json(accurate));
        Assert.Equal(LiveWordsChoice.Live, Read(Json(live)).SlowChoice);
        Assert.Equal(LiveWordsChoice.Accurate, Read(Json(accurate)).SlowChoice);
        Assert.Null(Read("{\"liveWordsChoice\":\"maybe\"}").SlowChoice);
        Assert.Null(Read("{\"liveWordsChoice\":null}").SlowChoice);
        Assert.Null(Read("{\"liveWordsChoice\":1}").SlowChoice);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void The_last_Hindustani_is_written_only_when_set_and_read_only_as_hi_or_ur()
    {
        Assert.Null(VoiceTypingSettings.Default.LastHindustani);
        Assert.Equal("{\"engine\":\"local\",\"lang\":null,\"model\":null,\"lastHindustani\":\"ur\"}", Json(VoiceTypingSettings.Default with { LastHindustani = "ur" }));
        Assert.Equal("{\"engine\":\"local\",\"lang\":null,\"model\":null}", Json(VoiceTypingSettings.Default with { LastHindustani = "en" }));
        Assert.Equal("hi", Read("{\"lastHindustani\":\"hi\"}").LastHindustani);
        Assert.Equal("ur", Read("{\"lastHindustani\":\"ur\"}").LastHindustani);
        foreach (var other in new[] { "\"en\"", "\"HI\"", "\"\"", "null", "7", "true", "[\"hi\"]" }) Assert.Null(Read("{\"lastHindustani\":" + other + "}").LastHindustani);
        var all = new VoiceTypingSettings { Engine = VoiceEngine.Cloud, Lang = "auto", Model = "large-v3-turbo-q8_0", Mic = "device-1", SlowChoice = LiveWordsChoice.Live, SlowChoiceKey = "k", LastHindustani = "hi" };
        Assert.Equal(all, VoiceTypingSettings.FromJson(all.ToJson()));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.6: all keys read leniently; unreadable data as a whole is the default
    public void Unreadable_data_is_the_default_and_every_field_set_together_round_trips()
    {
        foreach (var junk in new[] { "", "{", "[]", "null", "12", "\"local\"", "{\"engine\":" }) Assert.Equal(VoiceTypingSettings.Default, Read(junk));
        var all = new VoiceTypingSettings { Engine = VoiceEngine.Cloud, Lang = "ur", Model = "large-v3-turbo-q8_0", Mic = "device-1", SlowChoice = LiveWordsChoice.Accurate };
        Assert.Equal("{\"engine\":\"cloud\",\"lang\":\"ur\",\"model\":\"large-v3-turbo-q8_0\",\"mic\":\"device-1\",\"liveWordsChoice\":\"accurate\"}", Json(all));
        Assert.Equal(all, VoiceTypingSettings.FromJson(all.ToJson()));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.5: {"local":false,"cloud":false}
    public void The_privacy_notes_shown_default_to_false_for_both_engines_and_read_leniently()
    {
        var d = VoiceNotesSettings.Default;
        Assert.Equal("voiceNotes", VoiceNotesSettings.StoreKey);
        Assert.False(d.Local);
        Assert.False(d.Cloud);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"local\":false,\"cloud\":false}"), d.ToJson());
        Assert.Equal("{\"local\":true,\"cloud\":false}", Encoding.UTF8.GetString((d with { Local = true }).ToJson()));
        Assert.Equal("{\"local\":false,\"cloud\":true}", Encoding.UTF8.GetString((d with { Cloud = true }).ToJson()));
        Assert.Equal(new VoiceNotesSettings(true, true), VoiceNotesSettings.FromJson("{\"local\":true,\"cloud\":true}"u8));
        Assert.Equal(new VoiceNotesSettings(true, false), VoiceNotesSettings.FromJson("{\"local\":true}"u8));
        Assert.Equal(d, VoiceNotesSettings.FromJson("{\"local\":\"yes\",\"cloud\":1}"u8));
        Assert.Equal(d, VoiceNotesSettings.FromJson("[]"u8));
        Assert.Equal(d, VoiceNotesSettings.FromJson(""u8));
    }

    private static VoiceHardwareRecord Sample() => new()
    {
        Key = "0f".PadRight(64, 'a'), Origin = HardwareOrigin.Auto, Cpu = "AMD Ryzen 7 9800X3D 8-Core Processor", Cores = 8, RamGB = 31,
        Gpu = "NVIDIA GeForce RTX 4080", VramGB = 16, FreeVramGB = 14, Backend = VoiceBackend.Vulkan, Candidate = "large-v3-turbo-q8_0",
        Model = "large-v3-turbo-q8_0", Rtf = 0.06, LiveWords = true, At = new DateTimeOffset(2026, 10, 4, 8, 30, 0, TimeSpan.Zero), App = "1.7.0+21",
    };

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.4: the voiceHardware record, key order as written there
    public void The_hardware_record_writes_the_keys_of_V_16_4_in_order_and_reads_them_back()
    {
        Assert.Equal("voiceHardware", VoiceHardwareRecord.StoreKey);
        var expected = "{\"key\":\"0faaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"origin\":\"auto\",\"cpu\":\"AMD Ryzen 7 9800X3D 8-Core Processor\",\"cores\":8,\"ramGB\":31,"
            + "\"gpu\":\"NVIDIA GeForce RTX 4080\",\"vramGB\":16,\"freeVramGB\":14,\"backend\":\"vulkan\",\"candidate\":\"large-v3-turbo-q8_0\",\"model\":\"large-v3-turbo-q8_0\","
            + "\"rtf\":0.06,\"liveWords\":true,\"at\":\"2026-10-04T08:30:00Z\",\"app\":\"1.7.0+21\"}";
        var bytes = Sample().ToJson();
        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
        Assert.Equal(Sample(), VoiceHardwareRecord.FromJson(bytes));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.4: origin auto | manual, backend vulkan | cuda | cpu
    public void Origin_and_backend_use_their_wire_names()
    {
        foreach (var (origin, name) in new[] { (HardwareOrigin.Auto, "auto"), (HardwareOrigin.Manual, "manual") })
            Assert.Contains("\"origin\":\"" + name + "\"", Encoding.UTF8.GetString((Sample() with { Origin = origin }).ToJson()), StringComparison.Ordinal);
        foreach (var (backend, name) in new[] { (VoiceBackend.Vulkan, "vulkan"), (VoiceBackend.Cuda, "cuda"), (VoiceBackend.Cpu, "cpu") })
        {
            var stored = Sample() with { Backend = backend };
            Assert.Contains("\"backend\":\"" + name + "\"", Encoding.UTF8.GetString(stored.ToJson()), StringComparison.Ordinal);
            Assert.Equal(stored, VoiceHardwareRecord.FromJson(stored.ToJson()));
        }
        var manual = Sample() with { Origin = HardwareOrigin.Manual, LiveWords = false, Rtf = 0.4, Cpu = "Intel(R) Core(TM) i5 & \"co\" <x>" };
        Assert.Equal(manual, VoiceHardwareRecord.FromJson(manual.ToJson()));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.6: lenient read; no stored result is null, so the check runs
    public void The_hardware_record_reads_leniently_and_no_data_means_no_result()
    {
        Assert.Null(VoiceHardwareRecord.FromJson(""u8));
        Assert.Null(VoiceHardwareRecord.FromJson("[]"u8));
        Assert.Null(VoiceHardwareRecord.FromJson("{"u8));
        Assert.Null(VoiceHardwareRecord.FromJson("null"u8));
        var sparse = VoiceHardwareRecord.FromJson("{\"key\":\"abc\",\"origin\":\"weird\",\"cores\":\"eight\",\"backend\":\"metal\",\"at\":\"yesterday\"}"u8)!;
        Assert.Equal("abc", sparse.Key);
        Assert.Equal(HardwareOrigin.Auto, sparse.Origin);
        Assert.Equal(VoiceBackend.Cpu, sparse.Backend);
        Assert.Equal(0, sparse.Cores);
        Assert.Equal(DateTimeOffset.UnixEpoch, sparse.At);
        Assert.False(sparse.LiveWords);
    }
}
