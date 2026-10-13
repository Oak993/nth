using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.Testing;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W10, "HTML files get 'Open in Visus'" (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11, 5.2 "which serves it through the preview origin, never through file:"; ED-NEW-web-w10-8). A link to an HTML file inside the chat's project
// gets an "Open in Visus" row that serves the file on the project's preview site; a path outside the project, a hidden folder, another file type, a UNC or device path, a missing file or a dot-dot walk gets none.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-8")]
public sealed class WebW10HtmlFileTests(HeadlessSession headless)
{
    private static string Tree()
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        var root = Path.Combine(data, "HtmlFiles", Guid.NewGuid().ToString("N"), "project");
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        Directory.CreateDirectory(Path.Combine(root, ".hidden"));
        Directory.CreateDirectory(Path.Combine(root, "My Pages"));
        File.WriteAllText(Path.Combine(root, "index.html"), "<p>home</p>");
        File.WriteAllText(Path.Combine(root, "docs", "page.htm"), "<p>page</p>");
        File.WriteAllText(Path.Combine(root, "docs", "notes.txt"), "text");
        File.WriteAllText(Path.Combine(root, ".hidden", "secret.html"), "<p>secret</p>");
        File.WriteAllText(Path.Combine(root, "My Pages", "a#b & c.html"), "<p>odd name</p>");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(root)!, "outside.html"), "<p>outside</p>");
        return root;
    }

    private static SwiftURL Link(string path) => SwiftURL.Create(new Uri(path, UriKind.Absolute).AbsoluteUri)!;

    private static void Clean(string root)
    {
        try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_html_file_inside_the_project_gets_an_address_on_the_preview_site() => headless.OnUiThread(() =>
    {
        var root = Tree();
        try
        {
            var site = new StaticPreviewSite("p1", root);
            Assert.Equal(site.Origin + "/index.html", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "index.html")));
            Assert.Equal(site.Origin + "/docs/page.htm", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "docs", "page.htm")));
            Assert.Equal(site.Origin + "/DOCS/PAGE.HTM", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "DOCS", "PAGE.HTM"))); // the site folds case, as the file system does
            Assert.Equal(site.Origin + "/My%20Pages/a%23b%20%26%20c.html", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "My Pages", "a#b & c.html")));
            Assert.Equal(site.Origin + "/index.html", ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(root, "docs", "..", "index.html")));
        }
        finally
        {
            Clean(root);
        }
    });

    [Fact]
    public void Everything_else_gets_no_address() => headless.OnUiThread(() =>
    {
        var root = Tree();
        try
        {
            var site = new StaticPreviewSite("p1", root);
            string? Ask(string path) => ProjectPreviewHost.HtmlFileAddress(site, path);
            Assert.Null(Ask(Path.Combine(root, "docs", "notes.txt")));                     // not a page
            Assert.Null(Ask(Path.Combine(root, ".hidden", "secret.html")));                // a hidden folder
            Assert.Null(Ask(Path.Combine(root, "missing.html")));                          // not there
            Assert.Null(Ask(Path.Combine(root, "docs")));                                  // a folder
            Assert.Null(Ask(root));                                                        // the project itself
            Assert.Null(Ask(Path.Combine(Path.GetDirectoryName(root)!, "outside.html")));  // beside the project
            Assert.Null(Ask(Path.Combine(root, "..", "outside.html")));                    // a walk out of it
            Assert.Null(Ask(Path.Combine(root, "docs", "..", "..", "outside.html")));
            Assert.Null(Ask(root + "-sibling" + Path.DirectorySeparatorChar + "index.html")); // a folder that only starts with the same letters
            Assert.Null(Ask("index.html"));                                                // not a full path
            Assert.Null(Ask(Path.Combine("docs", "page.htm")));
            Assert.Null(Ask(@"\\host\share\index.html"));                                  // a network path
            Assert.Null(Ask("//host/share/index.html"));
            Assert.Null(Ask(@"\\?\" + Path.Combine(root, "index.html")));                  // a device path
            Assert.Null(Ask(Path.Combine(root, "index.html:stream.html")));                // an alternate stream
            Assert.Null(Ask(""));
        }
        finally
        {
            Clean(root);
        }
    });

    [Fact]
    public void The_chat_adds_Open_in_Visus_to_the_popover_of_a_project_html_link_only() => headless.OnUiThread(() =>
    {
        UiAwait.Of(Task.Run(() => LoginEnvironment.Load())); // completed once on a pool thread, so the rig's own load on the UI thread returns at once
        using var visus = new VisusRig();
        var root = Tree();
        try
        {
            using var chat = new ThreadRuntimeTurnRig(ProviderKind.Claude, projectFolder: root);
            chat.H.Main.OnMain(() =>
            {
                var page = Link(Path.Combine(root, "index.html"));
                var rows = ChatLinks.MenuFor(page, chat.Runtime);
                var row = Assert.Single(rows);
                Assert.Equal(("Open in Visus", "eye"), (row.Title, row.Symbol));

                Assert.Empty(ChatLinks.MenuFor(Link(Path.Combine(root, "docs", "notes.txt")), chat.Runtime));
                Assert.Empty(ChatLinks.MenuFor(Link(Path.Combine(root, ".hidden", "secret.html")), chat.Runtime));
                Assert.Empty(ChatLinks.MenuFor(Link(Path.Combine(Path.GetDirectoryName(root)!, "outside.html")), chat.Runtime));
                Assert.Empty(ChatLinks.MenuFor(SwiftURL.Create("https://example.org/index.html")!, chat.Runtime));
                Assert.Empty(ChatLinks.MenuFor(SwiftURL.Create("mailto:fake@example.org")!, chat.Runtime));

                row.Run();
                var session = VisusHost.SessionFor(chat.Runtime);
                var tab = Assert.Single(session.Tabs);
                Assert.Equal(VisusTabKind.Preview, tab.Kind);
                Assert.StartsWith("https://p-", tab.Url, StringComparison.Ordinal);
                Assert.EndsWith(".invalid/index.html", tab.Url, StringComparison.Ordinal);
                Assert.True(chat.Runtime.Workspace.IsOpen(PaneKind.Browser));
            });
        }
        finally
        {
            Clean(root);
        }
    });

    [Fact]
    public void A_html_link_in_a_side_chat_opens_on_the_main_threads_project_and_workspace() => headless.OnUiThread(() =>
    {
        UiAwait.Of(Task.Run(() => LoginEnvironment.Load()));
        using var visus = new VisusRig();
        using var rig = new SideChatRig();
        rig.H.Main.OnMain(() =>
        {
            rig.MakeSide();
            var folder = rig.Main.Project.Path;
            File.WriteAllText(Path.Combine(folder, "index.html"), "<p>x</p>");
            var rows = ChatLinks.MenuFor(Link(Path.Combine(folder, "index.html")), rig.SideRuntime);
            Assert.Single(rows);
            rows[0].Run();
            Assert.Single(VisusHost.SessionFor(rig.Main.Runtime).Tabs);
            Assert.Empty(VisusHost.SessionFor(rig.SideRuntime).Tabs);
        });
    });
}
