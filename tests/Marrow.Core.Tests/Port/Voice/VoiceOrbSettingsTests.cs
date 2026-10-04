using System.Text;
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0091 sixth amendment, OD-0097): the voiceOrb settings and their JSON. The default bytes are voice-SPEC V-16.1's own line.
public sealed class VoiceOrbSettingsTests
{
    private const string DefaultJson = "{\"orb\":\"siri\",\"lstyle\":\"pill\",\"orbSize\":32,\"attach\":\"in\",\"afterVoice\":\"leave\",\"labels\":\"L1\",\"lightVoice\":\"away\",\"langMode\":\"auto\"}";

    private static VoiceOrbSettings Read(string json) => VoiceOrbSettings.FromJson(Encoding.UTF8.GetBytes(json));

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // OD-0091 sixth amendment, OD-0097; voice-SPEC V-16.1
    public void The_defaults_are_the_locked_ones_and_write_the_exact_bytes_of_V_16_1()
    {
        var d = VoiceOrbSettings.Default;
        Assert.Equal((VoiceOrbKind.Siri, VoiceListeningStyle.Pill, 32, VoiceAttachment.Inside, VoiceAfterVoice.Leave),
            (d.Orb, d.ListeningStyle, d.OrbSize, d.Attachment, d.AfterVoice));
        Assert.Equal((VoicePillWords.L1, VoiceLightVoice.Away, VoiceLangMode.Auto), (d.Labels, d.LightVoice, d.LangMode));
        Assert.Equal("voiceOrb", VoiceOrbSettings.StoreKey);
        Assert.Equal(Encoding.UTF8.GetBytes(DefaultJson), d.ToJson());
        Assert.Equal(d, Read(DefaultJson));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.1 key names and values (OD-0097: labels, lightVoice, langMode)
    public void Every_value_writes_its_wire_name_and_reads_back()
    {
        var cases = new (VoiceOrbSettings Settings, string Fragment)[]
        {
            (VoiceOrbSettings.Default with { Orb = VoiceOrbKind.Membrane }, "\"orb\":\"membrane\""),
            (VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Cross }, "\"lstyle\":\"cross\""),
            (VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Float }, "\"lstyle\":\"float\""),
            (VoiceOrbSettings.Default with { OrbSize = 26 }, "\"orbSize\":26"),
            (VoiceOrbSettings.Default with { Attachment = VoiceAttachment.Outside }, "\"attach\":\"out\""),
            (VoiceOrbSettings.Default with { AfterVoice = VoiceAfterVoice.Send }, "\"afterVoice\":\"send\""),
            (VoiceOrbSettings.Default with { Labels = VoicePillWords.L2 }, "\"labels\":\"L2\""),
            (VoiceOrbSettings.Default with { Labels = VoicePillWords.L3 }, "\"labels\":\"L3\""),
            (VoiceOrbSettings.Default with { Labels = VoicePillWords.L4 }, "\"labels\":\"L4\""),
            (VoiceOrbSettings.Default with { Labels = VoicePillWords.L5 }, "\"labels\":\"L5\""),
            (VoiceOrbSettings.Default with { Labels = VoicePillWords.L6 }, "\"labels\":\"L6\""),
            (VoiceOrbSettings.Default with { LightVoice = VoiceLightVoice.Dim }, "\"lightVoice\":\"dim\""),
            (VoiceOrbSettings.Default with { LightVoice = VoiceLightVoice.Stay }, "\"lightVoice\":\"stay\""),
            (VoiceOrbSettings.Default with { LangMode = VoiceLangMode.Show }, "\"langMode\":\"show\""),
            (VoiceOrbSettings.Default with { LangMode = VoiceLangMode.Windows }, "\"langMode\":\"windows\""),
            (VoiceOrbSettings.Default with { LangMode = VoiceLangMode.Ask }, "\"langMode\":\"ask\""),
        };
        foreach (var (settings, fragment) in cases)
        {
            var json = Encoding.UTF8.GetString(settings.ToJson());
            Assert.Contains(fragment, json, StringComparison.Ordinal);
            Assert.Equal(settings, Read(json));
        }
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.1 / V-16.6: every combination of values survives a write and a read
    public void Every_combination_of_one_value_per_key_round_trips()
    {
        var count = 0;
        foreach (var orb in Enum.GetValues<VoiceOrbKind>())
        foreach (var style in Enum.GetValues<VoiceListeningStyle>())
        foreach (var size in new[] { 32, 26 })
        foreach (var attach in Enum.GetValues<VoiceAttachment>())
        foreach (var after in Enum.GetValues<VoiceAfterVoice>())
        foreach (var labels in Enum.GetValues<VoicePillWords>())
        foreach (var light in Enum.GetValues<VoiceLightVoice>())
        foreach (var mode in Enum.GetValues<VoiceLangMode>())
        {
            var s = new VoiceOrbSettings { Orb = orb, ListeningStyle = style, OrbSize = size, Attachment = attach, AfterVoice = after, Labels = labels, LightVoice = light, LangMode = mode };
            Assert.Equal(s, VoiceOrbSettings.FromJson(s.ToJson()));
            count++;
        }
        Assert.Equal(2 * 3 * 2 * 2 * 2 * 6 * 3 * 4, count);
    }

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.1 lenient read: a missing or unknown value keeps its default
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"siri\"")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{\"orb\":\"nope\",\"lstyle\":\"\",\"orbSize\":30,\"attach\":\"Inside\",\"afterVoice\":3,\"labels\":\"L7\",\"lightVoice\":null,\"langMode\":[]}")]
    [InlineData("{\"orbSize\":\"26\",\"labels\":\"l2\",\"unknown\":true}")]
    [InlineData("{\"orbSize\":32.5}")]
    public void Unreadable_or_unknown_input_keeps_every_default(string json) =>
        Assert.Equal(VoiceOrbSettings.Default, Read(json));

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T10")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.1: a bad key does not spoil the good ones
    public void One_unknown_value_leaves_the_other_keys_as_stored()
    {
        var s = Read("{\"orb\":\"membrane\",\"lstyle\":\"sideways\",\"orbSize\":26,\"attach\":\"out\",\"afterVoice\":\"send\",\"labels\":\"L5\",\"lightVoice\":\"dim\",\"langMode\":\"windows\"}");
        Assert.Equal(new VoiceOrbSettings
        {
            Orb = VoiceOrbKind.Membrane, ListeningStyle = VoiceListeningStyle.Pill, OrbSize = 26, Attachment = VoiceAttachment.Outside,
            AfterVoice = VoiceAfterVoice.Send, Labels = VoicePillWords.L5, LightVoice = VoiceLightVoice.Dim, LangMode = VoiceLangMode.Windows,
        }, s);
        Assert.Equal(26, Read("{\"orbSize\":26.0}").OrbSize);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T120")]
    [Trait("Decision", "ED-267")] // OD-0097; voice-SPEC V-16.1: the three OD-0097 keys are in the default bytes and a store from before them reads with the defaults
    public void The_OD_0097_keys_are_in_the_default_bytes_and_an_older_store_without_them_reads_as_L1_away_auto()
    {
        var bytes = Encoding.UTF8.GetString(VoiceOrbSettings.Default.ToJson());
        Assert.EndsWith("\"labels\":\"L1\",\"lightVoice\":\"away\",\"langMode\":\"auto\"}", bytes, StringComparison.Ordinal);
        var older = Read("{\"orb\":\"membrane\",\"lstyle\":\"float\",\"orbSize\":26,\"attach\":\"out\",\"afterVoice\":\"send\"}");
        Assert.Equal((VoicePillWords.L1, VoiceLightVoice.Away, VoiceLangMode.Auto), (older.Labels, older.LightVoice, older.LangMode));
        Assert.Equal(VoiceOrbKind.Membrane, older.Orb);
    }
}
