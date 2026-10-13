using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W10, "Map street tiles source (none)" (E:/Droppy/work/design/web-surfaces/DESIGN.md 7.2, 9). The address a reader adds is an https
// address with {z}, {x} and {y}, from a site on the internet; nothing else is a tile source.
[Trait("Decision", "ED-NEW-web-w10-5")]
public sealed class MapTileTemplateTests
{
    [Theory]
    [InlineData("https://tiles.example.org/{z}/{x}/{y}.png", "tiles.example.org")]
    [InlineData("  https://tiles.example.org/{z}/{x}/{y}.png  ", "tiles.example.org")]
    [InlineData("https://tiles.example.org/styles/street/{z}/{y}/{x}.jpg?key=fake", "tiles.example.org")]
    [InlineData("https://a.tile.example.org:8443/{z}/{x}/{y}", "a.tile.example.org")]
    [InlineData("https://8.8.8.8/{z}/{x}/{y}.png", "8.8.8.8")]
    public void An_https_address_with_the_three_placeholders_is_a_tile_source(string text, string host)
    {
        Assert.True(MapTileTemplate.TryParse(text, out var template));
        Assert.Equal(host, template.Host);
        Assert.Equal(text.Trim(), template.Text);
        Assert.Equal("Map tiles from " + host, template.Attribution);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://tiles.example.org/{z}/{x}/{y}.png")]
    [InlineData("ftp://tiles.example.org/{z}/{x}/{y}.png")]
    [InlineData("file:///C:/tiles/{z}/{x}/{y}.png")]
    [InlineData("tiles.example.org/{z}/{x}/{y}.png")]
    [InlineData("https://tiles.example.org/{z}/{x}.png")]
    [InlineData("https://tiles.example.org/{z}/{x}/{y}/{y}.png")]
    [InlineData("https://tiles.example.org/{z}/{x}/{y}/{s}.png")]
    [InlineData("https://tiles.example.org/{z}/{x}/{y}.png#frag")]
    [InlineData("https://user:secret@tiles.example.org/{z}/{x}/{y}.png")]
    [InlineData("https://{z}.example.org/{z}/{x}/{y}.png")]
    [InlineData("https://tiles.example.org{x}/{z}/{y}.png")]
    [InlineData("https://localhost/{z}/{x}/{y}.png")]
    [InlineData("https://tiles.localhost/{z}/{x}/{y}.png")]
    [InlineData("https://127.0.0.1/{z}/{x}/{y}.png")]
    [InlineData("https://192.168.1.20/{z}/{x}/{y}.png")]
    [InlineData("https://10.0.0.5:8080/{z}/{x}/{y}.png")]
    [InlineData("https://tiles.lan/{z}/{x}/{y}.png")]
    [InlineData("https://tiles.example.test/{z}/{x}/{y}.png")]
    [InlineData("https://tiles.corp.internal/{z}/{x}/{y}.png")]
    [InlineData("https://printer.local/{z}/{x}/{y}.png")]
    [InlineData("https://tiles/{z}/{x}/{y}.png")]
    [InlineData("https://[::1]/{z}/{x}/{y}.png")]
    public void Anything_else_is_refused(string? text) => Assert.False(MapTileTemplate.TryParse(text, out _));

    [Fact]
    public void An_address_over_the_length_limit_is_refused()
    {
        var path = new string('a', MapTileTemplate.MaxLength);
        Assert.False(MapTileTemplate.TryParse("https://tiles.example.org/" + path + "/{z}/{x}/{y}.png", out _));
        Assert.True(MapTileTemplate.TryParse("https://tiles.example.org/" + new string('a', 100) + "/{z}/{x}/{y}.png", out _));
    }

    [Fact]
    public void The_placeholders_become_the_tile_numbers_and_nothing_else_changes()
    {
        Assert.True(MapTileTemplate.TryParse("https://tiles.example.org/{z}/{x}/{y}.png?scale=2", out var template));
        Assert.Equal("https://tiles.example.org/3/5/2.png?scale=2", template.UrlFor(3, 5, 2));
        Assert.Equal("https://tiles.example.org/19/524287/12.png?scale=2", template.UrlFor(19, 524287, 12));
    }
}
