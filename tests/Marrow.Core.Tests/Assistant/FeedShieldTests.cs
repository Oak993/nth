using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// AS-2 (DESIGN.md 5.4 "Shield on the way in"): every summary passes the privacy-shield patterns before it reaches the
// brain, and keys, passwords, card numbers, one-time codes and IDs are masked. Expected values are written out here.
public sealed class FeedShieldTests
{
    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    [InlineData("Use sk-proj-AbCdEfGhIjKlMnOpQrStUv1234 for the build", "Use [secret] for the build")]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwxyz0123456789 pushed", "token [secret] pushed")]
    [InlineData("aws AKIAIOSFODNN7EXAMPLE here", "aws [secret] here")]
    [InlineData("Authorization: Bearer abc123def456ghi789", "Authorization: Bearer [secret]")]
    [InlineData("password=hunter2", "password=[secret]")]
    [InlineData("DB_PASSWORD: s3cr3t!x", "DB_PASSWORD: [secret]")]
    [InlineData("{\"api_key\": \"abcd1234efgh\"}", "{\"api_key\": \"[secret]\"}")]
    [InlineData("my password is hunter2, ok", "my password is [secret], ok")]
    [InlineData("connect to postgres://user:pa55w0rd@db.test/app", "connect to postgres://user:[secret]@db.test/app")]
    [InlineData("open https://x.test/cb?token=abc123&x=1", "open https://x.test/cb?token=[secret]&x=1")]
    [InlineData("run tool --password hunter2 now", "run tool --password [secret] now")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dBjftJeZ4CVPmB92K27uhbUJU1p1r", "[secret]")]
    public void Keys_and_passwords_are_masked(string text, string expected) => Assert.Equal(expected, FeedShield.Mask(text));

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public void A_private_key_block_is_masked_to_its_end_or_to_the_end_of_the_text()
    {
        Assert.Equal("before [secret] after", FeedShield.Mask("before -----BEGIN RSA PRIVATE KEY-----\nMIIEow\n-----END RSA PRIVATE KEY----- after"));
        Assert.Equal("cut [secret]", FeedShield.Mask("cut -----BEGIN PRIVATE KEY-----\nMIIEow"));
    }

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    [InlineData("card 4111 1111 1111 1111 expires soon", "card [card] expires soon")]
    [InlineData("card 4242-4242-4242-4242.", "card [card].")]
    [InlineData("5555555555554444", "[card]")]
    [InlineData("amex 378282246310005 ok", "amex [card] ok")]
    [InlineData("cvv 123", "cvv [code]")]
    [InlineData("CVC: 4821", "CVC: [code]")]
    [InlineData("exp 12/27", "exp [card]")]
    public void Card_numbers_and_their_codes_are_masked(string text, string expected) => Assert.Equal(expected, FeedShield.Mask(text));

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    [InlineData("Your verification code is 482913", "Your verification code is [code]")]
    [InlineData("OTP: 123456", "OTP: [code]")]
    [InlineData("one-time code 99887766", "one-time code [code]")]
    [InlineData("the code is 4821", "the code is [code]")]
    [InlineData("G-123456 is your Google code", "[code] is your Google code")]
    [InlineData("pin: 4821", "pin: [code]")]
    [InlineData("use 2fa code 654321 now", "use 2fa code [code] now")]
    public void One_time_codes_are_masked(string text, string expected) => Assert.Equal(expected, FeedShield.Mask(text));

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    [InlineData("ssn 123-45-6789 on file", "ssn [id] on file")]
    [InlineData("123-45-6789", "[id]")]
    [InlineData("iban GB82 WEST 1234 5698 7654 32 given", "iban [id] given")]
    [InlineData("passport number: X1234567", "passport number: [id]")]
    [InlineData("Social Security Number 987654321", "Social Security Number [id]")]
    [InlineData("driver's license no. D1234567", "driver's license no. [id]")]
    public void Identity_numbers_are_masked(string text, string expected) => Assert.Equal(expected, FeedShield.Mask(text));

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    [InlineData("Fixed 3 tests in 120 ms")]
    [InlineData("used 4,210 tokens")]
    [InlineData("max_tokens: 4096 and tokens used=1200")]
    [InlineData("exit code 10061 from the server")]
    [InlineData("commit 4f2a9c1 built 20241012 ok")]
    [InlineData("the token budget is high")]
    [InlineData("the token generation failed, and the api key is missing")]
    [InlineData("order 1234567890123 shipped")]
    [InlineData("version 2024-01-15 of pin 12 and code 7")]
    [InlineData("Merged branch claude/dextra-as2 into main, 14 files changed")]
    public void Ordinary_words_and_numbers_stay_as_they_are(string text) => Assert.Equal(text, FeedShield.Mask(text));

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    [InlineData("Use sk-proj-AbCdEfGhIjKlMnOpQrStUv1234 and password=hunter2 and card 4111 1111 1111 1111")]
    [InlineData("code is 482913, ssn 123-45-6789, Bearer abcdefgh12345678")]
    [InlineData("plain text with nothing in it")]
    public void Masking_twice_gives_the_same_text(string text)
    {
        var once = FeedShield.Mask(text);
        Assert.Equal(once, FeedShield.Mask(once));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public void An_excerpt_folds_white_space_to_one_line()
    {
        Assert.Equal("one two three", FeedShield.Excerpt("  one\n\ttwo\r\n  three  ", 300));
        Assert.Equal("", FeedShield.Excerpt("  \n ", 300));
        Assert.Equal("", FeedShield.Excerpt(null, 300));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public void An_excerpt_keeps_the_first_300_characters_and_says_it_was_cut()
    {
        var text = string.Concat(Enumerable.Range(0, 100).Select(i => $"w{i:00} "));
        var excerpt = FeedShield.Excerpt(text, 300);
        Assert.EndsWith("…", excerpt, StringComparison.Ordinal);
        Assert.True(excerpt.Length <= 301);
        Assert.StartsWith("w00 w01 w02", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public void A_key_that_straddles_the_cut_leaves_no_half_behind()
    {
        var key = "sk-" + new string('Z', 40);
        var text = new string('a', 290) + " " + key + " tail";
        var excerpt = FeedShield.Excerpt(text, 300);
        Assert.DoesNotContain("sk-", excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("ZZZZ", excerpt, StringComparison.Ordinal);
        Assert.Contains("[secret]", excerpt[..Math.Min(excerpt.Length, 300)], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public void The_cut_never_lands_inside_a_mask()
    {
        var text = new string('a', 286) + " password=hunter2 and more words after it";
        var excerpt = FeedShield.Excerpt(text, 300);
        Assert.DoesNotContain("[sec", excerpt, StringComparison.Ordinal);
        Assert.EndsWith("password=…", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public async Task A_long_hostile_text_is_cut_before_it_is_scanned_and_cannot_stall_the_filter()
    {
        var hostile = string.Concat(Enumerable.Repeat("1 ", 100_000)) + "password=hunter2";
        var masked = await Task.Run(() => FeedShield.Mask(hostile)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(masked.Length <= FeedShield.MaxScanChars);
        Assert.DoesNotContain("hunter2", masked, StringComparison.Ordinal);
        var digits = await Task.Run(() => FeedShield.Mask(new string('9', 50_000))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(digits.Length <= FeedShield.MaxScanChars);
    }
}
