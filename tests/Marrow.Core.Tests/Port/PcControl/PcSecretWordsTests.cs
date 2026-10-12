// PORT-ONLY: pill v2's secret check (OD-0145, ED-NEW-pcctl-12; DESIGN.md 10.4): a question that asks for a password, a PIN,
// a one-time code, a card or bank number, a recovery phrase or a key, in English, Hindi or Urdu, goes only to the chat;
// whole words only, so ordinary questions stay answerable in the pill.
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-12")]
public sealed class PcSecretWordsTests
{
    [Theory]
    [InlineData("What is your Outlook password?")]
    [InlineData("Type the PIN for the card reader?")]
    [InlineData("What's your pin code?")]
    [InlineData("Please give me the one-time code from your phone.")]
    [InlineData("Enter the OTP that was sent?")]
    [InlineData("Which credit card should I charge?")]
    [InlineData("What is the IBAN?")]
    [InlineData("Can you read me the recovery phrase?")]
    [InlineData("Paste the API key here?")]
    [InlineData("What is the verification code?")]
    [InlineData("अपना पासवर्ड बताइए?")]
    [InlineData("ओटीपी क्या है?")]
    [InlineData("कार्ड नंबर क्या है?")]
    [InlineData("आपकी निजी कुंजी क्या है?")]
    [InlineData("آپ کا پاس ورڈ کیا ہے؟")]
    [InlineData("پاسورڈ بتائیں")]
    [InlineData("او ٹی پی کیا ہے؟")]
    [InlineData("بینک اکاؤنٹ نمبر دیں")]
    public void Questions_that_ask_for_a_secret_are_caught(string question) =>
        Assert.True(PcSecretWords.AsksForSecret(question), question);

    [Theory]
    [InlineData("Which sheet should I fill, Q3 or Q4?")]
    [InlineData("Should I pin this tab to the taskbar?")]
    [InlineData("The pinned note says 'call Ana'. Keep it?")]
    [InlineData("Which key column holds the dates?")]
    [InlineData("Send the passing report now?")]
    [InlineData("کون سی شیٹ بھروں؟ اپنا نام لکھوں؟")]
    [InlineData("कौन सी फ़ाइल खोलूँ?")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_questions_stay_in_the_pill(string? question) =>
        Assert.False(PcSecretWords.AsksForSecret(question), question);

    [Fact]
    public void Words_keep_the_vowel_signs_of_Devanagari_and_drop_zero_width_joiners()
    {
        Assert.Equal(new[] { "पासवर्ड", "बताइए" }, PcSecretWords.Words("पासवर्ड, बताइए!"));
        Assert.Equal(new[] { "پاسورڈ" }, PcSecretWords.Words("پاس‌ورڈ"));
        Assert.Equal(new[] { "one-time", "code" }, PcSecretWords.Words("—one-time code."));
    }
}
