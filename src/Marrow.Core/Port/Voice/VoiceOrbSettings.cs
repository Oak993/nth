// PORT-ONLY, owner-ruled addition (OD-0091 sixth amendment, OD-0097; first-release voice typing, VT-1): the voiceOrb settings
// (voice-SPEC V-16.1). Key names and values are the decision page's own (labels L1..L6, lightVoice away|dim|stay, its lang stored
// as langMode so it is never confused with voiceTyping.lang). Customization reads and writes them; the voice runtime reads them.
using System.Text.Json;

namespace Marrow.Core.Port.Voice;

/// <summary>Which orb the composer shows (V-1.1): Siri Wave (the default) or Voice Membrane.</summary>
public enum VoiceOrbKind { Siri, Membrane }

/// <summary>How listening looks (V-12.1): the pill in the composer (default), the cross beside the orb, or the floating pill.</summary>
public enum VoiceListeningStyle { Pill, Cross, Float }

/// <summary>Where the attachment button sits (V-14.1): inside the composer (default) or outside it.</summary>
public enum VoiceAttachment { Inside, Outside }

/// <summary>What happens to the words when voice input ends (V-8.8): the text stays unsent (default) or is sent.</summary>
public enum VoiceAfterVoice { Leave, Send }

/// <summary>The pill's word sets L1 to L6 (V-12.9); L1 is the default.</summary>
public enum VoicePillWords { L1, L2, L3, L4, L5, L6 }

/// <summary>The composer light while you speak (V-13.4): steps aside (default, B1), dims to 35 % (B2), or stays on (B3).</summary>
public enum VoiceLightVoice { Away, Dim, Stay }

/// <summary>The language default (V-7.14): A auto-detect silent (default), B auto-detect named in the pill, C the Windows display language, D asked on the first click.</summary>
public enum VoiceLangMode { Auto, Show, Windows, Ask }

/// <summary>
/// The <c>voiceOrb</c> settings. Defaults are the locked ones (OD-0091 sixth amendment: Siri Wave, pill, 32 px, attachment inside,
/// leave the text) and OD-0097's (L1, the light steps aside, Auto-detect silent).
/// </summary>
public sealed record VoiceOrbSettings
{
    /// <summary>The settings key.</summary>
    public const string StoreKey = "voiceOrb";

    public VoiceOrbKind Orb { get; init; } = VoiceOrbKind.Siri;
    public VoiceListeningStyle ListeningStyle { get; init; } = VoiceListeningStyle.Pill;

    /// <summary>The orb's diameter in px: 32 (default) or 26.</summary>
    public int OrbSize { get; init; } = 32;

    public VoiceAttachment Attachment { get; init; } = VoiceAttachment.Inside;
    public VoiceAfterVoice AfterVoice { get; init; } = VoiceAfterVoice.Leave;
    public VoicePillWords Labels { get; init; } = VoicePillWords.L1;
    public VoiceLightVoice LightVoice { get; init; } = VoiceLightVoice.Away;
    public VoiceLangMode LangMode { get; init; } = VoiceLangMode.Auto;

    public static VoiceOrbSettings Default { get; } = new();

    private static readonly string[] OrbNames = ["siri", "membrane"];
    private static readonly string[] StyleNames = ["pill", "cross", "float"];
    private static readonly string[] AttachNames = ["in", "out"];
    private static readonly string[] AfterNames = ["leave", "send"];
    private static readonly string[] LabelNames = ["L1", "L2", "L3", "L4", "L5", "L6"];
    private static readonly string[] LightNames = ["away", "dim", "stay"];
    private static readonly string[] LangModeNames = ["auto", "show", "windows", "ask"];

    /// <summary>Compact JSON in V-16.1's key order.</summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, VoiceJson.WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("orb", VoiceJson.WireName(OrbNames, Orb, Default.Orb));
            w.WriteString("lstyle", VoiceJson.WireName(StyleNames, ListeningStyle, Default.ListeningStyle));
            w.WriteNumber("orbSize", OrbSize);
            w.WriteString("attach", VoiceJson.WireName(AttachNames, Attachment, Default.Attachment));
            w.WriteString("afterVoice", VoiceJson.WireName(AfterNames, AfterVoice, Default.AfterVoice));
            w.WriteString("labels", VoiceJson.WireName(LabelNames, Labels, Default.Labels));
            w.WriteString("lightVoice", VoiceJson.WireName(LightNames, LightVoice, Default.LightVoice));
            w.WriteString("langMode", VoiceJson.WireName(LangModeNames, LangMode, Default.LangMode));
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Lenient read (V-16.1): a missing or unknown value keeps its default; unreadable data is the default.</summary>
    public static VoiceOrbSettings FromJson(ReadOnlySpan<byte> json) => VoiceJson.ReadObject(json, Default, o =>
    {
        var size = VoiceJson.Number(o, "orbSize", Default.OrbSize);
        return new VoiceOrbSettings
        {
            Orb = VoiceJson.Name(o, "orb", OrbNames, Default.Orb),
            ListeningStyle = VoiceJson.Name(o, "lstyle", StyleNames, Default.ListeningStyle),
            OrbSize = size == 32 || size == 26 ? (int)size : Default.OrbSize,
            Attachment = VoiceJson.Name(o, "attach", AttachNames, Default.Attachment),
            AfterVoice = VoiceJson.Name(o, "afterVoice", AfterNames, Default.AfterVoice),
            Labels = VoiceJson.Name(o, "labels", LabelNames, Default.Labels),
            LightVoice = VoiceJson.Name(o, "lightVoice", LightNames, Default.LightVoice),
            LangMode = VoiceJson.Name(o, "langMode", LangModeNames, Default.LangMode),
        };
    });
}
