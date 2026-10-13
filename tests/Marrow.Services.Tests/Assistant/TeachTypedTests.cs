using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 1, 12.5; ED-NEW-dextra-as19-2): what a recording keeps of what was typed. A password field is a
// secret whatever it holds; a text the privacy patterns would change is a secret and goes whole; a field nth could not look up keeps only
// how many characters were typed; other text is kept, cut to a bounded length. A secret keeps its length and nothing else.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-2")]
public sealed class TeachTypedTests
{
    private static readonly TeachElement Field = new("outlook", "New message", "edit", "Subject");

    private static readonly TeachElement Password = new("chrome", "Sign in", "edit", "Password") { IsPassword = true };

    [Fact]
    public void A_password_field_is_a_secret_whatever_it_holds_and_keeps_only_its_length()
    {
        var kept = TeachTyped.Record(Password, "hunter2", 7);
        Assert.Equal(new TeachKept("[secret]", 7, true), kept);
        Assert.Equal(new TeachKept("[secret]", 0, true), TeachTyped.Record(Password, null, 0));
    }

    [Theory]
    [InlineData("sk-abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("my password is hunter2")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----")]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWx123456")]
    public void A_text_the_privacy_patterns_would_change_is_a_secret_and_goes_whole(string text)
    {
        var kept = TeachTyped.Record(Field, text, text.Length);
        Assert.True(kept.Secret);
        Assert.Equal(TeachWords.Secret, kept.Text);
        Assert.Equal(text.Length, kept.Length);
    }

    [Fact]
    public void A_field_nth_could_not_look_up_keeps_only_the_length()
    {
        Assert.Equal(new TeachKept(null, 12, false), TeachTyped.Record(null, "anything", 12));
        Assert.Equal(new TeachKept(null, 12, false), TeachTyped.Record(new TeachElement("app", "", "", ""), "anything", 12));
    }

    [Fact]
    public void A_field_whose_value_could_not_be_read_keeps_only_the_length()
    {
        Assert.Equal(new TeachKept(null, 5, false), TeachTyped.Record(Field, null, 5));
        Assert.Equal(new TeachKept(null, 5, false), TeachTyped.Record(Field, "", 5));
    }

    [Fact]
    public void Ordinary_text_is_kept_as_it_is()
    {
        Assert.Equal(new TeachKept("Weekly report for Q3", 20, false), TeachTyped.Record(Field, "Weekly report for Q3", 20));
        Assert.Equal(new TeachKept("भेजें रिपोर्ट", 13, false), TeachTyped.Record(Field, "भेजें रिपोर्ट", 13));
    }

    [Fact]
    public void A_long_text_is_cut_to_the_limit_and_never_inside_a_pair_of_surrogates()
    {
        var text = new string('x', TeachLimits.TypedChars - 1) + "😀" + "tail";
        var kept = TeachTyped.Record(Field, text, text.Length);
        Assert.Equal(TeachLimits.TypedChars - 1, kept.Text!.Length);
        Assert.Equal(text.Length, kept.Length);
        var plain = TeachTyped.Record(Field, new string('y', 1000), 1000);
        Assert.Equal(TeachLimits.TypedChars, plain.Text!.Length);
    }

    [Fact]
    public void A_negative_count_is_none()
    {
        Assert.Equal(0, TeachTyped.Record(Field, "x", -3).Length);
    }
}
