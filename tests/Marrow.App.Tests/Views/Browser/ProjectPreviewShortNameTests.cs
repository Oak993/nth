using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: final security gate S3 (ED-NEW-secfinal-21; ED-NEW-web-w10-8). "Open in Visus" offers an HTML file of the project only when no
// hidden folder or file is on the way, but it read the text of the path, and a hidden folder can be named through its 8.3 alias
// (C:\project\GIT~1\page.html). The path is now resolved to its real long names before the hidden-name rule runs, so the alias gets no row.
// A fake long-path mapping stands for a volume that gives the files their aliases.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-secfinal-21")]
public sealed class ProjectPreviewShortNameTests(HeadlessSession headless)
{
    private static string Tree()
    {
        var root = Path.Combine(Path.GetTempPath(), "secfinal-open-" + Guid.NewGuid().ToString("N"), "project");
        Directory.CreateDirectory(Path.Combine(root, "HIDDE~1"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "HIDDE~1", "page.html"), "<p>secret</p>");
        File.WriteAllText(Path.Combine(root, "docs", "page.html"), "<p>page</p>");
        File.WriteAllText(Path.Combine(root, "docs", "my~page.html"), "<p>tilde</p>");
        File.WriteAllText(Path.Combine(root, "index.html"), "<p>home</p>");
        return root;
    }

    private static Func<string, string?> Mapping(string root) => path =>
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || relative == ".") return path;
        var segments = relative.Split(Path.DirectorySeparatorChar).Select(s => string.Equals(s, "HIDDE~1", StringComparison.OrdinalIgnoreCase) ? ".hidden" : s);
        return Path.Combine(root, string.Join(Path.DirectorySeparatorChar, segments));
    };

    [Fact]
    public void A_hidden_folder_reached_by_its_short_name_gets_no_address() => headless.OnUiThread(() =>
    {
        var root = Tree();
        try
        {
            var site = new StaticPreviewSite("p1", root, Mapping(root));

            Assert.Null(ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "HIDDE~1", "page.html")));
            Assert.Null(ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "hidde~1", "page.html")));
            Assert.Equal(site.Origin + "/docs/page.html", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "docs", "page.html")));
            Assert.Equal(site.Origin + "/docs/my~page.html", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "docs", "my~page.html")));
            Assert.Equal(site.Origin + "/index.html", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "index.html")));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    });
}
