using System.Diagnostics;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// nth web surfaces, unit W23 (DESIGN 2.3 and 13.4): the Velopack before-uninstall hook removes %LOCALAPPDATA%\nth\WebView2, the one
// user data folder of every Visus profile, and nothing else. Real files in a disposable folder; Velopack itself is never run.
[Trait("Decision", "ED-NEW-web-w23-3")]
[Trait("Feature", "W-23")]
public sealed class WebDataUninstallTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("nth-w23-uninstall ").FullName;

    private string DataRoot => Path.Combine(temp, "nth");

    private string Web => Path.Combine(DataRoot, "WebView2");

    public void Dispose()
    {
        try
        {
            ClearReadOnly(temp);
            Directory.Delete(temp, recursive: true);
        }
        catch (IOException)
        {
            // a test that held a file released it already; the temp folder is the OS's to clear otherwise
        }
    }

    private static void ClearReadOnly(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(file, FileAttributes.Normal);
    }

    private void SeedWebData()
    {
        Directory.CreateDirectory(Path.Combine(Web, "EBWebView", "browse", "Cache"));
        Directory.CreateDirectory(Path.Combine(Web, "EBWebView", "agent-0123456789abcdef", "Local Storage"));
        File.WriteAllText(Path.Combine(Web, "EBWebView", "browse", "Cookies"), "cookie");
        File.WriteAllText(Path.Combine(Web, "EBWebView", "browse", "Cache", "data_0"), "cache");
        File.WriteAllText(Path.Combine(Web, "EBWebView", "agent-0123456789abcdef", "Local Storage", "leveldb"), "ls");
        File.SetAttributes(Path.Combine(Web, "EBWebView", "browse", "Cache", "data_0"), FileAttributes.ReadOnly);
    }

    private static bool MakeJunction(string link, string target)
    {
        var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.WaitForExit(15_000);
        return process.ExitCode == 0 && Directory.Exists(link);
    }

    [Fact]
    public void The_web_data_folder_and_everything_in_it_goes_read_only_files_too()
    {
        SeedWebData();
        File.WriteAllText(Path.Combine(DataRoot, "settings.json"), "keep");

        var report = WebDataUninstall.BeforeUninstall(Web);

        Assert.True(report.Removed);
        Assert.False(report.Refused);
        Assert.False(report.OutOfTime);
        Assert.Empty(report.Left);
        Assert.Equal(1, report.Attempts);
        Assert.False(Directory.Exists(Web));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(DataRoot, "settings.json")));
    }

    [Fact]
    public void A_folder_that_is_not_there_is_already_done()
    {
        var report = WebDataUninstall.BeforeUninstall(Web);
        Assert.True(report.Removed);
        Assert.Equal(1, report.Attempts);
    }

    [Theory]
    [InlineData("relative\\nth\\WebView2")]
    [InlineData("nth\\WebView2")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_path_that_is_not_fully_qualified_is_refused(string path)
    {
        var report = WebDataUninstall.BeforeUninstall(path);
        Assert.True(report.Refused);
        Assert.False(report.Removed);
    }

    [Theory]
    [InlineData("nth", "browse")]
    [InlineData("nth", "WebView2x")]
    [InlineData("other", "WebView2")]
    [InlineData("Marrow Code", "WebView2")]
    public void Only_a_WebView2_folder_directly_under_nth_is_ever_touched(string parent, string name)
    {
        var folder = Path.Combine(temp, parent, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "precious.txt"), "x");

        var report = WebDataUninstall.BeforeUninstall(folder);

        Assert.True(report.Refused);
        Assert.False(report.Removed);
        Assert.Equal(0, report.Attempts);
        Assert.True(File.Exists(Path.Combine(folder, "precious.txt")));
    }

    [Fact]
    public void The_drive_root_and_the_data_root_are_refused()
    {
        Assert.True(WebDataUninstall.BeforeUninstall(Path.GetPathRoot(temp)!).Refused);
        Assert.True(WebDataUninstall.BeforeUninstall(DataRoot).Refused);
        Assert.True(WebDataUninstall.BeforeUninstall(temp).Refused);
    }

    [Fact]
    public void A_trailing_separator_and_different_letter_case_are_the_same_folder()
    {
        SeedWebData();
        var report = WebDataUninstall.BeforeUninstall(Web.ToUpperInvariant() + Path.DirectorySeparatorChar);
        Assert.True(report.Removed);
        Assert.False(Directory.Exists(Web));
    }

    [Fact]
    public void A_file_the_browser_still_holds_is_waited_for_and_the_rest_goes_meanwhile()
    {
        SeedWebData();
        var held = Path.Combine(Web, "EBWebView", "browse", "Cookies");
        var hold = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var pauses = new List<TimeSpan>();
        var siblingGoneAtFirstPause = false;

        var report = WebDataUninstall.BeforeUninstall(Web, wait: span =>
        {
            pauses.Add(span);
            if (pauses.Count == 1)
            {
                siblingGoneAtFirstPause = !File.Exists(Path.Combine(Web, "EBWebView", "browse", "Cache", "data_0"));
                hold.Dispose();
            }
        });

        Assert.True(report.Removed);
        Assert.Equal(2, report.Attempts);
        Assert.Equal([TimeSpan.FromMilliseconds(250)], pauses);
        Assert.True(siblingGoneAtFirstPause, "what is not held goes on the first pass");
        Assert.False(Directory.Exists(Web));
    }

    [Fact]
    public void A_file_that_stays_held_ends_inside_the_budget_and_is_reported_with_its_ancestors()
    {
        SeedWebData();
        var held = Path.Combine(Web, "EBWebView", "browse", "Cookies");
        using var hold = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var clock = new ManualTimeProvider();
        var pauses = new List<TimeSpan>();

        var report = WebDataUninstall.BeforeUninstall(Web, budget: TimeSpan.FromSeconds(5), clock: clock, wait: span =>
        {
            pauses.Add(span);
            clock.Advance(span);
        });

        Assert.False(report.Removed);
        Assert.True(report.OutOfTime);
        Assert.Contains(held, report.Left);
        Assert.Contains(Web, report.Left);
        Assert.False(File.Exists(Path.Combine(Web, "EBWebView", "browse", "Cache", "data_0")));
        Assert.False(Directory.Exists(Path.Combine(Web, "EBWebView", "agent-0123456789abcdef")));
        // 250, 500, 1000, 1000, ... ms: growing, capped at a second, and never past the 5 s budget.
        Assert.Equal(TimeSpan.FromMilliseconds(250), pauses[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(500), pauses[1]);
        Assert.All(pauses.Skip(2), pause => Assert.Equal(TimeSpan.FromSeconds(1), pause));
        Assert.True(pauses.Aggregate(TimeSpan.Zero, (sum, p) => sum + p) <= TimeSpan.FromSeconds(5));
        Assert.Equal(pauses.Count + 1, report.Attempts);
    }

    [Fact]
    public void A_link_inside_the_tree_is_removed_itself_and_its_target_is_left_alone()
    {
        SeedWebData();
        var target = Path.Combine(temp, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "precious.txt"), "x");
        var link = Path.Combine(Web, "EBWebView", "linked");
        Assert.True(MakeJunction(link, target));

        var report = WebDataUninstall.BeforeUninstall(Web);

        Assert.True(report.Removed);
        Assert.False(Directory.Exists(Web));
        Assert.True(File.Exists(Path.Combine(target, "precious.txt")));
    }

    [Fact]
    public void When_the_web_data_folder_is_itself_a_link_only_the_link_goes()
    {
        var target = Path.Combine(temp, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "precious.txt"), "x");
        Directory.CreateDirectory(DataRoot);
        Assert.True(MakeJunction(Web, target));

        var report = WebDataUninstall.BeforeUninstall(Web);

        Assert.True(report.Removed);
        Assert.False(Directory.Exists(Web));
        Assert.True(File.Exists(Path.Combine(target, "precious.txt")));
    }

    [Fact]
    public void The_budget_leaves_room_for_the_other_uninstall_hooks()
    {
        // Velopack gives a hook 30 s in all; the skills hook may use 20 (SkillsUninstall.DefaultBudget).
        Assert.Equal(TimeSpan.FromSeconds(5), WebDataUninstall.DefaultBudget);
        Assert.True(WebDataUninstall.DefaultBudget + TimeSpan.FromSeconds(20) < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void The_engines_user_data_folder_and_the_hook_are_the_same_folder_under_nth()
    {
        Assert.Equal(WebDataRoot.Resolve(), WebProfiles.DefaultUserDataFolder);
        Assert.True(WebDataUninstall.IsWebDataFolder(WebDataRoot.Resolve()));
        Assert.EndsWith(Path.Combine("nth", "WebView2"), WebDataRoot.Resolve(), StringComparison.Ordinal);
    }
}
