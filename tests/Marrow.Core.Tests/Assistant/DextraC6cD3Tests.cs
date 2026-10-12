using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

public sealed class DextraC6cD3Tests
{
    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-3")]
    public void Empty_quoted_values_keep_the_missing_key_diagnostic()
    {
        Check("Invalid config: \"api_key\": \"\" is empty, set it in settings", "Invalid config: \"api_key\": \"\" is empty, set it in settings");
        Check("password='' is empty", "password='' is empty");
        Check("password=\\\"\\\" is empty", "password=\\\"\\\" is empty");
    }

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-3")]
    public void A_literal_drive_path_ending_in_a_backslash_keeps_the_diagnostic_tail()
    {
        Check("private_key: 'C:\\keys\\' not found; check the folder permissions and retry", "private_key: '[secret]' not found; check the folder permissions and retry");
        Check("private_key: \"C:\\keys\\\" not found; retry", "private_key: \"[secret]\" not found; retry");
    }

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-3")]
    public void Ambiguous_drive_shaped_escaped_quotes_still_fail_closed()
    {
        Check("password=\"C:\\keys\\\"hunter2secret", "password=\"[secret]\"");
        Check("private_key: 'C:\\keys\\' hunter2secret", "private_key: '[secret]'");
    }

    private static void Check(string text, string expected)
    {
        Assert.Equal(expected, FeedShield.Mask(text));
        Assert.Equal(expected, FeedShield.Mask(FeedShield.Mask(text)));
    }
}
