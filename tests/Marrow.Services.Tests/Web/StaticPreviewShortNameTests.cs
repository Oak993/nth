using System.Runtime.InteropServices;
using System.Text;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: final security gate S3 (ED-NEW-secfinal-21; ED-NEW-web-w9cx-1). The project preview refuses a leading-dot name (.env, .git, .npmrc),
// but it read the text of the request, and Windows also opens a file through its 8.3 alias: /ENV~1 is .env, /GIT~1/config is .git/config. A previewed
// page of an untrusted repository could fetch the project's secrets that way. The request is now resolved to its real long path and the same
// hidden-name rule runs on that; a segment that is not spelled exactly as the file system names it (any alias) is refused.
// Two ways to show it: the real file system (the process temp volume, when it has 8.3 names), and a fake long-path mapping for a volume
// that has none (the files then carry the alias as their own name, and the mapping says what the file system would call them).
[Trait("Decision", "ED-NEW-secfinal-21")]
public sealed class StaticPreviewShortNameTests
{
    private const string Secret = "TOKEN=sentinel-secret";

    private static string Fresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "secfinal-short-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<p>fixture</p>");
        return root;
    }

    private static WebResourceResponse Get(StaticPreviewSite site, string path) => site.Answer(new(site.Origin + path, "GET", WebResourceKind.Fetch))!;

    private static string Body(WebResourceResponse answer) => Encoding.UTF8.GetString(answer.Body);

    // The 8.3 name a file has on its volume (its last segment), or its own long name when the volume gives it none.
    private static string ShortLeaf(string path)
    {
        var buffer = new StringBuilder(520);
        return GetShortPathNameW(path, buffer, buffer.Capacity) is > 0 and < 520 ? Path.GetFileName(buffer.ToString()) : Path.GetFileName(path);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

    // What GetLongPathName would answer: the path with each listed alias segment replaced by its long name; any other path as it is.
    private static Func<string, string?> Mapping(string root, params (string Alias, string Long)[] aliases) => path =>
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || relative == ".") return path;
        var segments = relative.Split(Path.DirectorySeparatorChar).Select(segment =>
            aliases.FirstOrDefault(a => string.Equals(a.Alias, segment, StringComparison.OrdinalIgnoreCase)).Long ?? segment);
        return Path.Combine(root, string.Join(Path.DirectorySeparatorChar, segments));
    };

    [Fact]
    public void A_dot_file_and_a_dot_folder_are_not_served_through_their_real_short_names_where_the_volume_has_them()
    {
        var root = Fresh();
        try
        {
            File.WriteAllText(Path.Combine(root, ".env"), Secret);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, ".git", "config"), "url = https://user:sentinel-secret@example.test/repo.git");
            var site = new StaticPreviewSite("fixture", root);
            var envAlias = ShortLeaf(Path.Combine(root, ".env"));
            var gitAlias = ShortLeaf(Path.Combine(root, ".git"));

            // With 8.3 names on the volume these are the aliases (ENV~1, GIT~1); without them, the dot names themselves: refused either way.
            foreach (var requested in new[] { "/" + envAlias, "/" + gitAlias + "/config", "/" + envAlias.ToLowerInvariant(), "/" + gitAlias + "/CONFIG" })
            {
                var answer = Get(site, requested);
                Assert.Equal(403, answer.StatusCode);
                Assert.DoesNotContain("sentinel-secret", Body(answer), StringComparison.Ordinal);
            }
            Assert.Equal(200, Get(site, "/index.html").StatusCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_dot_file_and_a_dot_folder_are_not_served_through_a_short_name_the_file_system_resolves_to_them()
    {
        var root = Fresh();
        try
        {
            // The files stand under their alias because this volume gives no aliases; the mapping is what Windows would answer.
            File.WriteAllText(Path.Combine(root, "ENV~1"), Secret);
            Directory.CreateDirectory(Path.Combine(root, "GIT~1"));
            File.WriteAllText(Path.Combine(root, "GIT~1", "config"), "url = https://user:sentinel-secret@example.test/repo.git");
            var site = new StaticPreviewSite("fixture", root, Mapping(root, ("ENV~1", ".env"), ("GIT~1", ".git")));

            foreach (var requested in new[] { "/ENV~1", "/env~1", "/GIT~1/config", "/git~1/CONFIG", "/GIT~1/./config" })
            {
                var answer = Get(site, requested);
                Assert.Equal(403, answer.StatusCode);
                Assert.DoesNotContain("sentinel-secret", Body(answer), StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_short_name_for_an_ordinary_file_is_refused_but_the_long_name_and_a_tilde_in_a_real_name_are_served()
    {
        var root = Fresh();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "PROJEC~1"));
            File.WriteAllText(Path.Combine(root, "PROJEC~1", "page.txt"), "ordinary");
            Directory.CreateDirectory(Path.Combine(root, "project-files"));
            File.WriteAllText(Path.Combine(root, "project-files", "page.txt"), "ordinary");
            File.WriteAllText(Path.Combine(root, "my~notes.txt"), "tilde");
            var site = new StaticPreviewSite("fixture", root, Mapping(root, ("PROJEC~1", "project-files")));

            Assert.Equal(403, Get(site, "/PROJEC~1/page.txt").StatusCode);
            Assert.Equal(200, Get(site, "/project-files/page.txt").StatusCode);
            Assert.Equal(200, Get(site, "/PROJECT-FILES/PAGE.TXT").StatusCode);
            Assert.Equal("tilde", Body(Get(site, "/my~notes.txt")));
            Assert.Equal(200, Get(site, "/").StatusCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_path_the_file_system_cannot_name_or_that_resolves_outside_the_folder_is_not_served()
    {
        var root = Fresh();
        try
        {
            File.WriteAllText(Path.Combine(root, "gone.txt"), "x");
            File.WriteAllText(Path.Combine(root, "away.txt"), "x");
            var outside = Path.Combine(Path.GetDirectoryName(root)!, "elsewhere", "away.txt");
            string? Resolve(string path) =>
                path.EndsWith("gone.txt", StringComparison.OrdinalIgnoreCase) ? null
                : path.EndsWith("away.txt", StringComparison.OrdinalIgnoreCase) ? outside
                : path;
            var site = new StaticPreviewSite("fixture", root, Resolve);

            Assert.Equal(404, Get(site, "/gone.txt").StatusCode);
            Assert.Equal(404, Get(site, "/away.txt").StatusCode);
            Assert.Equal(200, Get(site, "/index.html").StatusCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_real_segments_of_a_path_are_the_names_below_the_folder()
    {
        var root = Fresh();
        try
        {
            var site = new StaticPreviewSite("fixture", root, Mapping(root, ("ENV~1", ".env"), ("GIT~1", ".git")));

            Assert.Equal([".git", "config"], site.RealSegments(Path.Combine(root, "GIT~1", "config")));
            Assert.Equal(["docs", "a.html"], site.RealSegments(Path.Combine(root, "docs", "a.html")));
            Assert.Null(site.RealSegments(Path.Combine(Path.GetDirectoryName(root)!, "other.html")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
