using System.Text.RegularExpressions;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 "Open at sign-in" (--at-sign-in) and 8a, unit U8 (ED-NEW-inst-u8-1 and
// -3). The Run entry starts nth with --at-sign-in: nth has no tray, so it opens like any start, the argument must not read as a
// notification, a launch by that entry never reopens a running nth, and the Settings section's platform is bound only in a
// Velopack layout and only with factories (nothing real is made at binding time). Nothing here starts nth or touches HKCU.
[Collection("Headless")]
public sealed class ProgramAtSignInTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_start_by_the_sign_in_entry_never_reopens_a_running_nth_and_any_other_start_does()
    {
        Assert.False(Program.ShouldReopenRunningInstance(["--at-sign-in"]));
        Assert.False(Program.ShouldReopenRunningInstance(["--other", "--at-sign-in"]));
        Assert.True(Program.ShouldReopenRunningInstance([]));
        Assert.True(Program.ShouldReopenRunningInstance(["--silent"]));
        Assert.True(Program.ShouldReopenRunningInstance(["nth://notification?payload=%7B%7D"]));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_sign_in_argument_is_not_a_notification_activation()
    {
        var identity = ToastBuildIdentity.ForBuild(debug: false, dev: false);

        Assert.Null(ToastActivation.Decode(identity, SignInEntry.Argument));
        Assert.Null(ToastActivation.DevRoot(identity, SignInEntry.Argument));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_second_instance_path_asks_before_it_reopens()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Marrow.App", "Program.cs"));

        Assert.Matches(new Regex(@"if \(ShouldReopenRunningInstance\(args\)\) ReopenSignal\.Send\(", RegexOptions.CultureInvariant), source);
        Assert.DoesNotMatch(new Regex(@"^\s*ReopenSignal\.Send\(", RegexOptions.Multiline | RegexOptions.CultureInvariant), source);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Outside_a_Velopack_layout_the_Settings_section_stays_unavailable()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(temp, "app")).FullName; // no Update.exe one folder above

            Program.BindOnThisPc(folder + Path.DirectorySeparatorChar);

            Assert.False(OnThisPcPlatform.Slot.Current.IsAvailable);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void In_a_Velopack_layout_the_platform_is_bound_to_the_root_and_makes_nothing_until_used()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(temp, "nth")).FullName;
            var current = Directory.CreateDirectory(Path.Combine(root, "current")).FullName;
            File.WriteAllBytes(Path.Combine(root, "Update.exe"), []);

            Program.BindOnThisPc(current + Path.DirectorySeparatorChar);

            var platform = OnThisPcPlatform.Slot.Current;
            Assert.True(platform.IsAvailable);
            Assert.Equal(root, platform.Root);
            Assert.NotSame(OnThisPcPlatform.Unavailable, platform);
            // the real shell and registry are factories: nothing real exists until the Settings worker calls them
        }
        finally
        {
            OnThisPcPlatform.Slot.SetProcessDefault(OnThisPcPlatform.Unavailable);
            Cleanup(temp);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }

    private static void Cleanup(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { } // a temporary folder; Windows cleans %TEMP%
    }
}
