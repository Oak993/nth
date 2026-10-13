using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

public sealed class DextraC6cD2Tests
{
    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-2")]
    public void Single_quoted_dictionary_key_masks_the_whole_value() => Check(
        "{'password': 'hunter2secret'}", "{'password': '[secret]'}");

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-2")]
    public void Escaped_json_key_masks_the_whole_value() => Check(
        "{\\\"password\\\":\\\"hunter2secret\\\"}", "{\\\"password\\\":\\\"[secret]\\\"}");

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-2")]
    public void Escaped_outer_quotes_mask_the_whole_value() => Check(
        "password=\\\"hunter2secret\\\"", "password=\\\"[secret]\\\"");

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-2")]
    public void Escaped_inner_quote_in_an_embedded_value_cannot_leave_a_secret_tail() => Check(
        "password=\\\"first\\\\\\\"stillsecret\\\" tail", "password=\\\"[secret]\\\" tail");

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-2")]
    public void Doubled_yaml_quote_cannot_leave_a_secret_tail() => Check(
        "password: 'it''s hunter2secret'", "password: '[secret]'");

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-2")]
    public void A_backslash_before_doubled_yaml_quotes_cannot_leave_a_secret_tail() => Check(
        "password: 'it\\''s hunter2secret'", "password: '[secret]'");

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-2")]
    public void Plain_json_without_spaces_is_an_already_masked_control() => Check(
        "{\"password\":\"hunter2secret\"}", "{\"password\":\"[secret]\"}");

    private static void Check(string text, string expected)
    {
        Assert.Equal(expected, FeedShield.Mask(text));
        Assert.Equal(expected, FeedShield.Mask(FeedShield.Mask(text)));
        Assert.Equal(expected, FeedShield.Excerpt(text, 300));
    }
}
