using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

// Regression for the accepted Dextra C6-F4 finding. Offline only.
public sealed class DextraC6F4Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-4")]
    public void Long_quoted_secret_is_masked_before_the_feed_sees_it()
    {
        var secret = new string('q', 240);
        var draft = FeedSamples.Draft(FeedEventKind.Error, FeedSeverity.Error, 0, "hit an error", Guid.NewGuid(),
            "password=\"" + secret + "\"", FeedSamples.Facts());
        var line = FeedBuilder.Build(draft, new FeedBrain(ProviderKind.Claude), 1)!;
        Assert.DoesNotContain(secret, line.Summary, StringComparison.Ordinal);
        Assert.Contains("[secret]", line.Summary, StringComparison.Ordinal);
    }
    [Theory]
    [Trait("Decision", "ED-NEW-dextra-cx-4")]
    [InlineData(201)]
    [InlineData(240)]
    [InlineData(4096)]
    [InlineData(50000)]
    public void Quoted_secrets_are_masked_even_when_the_closing_quote_is_outside_the_scan(int length)
    {
        var secret = new string('q', length);
        Assert.Equal("password=\"[secret]\"", FeedShield.Mask("password=\"" + secret + "\""));
        Assert.Equal("password='[secret]'", FeedShield.Mask("password='" + secret + "'"));
    }

    [Theory]
    [Trait("Decision", "ED-NEW-dextra-cx-4")]
    [InlineData("password=\"first'part\\\"stillsecret\nlast\" after", "password=\"[secret]\" after")]
    [InlineData("password='first\"part\\'stillsecret\nlast' after", "password='[secret]' after")]
    [InlineData("password=\"unfinished\\", "password=\"[secret]\"")]
    public void Quoted_values_include_escaped_and_opposite_quotes_and_unterminated_tails(string text, string expected)
    {
        Assert.Equal(expected, FeedShield.Mask(text));
        Assert.Equal(expected, FeedShield.Mask(FeedShield.Mask(text)));
    }

}
