using Marrow.Core.Port.SelfUpdate;

namespace Marrow.Core.Tests.Port.SelfUpdate;

// UE-1 (OD-0100, answered 2026-10-04): Marrow's update feed is the public GitHub repository Oak993/marrow-releases, one named constant.
public sealed class MarrowReleaseFeedTests
{
    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_feed_is_the_one_public_repository_the_owner_named()
    {
        Assert.Equal("Oak993/marrow-releases", MarrowReleaseFeed.Repository);
        Assert.Equal("https://github.com/Oak993/marrow-releases", MarrowReleaseFeed.Url);
        Assert.Equal("https://github.com/Oak993/marrow-releases/releases/latest", MarrowReleaseFeed.LatestReleasePage);
        Assert.Equal("NthApp", MarrowReleaseFeed.PackageId);                       // ED-235: the id vpk pack is given
    }

    [Theory]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("https://github.com/Oak993/marrow-releases", true)]
    [InlineData("https://github.com/Oak993/marrow-releases/", true)]
    [InlineData("http://github.com/Oak993/marrow-releases", true)]
    [InlineData("", false)]
    [InlineData("https://github.com/Oak993", false)]
    [InlineData("https://github.com/Oak993/marrow-releases/releases", false)]
    [InlineData("http://127.0.0.1:5000/releases", false)]
    [InlineData("https://gitlab.com/a/b", false)]
    [InlineData("not a url", false)]
    public void Only_an_owner_and_name_on_github_is_a_repository_feed(string feed, bool expected) =>
        Assert.Equal(expected, MarrowReleaseFeed.IsRepositoryUrl(feed));
}
