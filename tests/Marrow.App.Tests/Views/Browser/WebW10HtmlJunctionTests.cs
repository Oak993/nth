using System.Diagnostics;
using Marrow.App.Views.Browser;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W10 landing check (ED-NEW-web-w10-9; ED-NEW-web-w10-8 point 2 "no reparse point on the way"). An HTML file reached through a junction is
// refused: a junction inside the project can lead anywhere on the PC, so the file it shows is not "strictly under the project's folder" however the path reads.
// The junction is made with the system's mklink /J, which needs no privilege (a system binary, no network).
[Trait("Decision", "ED-NEW-web-w10-9")]
public sealed class WebW10HtmlJunctionTests
{
    private static string Base()
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        return Path.Combine(data, "HtmlJunction", Guid.NewGuid().ToString("N"));
    }

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "mklink did not finish");
        Assert.True(process.ExitCode == 0, "mklink /J failed: " + output);
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), "the link is not a reparse point");
    }

    [Fact]
    public void A_html_file_reached_through_a_junction_inside_the_project_gets_no_address()
    {
        var folder = Base();
        try
        {
            var project = Path.Combine(folder, "project");
            var elsewhere = Path.Combine(folder, "elsewhere");
            Directory.CreateDirectory(project);
            Directory.CreateDirectory(elsewhere);
            File.WriteAllText(Path.Combine(project, "index.html"), "<p>home</p>");
            File.WriteAllText(Path.Combine(elsewhere, "private.html"), "<p>not the project's</p>");
            Junction(Path.Combine(project, "linked"), elsewhere);

            var site = new StaticPreviewSite("p1", project);
            Assert.NotNull(ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(project, "index.html"))); // the project's own file still gets one
            Assert.True(File.Exists(Path.Combine(project, "linked", "private.html")), "the junction does not lead to the file");
            Assert.Null(ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(project, "linked", "private.html")));
        }
        finally
        {
            Clean(folder, Path.Combine(folder, "project", "linked"));
        }
    }

    [Fact]
    public void A_project_folder_that_is_itself_a_junction_serves_nothing()
    {
        var folder = Base();
        try
        {
            var real = Path.Combine(folder, "real");
            var project = Path.Combine(folder, "project");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "index.html"), "<p>home</p>");
            Junction(project, real);

            var site = new StaticPreviewSite("p1", project);
            Assert.Null(ProjectPreviewHost.HtmlFileAddress(site, Path.Combine(project, "index.html")));
        }
        finally
        {
            Clean(folder, Path.Combine(folder, "project"));
        }
    }

    private static void Clean(string folder, string junction)
    {
        try
        {
            // the junction goes first and alone (recursive: false removes the link, not what it points at), so deleting the tree never follows it
            if (Directory.Exists(junction) && new DirectoryInfo(junction).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(junction, recursive: false);
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // a leftover in the throwaway test folder is harmless
        }
        catch (UnauthorizedAccessException)
        {
            // a leftover in the throwaway test folder is harmless
        }
    }
}
