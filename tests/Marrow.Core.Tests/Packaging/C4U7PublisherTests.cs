using System.Text.RegularExpressions;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4U7PublisherTests
{
    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Owner_selected_publisher_is_the_default_in_both_packaging_and_release_metadata()
    {
        var script = File.ReadAllText(Path.Combine(PackagingRig.Scripts, "package-windows.ps1"));
        var props = File.ReadAllText(Path.Combine(PackagingRig.Repository, "Directory.Release.props"));
        Assert.Equal("oak993", Regex.Match(script, @"\$PackAuthors\s*=\s*'([^']+)'", RegexOptions.CultureInvariant).Groups[1].Value);
        Assert.Equal("oak993", Regex.Match(props, "<NthPublisher>([^<]+)</NthPublisher>", RegexOptions.CultureInvariant).Groups[1].Value);
    }
}
