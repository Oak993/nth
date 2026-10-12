// PORT-ONLY: ED-NEW-dlhonest-1 (owner 2026-10-11). Step 1 of "Download it yourself" reads as prose with only its literal values in code: the commit of a repository, the file of a tool, wheels.txt of a package set.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Core.Tests.Port.Skills.Pages;

[Trait("Feature", "SK-14")]
[Trait("Decision", "ED-NEW-dlhonest-1")]
public sealed class ManualFallbackPartsTests
{
    private static readonly SkillsCatalogue Catalogue = SkillsCatalogueLoader.LoadEmbedded();

    private static ManualFallback Of(string id) => ManualFallbacks.For(Catalogue.Items.Single(i => i.Id == id))!;

    private static string[] Code(ManualFallback fallback) => fallback.Parts.Where(p => p.Code).Select(p => p.Text).ToArray();

    [Theory]
    [InlineData("A3")]
    [InlineData("O3")]
    [InlineData("T1")]
    [InlineData("T10")]
    [InlineData("P1")]
    public void The_parts_join_to_the_whole_step(string id) => Assert.Equal(Of(id).File, string.Concat(Of(id).Parts.Select(p => p.Text)));

    [Fact]
    public void A_repository_is_prose_with_only_its_commit_in_code()
    {
        Assert.Equal([new ManualFallbackPart("the repository at commit ", false), new ManualFallbackPart("8444efc", true), new ManualFallbackPart(", as a .zip", false)], Of("A3").Parts);
        Assert.Equal(["45d05d7", "49f948f", "e6afb0d"], Code(Of("O3")));
    }

    [Fact]
    public void A_tool_is_its_file_names_in_code_and_never_a_repository_commit()
    {
        Assert.Equal(["python-3.14.8-embed-amd64.zip", "pip-26.2.1-py3-none-any.whl"], Code(Of("T1")));
        Assert.Equal(" and ", Assert.Single(Of("T1").Parts, p => !p.Code).Text);
        Assert.DoesNotContain("commit", Of("T1").File, StringComparison.Ordinal);
        Assert.Equal(["chrome-win64.zip", "chrome-headless-shell-win64.zip"], Code(Of("T10")));
        Assert.Equal(" and ", Assert.Single(Of("T10").Parts, p => !p.Code).Text);
        Assert.Equal(["wheels.txt"], Code(Of("P1")));
    }
}
