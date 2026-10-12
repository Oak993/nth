using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-44; DESIGN.md 2.7 "IME composition" and "The gate", critique C4).
// The keyboard part of the gate from language id, TSF profile type and the composition signal: a Hindi or Urdu TSF text
// service waits 2 s, an English layout 700 ms, an open composition blocks, and the gate itself follows the decision.
[Trait("Decision", "ED-NEW-pcctl-44")]
public sealed class PcImeLanguageTests
{
    private const ushort EnglishUs = 0x0409;
    private const ushort Hindi = 0x0439;
    private const ushort Urdu = 0x0420;
    private const ushort Japanese = 0x0411;
    private const ushort ChineseSimplified = 0x0804;
    private const long Now = 100_000;

    [Theory]
    [InlineData(Hindi)]
    [InlineData(Urdu)]
    public void A_Hindi_or_Urdu_TSF_text_service_waits_2_s(ushort language)
    {
        var verdict = PcImeLanguage.Decide(language, PcInputProfileType.InputProcessor, immIsIme: false, compositionOpen: false);

        Assert.Equal(new PcImeVerdict(false, 2_000), verdict);
    }

    [Fact]
    public void An_English_layout_waits_700_ms()
    {
        var verdict = PcImeLanguage.Decide(EnglishUs, PcInputProfileType.KeyboardLayout, immIsIme: false, compositionOpen: false);

        Assert.Equal(new PcImeVerdict(false, 700), verdict);
    }

    [Theory]
    [InlineData(Hindi)]
    [InlineData(Urdu)]
    public void A_plain_Hindi_or_Urdu_keyboard_layout_composes_nothing_and_waits_700_ms(ushort language)
    {
        Assert.Equal(700, PcImeLanguage.Decide(language, PcInputProfileType.KeyboardLayout, immIsIme: false, compositionOpen: false).KeysStillMs);
    }

    [Theory]
    [InlineData(Japanese, false)]
    [InlineData(ChineseSimplified, false)]
    [InlineData(EnglishUs, true)]
    public void A_composed_language_or_an_IMM_IME_waits_2_s_even_on_a_layout_profile(ushort language, bool immIsIme)
    {
        Assert.Equal(2_000, PcImeLanguage.Decide(language, PcInputProfileType.KeyboardLayout, immIsIme, compositionOpen: false).KeysStillMs);
    }

    [Fact]
    public void An_unreadable_TSF_profile_errs_on_the_longer_wait()
    {
        Assert.Equal(2_000, PcImeLanguage.Decide(EnglishUs, PcInputProfileType.Unknown, immIsIme: false, compositionOpen: false).KeysStillMs);
    }

    [Theory]
    [InlineData(EnglishUs, PcInputProfileType.KeyboardLayout)]
    [InlineData(Hindi, PcInputProfileType.InputProcessor)]
    public void An_open_composition_is_reported_whatever_the_language(ushort language, PcInputProfileType profile)
    {
        Assert.True(PcImeLanguage.Decide(language, profile, immIsIme: false, compositionOpen: true).CompositionOpen);
    }

    [Theory]
    [InlineData(Urdu, PcInputProfileType.InputProcessor, 1_999, PcGateBlock.Keys)]
    [InlineData(Urdu, PcInputProfileType.InputProcessor, 2_000, PcGateBlock.None)]
    [InlineData(Hindi, PcInputProfileType.InputProcessor, 1_000, PcGateBlock.Keys)]
    [InlineData(EnglishUs, PcInputProfileType.KeyboardLayout, 699, PcGateBlock.Keys)]
    [InlineData(EnglishUs, PcInputProfileType.KeyboardLayout, 700, PcGateBlock.None)]
    public void The_gate_waits_as_long_as_the_decision_says(ushort language, PcInputProfileType profile, int ago, PcGateBlock block)
    {
        var state = new PcPhysicalSnapshot
        {
            Healthy = true,
            LastPointerMs = Now - 5_000,
            LastKeyMs = Now - ago,
            ImeLanguage = PcImeLanguage.Served(language, profile, immIsIme: false),
        };

        Assert.Equal(block, PcInputGate.Check(state, Now, PcForegroundProtection.None).Block);
    }
}
