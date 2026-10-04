using Marrow.Core.Port.Updates;

namespace Marrow.Core.Tests.Port.Updates;

// Unit UE-2: "Check now: latest versions from public listings (a network inventory entry), 'Checks when updated' when there is no listing". The listings are the npm registry's latest
// document for Claude Code and GitHub's latest release for Codex (UNVERIFIED from the pages, see ED-308); the fetch is a seam that fails closed, so no test reaches the network.
public sealed class PublicVersionListingsTests
{
    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_npm_document_gives_its_version_and_anything_else_gives_nothing()
    {
        Assert.Equal("2.1.3", PublicVersionListings.ParseNpmLatest("{\"name\":\"x\",\"version\":\"2.1.3\"}"));
        Assert.Null(PublicVersionListings.ParseNpmLatest("{\"name\":\"x\"}"));
        Assert.Null(PublicVersionListings.ParseNpmLatest("{\"version\":3}"));
        Assert.Null(PublicVersionListings.ParseNpmLatest("{\"version\":\"  \"}"));
        Assert.Null(PublicVersionListings.ParseNpmLatest("not json"));
        Assert.Null(PublicVersionListings.ParseNpmLatest("[]"));
        Assert.Null(PublicVersionListings.ParseNpmLatest(""));
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_release_document_gives_its_tag_without_the_prefix_and_only_when_the_tag_has_it()
    {
        Assert.Equal("0.136.0", PublicVersionListings.ParseGithubLatestRelease("{\"tag_name\":\"rust-v0.136.0\"}", "rust-v"));
        Assert.Null(PublicVersionListings.ParseGithubLatestRelease("{\"tag_name\":\"v0.136.0\"}", "rust-v"));
        Assert.Null(PublicVersionListings.ParseGithubLatestRelease("{\"tag_name\":\"rust-v\"}", "rust-v"));
        Assert.Null(PublicVersionListings.ParseGithubLatestRelease("{}", "rust-v"));
        Assert.Null(PublicVersionListings.ParseGithubLatestRelease("nope", "rust-v"));
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_listing_fetches_the_url_of_the_key_and_has_nothing_for_any_other_key()
    {
        var asked = new List<string>();
        var listing = new FetchedVersionListing((uri, _) => { asked.Add(uri.AbsoluteUri); return Task.FromResult(uri.Host == "registry.npmjs.org" ? "{\"version\":\"2.0.0\"}" : "{\"tag_name\":\"rust-v0.1.0\"}"); });
        Assert.Equal("2.0.0", await listing.Latest("claude", default));
        Assert.Equal("0.1.0", await listing.Latest("codex", default));
        Assert.Null(await listing.Latest("grok", default));
        Assert.Equal([PublicVersionListings.ClaudeCodeUrl, PublicVersionListings.CodexUrl], asked);
        Assert.All(asked, a => Assert.StartsWith("https://", a, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_default_fetch_fails_closed_and_a_fetch_that_throws_is_not_swallowed_here()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FetchedVersionListing().Latest("claude", default));
        Assert.Null(await new FetchedVersionListing().Latest("grok", default));
        Assert.Null(await new NoVersionListing().Latest("claude", default));
    }

    [Theory]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("2.1.3 (Claude Code)", "2.1.3")]
    [InlineData("codex-cli 0.136.0", "0.136.0")]
    [InlineData("v1.2.3-beta", "1.2.3")]
    [InlineData("no digits", "no digits")]
    [InlineData("  7  ", "7")]
    public void The_version_number_inside_a_version_text_is_the_first_dotted_number(string text, string expected) =>
        Assert.Equal(expected, VersionText.Number(text));

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Two_texts_are_the_same_version_when_their_numbers_are_equal_and_unknown_ones_never_are()
    {
        Assert.True(VersionText.Same("2.1.3 (Claude Code)", "2.1.3"));
        Assert.False(VersionText.Same("2.1.3", "2.1.4"));
        Assert.False(VersionText.Same("2.1.3", "2.1"));
        Assert.False(VersionText.Same(null, "2.1.3"));
        Assert.False(VersionText.Same("2.1.3", null));
    }
}
