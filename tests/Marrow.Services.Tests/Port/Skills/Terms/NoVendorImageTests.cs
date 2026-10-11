// PORT-ONLY: SK-13 / DL-15 (text test). "No vendor logo or image referenced anywhere": the skills code, the Skills and Plugins page shells, the terms step and the embedded
// catalogue name no image file, no icon or logo field and no image type (outside the unmodified payload a vendor set pins, ED-NEW-dl-11 (8)); vendor names are plain text
// (skills-PLAN.md 3.1 wording rules, ONDEMAND-PLAN.md 2.5).
using System.Text.RegularExpressions;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Tests.Port.Skills.Terms;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-15")]
public sealed partial class NoVendorImageTests
{
    [GeneratedRegex(@"\.(png|jpe?g|gif|svg|ico|webp|bmp|icns)\b|icon_small|icon_large|brand_color|avares://|\bBitmap\b|\bIImage\b|""[^""]*logo[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageReference();

    [GeneratedRegex(@"""(icon|logo|image|brand)[A-Za-z_]*""\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ImageKey();

    [GeneratedRegex(@"\.(png|jpe?g|gif|svg|ico|webp|bmp|icns)$", RegexOptions.IgnoreCase)]
    private static partial Regex ImageFileName();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Marrow.sln not found above the test output");
    }

    private static string WithoutComments(string text) => Regex.Replace(text, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_skills_sources_reference_no_image_and_no_vendor_logo()
    {
        var root = RepoRoot();
        var folders = new[] { "src/Marrow.Core/Port/Skills", "src/Marrow.Services/Port/Skills", "src/Marrow.App/Port/Skills" };
        var files = folders.Select(f => Path.Combine(root, f)).Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Concat(new[] { "src/Marrow.App/Port/Settings/SkillsSettingsPage.cs", "src/Marrow.App/Port/Settings/PluginsSettingsPage.cs" }.Select(p => Path.Combine(root, p)))
            .ToArray();
        Assert.True(files.Length > 60, "the scan must cover the skills sources, found " + files.Length);
        Assert.Contains(files, f => f.EndsWith("TermsStep.cs", StringComparison.Ordinal));
        var hits = files.Where(f => ImageReference().IsMatch(WithoutComments(File.ReadAllText(f)))).Select(Path.GetFileName).ToArray();
        Assert.Empty(hits);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_embedded_catalogue_carries_no_image_reference_outside_the_pinned_files_of_its_sets_and_no_icon_or_logo_field()
    {
        using var stream = typeof(SkillsCatalogue).Assembly.GetManifestResourceStream(SkillsCatalogueLoader.ResourceName)!;
        var text = new StreamReader(stream).ReadToEnd();
        Assert.Empty(ImageKey().Matches(text).Select(m => m.Value));
        // ED-NEW-dl-11 (8): a vendor set is downloaded unmodified (ONDEMAND-PLAN.md section 1), so its pinned files may include the vendor's own icon files (OpenAI's
        // assets/*.png and *.svg); they are payload nth stores for the agents and never draws. Every other value (titles, summaries, vendors, licence names and notes, skill
        // names, addresses) carries no image reference, and no image is a tool's or package set's file.
        var catalogue = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        var payload = new List<string>();
        foreach (var item in catalogue["items"]!.AsArray())
        {
            var files = item!["files"]!.AsArray();
            if (item["kind"]!.GetValue<string>() is "skillset" or "plugin") payload.AddRange(files.Select(f => f!["path"]!.GetValue<string>()).Where(p => ImageReference().IsMatch(p)));
            item.AsObject().Remove("files");
        }
        Assert.Empty(ImageReference().Matches(catalogue.ToJsonString()).Select(m => m.Value));
        // ED-NEW-dl-13: pin run 4 pinned A6 and O5 unmodified too, so three README example images of A6's plugins and a test golden of O5's are payload of the same kind.
        Assert.Equal(["plugins/claude-code-setup/automation-recommender-example.png", "plugins/claude-md-management/claude-md-improver-example.png",
            "plugins/claude-md-management/revise-claude-md-example.png", "plugins/public-equity-investing/tests/goldens/dashboard-production-complete.png"],
            payload.Where(p => !p.Contains("/assets/", StringComparison.Ordinal)));
        Assert.NotEmpty(payload);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_assemblies_embed_no_image_for_the_skills_surfaces()
    {
        var names = new[] { typeof(SkillsCatalogue).Assembly, typeof(Marrow.Services.Port.Skills.Terms.TermsStep).Assembly }
            .SelectMany(a => a.GetManifestResourceNames()).Where(n => n.Contains("Skills", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.False(ImageFileName().IsMatch(n), n));
    }
}
