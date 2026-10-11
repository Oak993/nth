using Marrow.Setup.Smoke;

namespace Marrow.Setup.Tests.Smoke;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, "Startup time"; unit U1 (ED-NEW-inst-u1-1). --ui-smoke shows the
// window, checks it, writes a report and exits; it is read only before the app-argument separator, and its report goes
// to the temp folder unless a path follows the flag. These tests read the arguments and the report format only: the
// smoke run itself (a real window on this PC) is the lead's to run.
public sealed class UiSmokeTests
{
    private const string Temp = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Temp";

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_the_flag_there_is_no_smoke_run()
    {
        Assert.Null(UiSmoke.Read([], Temp));
        Assert.Null(UiSmoke.Read(["--silent"], Temp));
        Assert.Null(UiSmoke.Read(["--", "--ui-smoke"], Temp));
        Assert.Null(UiSmoke.Read(["--UI-SMOKE"], Temp));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_report_goes_to_the_temp_folder_unless_a_path_follows_the_flag()
    {
        Assert.Equal(Temp + @"\nth-setup-ui-smoke.txt", UiSmoke.Read(["--ui-smoke"], Temp)!.ReportPath);
        Assert.Equal(Temp + @"\nth-setup-ui-smoke.txt", UiSmoke.Read(["--ui-smoke", "--verbose"], Temp)!.ReportPath);
        Assert.Equal(@"D:\reports\smoke.txt", UiSmoke.Read(["--ui-smoke", @"D:\reports\smoke.txt"], Temp)!.ReportPath);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_report_lists_facts_then_checks_then_the_summary()
    {
        var report = UiSmoke.Report(
            [new SmokeCheck("hit-close", true, "got 20"), new SmokeCheck("close-paused", false, "open False")],
            new SortedDictionary<string, string>(StringComparer.Ordinal) { ["first-frame-ms-since-main"] = "212" });

        Assert.Equal("INFO first-frame-ms-since-main: 212\nPASS hit-close: got 20\nFAIL close-paused: open False\nFAILED 1/2\n", report);
        Assert.EndsWith("PASSED 1/1\n", UiSmoke.Report([new SmokeCheck("a", true, "")], new Dictionary<string, string>()), StringComparison.Ordinal);
    }
}
