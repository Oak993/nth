// PORT-ONLY: first-release feature unit SK-14 ("Download it yourself", the locked skills-downloads design: sd-page.js FB and fbBlock). The words are the design's, checked against its own table for every item it lists.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Core.Tests.Port.Skills.Pages;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-17")]
[Trait("Rule", "R-TEST-4")]
public sealed class ManualFallbackTests
{
    private static readonly SkillsCatalogue Catalogue = SkillsCatalogueLoader.LoadEmbedded();

    private static ManualFallback? Of(string id) => ManualFallbacks.For(Catalogue.Items.Single(i => i.Id == id));

    [Theory]
    [InlineData("A3", "the repository at commit 8444efc, as a .zip", "github.com/anthropics/knowledge-work-plugins/tree/8444efc")]
    [InlineData("A4", "the repository at commit 574ed36, as a .zip", "github.com/anthropics/financial-services/tree/574ed36")]
    [InlineData("A1", "the repository at commit 8a1541c, as a .zip", "github.com/anthropics/skills/tree/8a1541c")]
    [InlineData("A5", "the repository at commit e96556b, as a .zip", "github.com/anthropics/life-sciences/tree/e96556b")]
    [InlineData("O1", "the repository at commit 2739e82, as a .zip", "github.com/openai/codex/tree/2739e82")]
    [InlineData("O2", "the repository at commit 49f948f, as a .zip", "github.com/openai/skills/tree/49f948f")]
    [InlineData("O4", "the repository at commit 5fd93af, as a .zip", "github.com/openai/plugins/tree/5fd93af")]
    // The design lists O3's commits in its plan's skill order; the pinned catalogue lists the skills in the ordinal order of their paths (doc, pdf, slides, spreadsheet), so the commits come in that order.
    [InlineData("O3", "the three skill folders at their pinned commits (45d05d7, 49f948f, e6afb0d)", "github.com/openai/skills")]
    [InlineData("T2", "node-v24.21.0-win-x64.zip", "nodejs.org/dist/v24.21.0/")]
    [InlineData("T3", "LibreOffice_26.8.1_Win_x86-64.msi", "download.documentfoundation.org/libreoffice/stable/26.8.1/win/x86_64/")]
    [InlineData("T4", "Release-26.09.0-0.zip", "github.com/oschwartz10612/poppler-windows/releases")]
    [InlineData("T5", "qpdf-12.4.2-msvc64.zip", "github.com/qpdf/qpdf/releases")]
    [InlineData("T6", "pandoc-3.12.1-windows-x86_64.zip", "github.com/jgm/pandoc/releases")]
    [InlineData("T7", "PortableGit-2.56.0.2-64-bit.7z.exe", "github.com/git-for-windows/git/releases")]
    [InlineData("T8", "gh_2.102.0_windows_amd64.zip", "github.com/cli/cli/releases")]
    [InlineData("T9", "uv-x86_64-pc-windows-msvc.zip", "github.com/astral-sh/uv/releases")]
    [InlineData("T10", "chrome-win64.zip and chrome-headless-shell-win64.zip", "cdn.playwright.dev/builds/cft/153.0.8010.12/win64/")]
    [InlineData("T1", "python-3.14.8-embed-amd64.zip and pip-26.2.1-py3-none-any.whl", "python.org/ftp/python/3.14.8/")]
    [InlineData("T1p", "pip-26.2.1-py3-none-any.whl", "pypi.org")]
    [InlineData("P1", "the pinned wheels named in wheels.txt in that folder", "pypi.org")]
    public void Step_one_is_the_designs_text_for_the_item(string id, string file, string source)
    {
        var fallback = Of(id)!;
        Assert.Equal((file, source), (fallback.File, fallback.Source));
        Assert.Equal(new Uri("https://" + source), fallback.Page);
    }

    [Fact]
    public void A_tool_and_a_package_set_say_their_checking_in_the_tools_words_and_a_set_in_the_sets()
    {
        Assert.True(Of("T3")!.ForTool);
        Assert.True(Of("P1")!.ForTool);
        Assert.False(Of("A3")!.ForTool);
    }

    [Theory]
    [InlineData("P6")] // a Node package set: the design draws none
    [InlineData("A6")] // no pin: no commit to name
    public void An_item_the_design_draws_no_fallback_for_has_none(string id) => Assert.Null(Of(id));

    [Fact]
    public void The_words_are_the_designs()
    {
        Assert.Equal("Download it yourself", ManualFallbackWords.Heading);
        Assert.Equal("Open download page ↗", ManualFallbackWords.OpenPage);
        Assert.Equal("Open folder", ManualFallbackWords.OpenFolder);
        Assert.Equal("Check again", ManualFallbackWords.CheckAgain);
        Assert.Equal("nth checks its SHA-256 against the pinned one and installs it.", ManualFallbackWords.CheckingTool);
        Assert.Equal("nth checks every file against its pinned hash and installs it.", ManualFallbackWords.CheckingSet);
        Assert.Equal("Get ", ManualFallbackWords.Get);
        Assert.Equal("Put it in ", ManualFallbackWords.PutIn);
        Assert.Equal("%LOCALAPPDATA%\\nth\\addons\\downloads\\manual\\", ManualFallbackWords.Folder);
    }
}
