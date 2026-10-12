using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a "nth's Settings: the switch to add or remove it later", unit U8
// (ED-NEW-inst-u8-2). The scan behind the Desktop shortcut switch, on FakeShellLinks: "a shortcut nth made exists", cached per
// file by write time and per folder by write time, cloud placeholders never opened, the same 2,000-link / 3 s cap as a hook,
// and read-only. Nothing here touches the real Desktop.
public sealed class DesktopShortcutProbeTests
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private const string CurrentExe = Root + @"\current\nth.exe";
    private const string Desktop = FakeShellLinks.OneDriveDesktop;
    private const string ProfileDesktop = FakeShellLinks.ProfileDesktop;

    private static ShellLink Made() =>
        new(@"%LOCALAPPDATA%\Programs\nth\current\nth.exe", "", @"%LOCALAPPDATA%\Programs\nth\current", CurrentExe, 0, ShellLinkKeys.Description,
            AppUserModelId: ShellLinkKeys.NthAppUserModelId, CreatedBy: ShellLinkKeys.CreatedByValue);

    // the user's own: into the root, no marker
    private static ShellLink Users() => new(CurrentExe, "--mine", Root + @"\current", CurrentExe, 0, "nth");

    private static ShellLink Unrelated(string name = "Notes") => new(@"C:\Apps\" + name + ".exe", Description: name);

    private static string At(string name, string folder = Desktop) => folder + @"\" + name;

    private static (FakeShellLinks Shell, DesktopShortcutProbe Probe) Machine()
    {
        var shell = new FakeShellLinks();
        shell.AddFile(CurrentExe);
        return (shell, new DesktopShortcutProbe(shell, Root));
    }

    // ---- what counts ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_empty_desktop_has_no_shortcut()
    {
        var (_, probe) = Machine();

        Assert.Equal(new DesktopShortcutProbeResult(Present: false, Capped: false, Reads: 0, Failed: false), probe.Probe());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shortcut_with_nths_marker_counts()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());

        var result = probe.Probe();

        Assert.True(result.Present);
        Assert.Equal(1, result.Reads);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_users_own_shortcut_into_the_root_does_not_count()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Users());
        shell.Seed(At("nth - Shortcut.lnk"), Users());

        Assert.False(probe.Probe().Present);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_foreign_nth_lnk_does_not_count()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), new ShellLink(@"C:\Old\nth\nth.exe"));

        Assert.False(probe.Probe().Present);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shortcut_nth_made_still_counts_after_the_user_renamed_it()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("My assistant.lnk"), Made());
        shell.Seed(At("Notes.lnk"), Unrelated());

        Assert.True(probe.Probe().Present);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_profiles_own_desktop_is_looked_at_too_when_the_desktop_is_redirected()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk", ProfileDesktop), Made());

        Assert.True(probe.Probe().Present);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_machine_without_a_redirected_desktop_looks_at_one_folder()
    {
        var (shell, probe) = Machine();
        shell.Desktop = ProfileDesktop;
        shell.Seed(At("nth.lnk", ProfileDesktop), Made());
        shell.Seed(At("nth.lnk", Desktop), Made()); // the OneDrive folder is not the desktop here

        Assert.True(probe.Probe().Present);
        shell.DeleteLink(At("nth.lnk", ProfileDesktop));
        Assert.False(probe.Probe().Present);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_missing_desktop_folder_is_nothing_to_find_and_not_a_failure()
    {
        var shell = new FakeShellLinks { Desktop = Profile + @"\Nowhere" };
        shell.RemoveFolder(Profile + @"\Nowhere");

        var result = new DesktopShortcutProbe(shell, Root).Probe();

        Assert.False(result.Present);
        Assert.False(result.Failed);
    }

    private const string Profile = FakeShellLinks.Profile;

    // ---- the cache ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_probe_with_nothing_changed_opens_no_link_and_lists_nothing_in_full()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        shell.SeedMany(Desktop, 40);
        probe.Probe();
        shell.ResetCounters();

        var result = probe.Probe();

        Assert.True(result.Present);
        Assert.Equal(0, result.Reads);
        Assert.Equal(0, shell.Reads);
        Assert.Equal(2, shell.CallCount(FakeShellLinks.Call.EnumerateLinks)); // one cheap nth*.lnk listing per desktop folder, no full listing
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_new_link_on_the_desktop_opens_only_that_link()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        shell.SeedMany(Desktop, 40);
        probe.Probe();
        shell.ResetCounters();

        shell.WriteLink(At("Fresh.lnk"), Unrelated("Fresh"));
        shell.ResetCounters();
        var result = probe.Probe();

        Assert.True(result.Present);
        Assert.Equal(1, result.Reads);
        Assert.Equal(new[] { At("Fresh.lnk") }, shell.ReadPaths);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Deleting_the_shortcut_is_seen_on_the_next_probe_without_opening_anything()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        shell.SeedMany(Desktop, 10);
        Assert.True(probe.Probe().Present);

        shell.DeleteLink(At("nth.lnk"));
        shell.ResetCounters();
        var result = probe.Probe();

        Assert.False(result.Present);
        Assert.Equal(0, result.Reads);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Renaming_the_shortcut_opens_it_once_under_its_new_name()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        Assert.True(probe.Probe().Present);

        shell.Rename(At("nth.lnk"), At("Chat.lnk"));
        shell.ResetCounters();
        var result = probe.Probe();

        Assert.True(result.Present);
        Assert.Equal(1, result.Reads);
        shell.ResetCounters();
        Assert.Equal(0, probe.Probe().Reads);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shortcut_replaced_in_place_by_a_foreign_one_is_noticed_although_the_folder_time_did_not_move()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        Assert.True(probe.Probe().Present);

        shell.Seed(At("nth.lnk"), new ShellLink(@"C:\Old\nth\nth.exe")); // same path, new write time, the folder's time untouched
        var result = probe.Probe();

        Assert.False(result.Present);
        Assert.Equal(1, result.Reads);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Invalidate_lists_the_folders_again_but_opens_nothing_that_is_unchanged()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        shell.SeedMany(Desktop, 25);
        probe.Probe();

        probe.Invalidate();
        shell.ResetCounters();
        var result = probe.Probe();

        Assert.True(result.Present);
        Assert.Equal(0, result.Reads);
        Assert.Equal(0, shell.Reads);
        Assert.True(shell.CallCount(FakeShellLinks.Call.EnumerateLinks) > 2); // the folders were listed in full
    }

    // ---- cloud placeholders ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_cloud_placeholder_is_never_opened_counted_or_cached()
    {
        var (shell, probe) = Machine();
        shell.SeedPlaceholder(At("nth.lnk"), Made());
        shell.SeedPlaceholder(At("Anything.lnk"), Unrelated());

        var first = probe.Probe();
        var second = probe.Probe();

        Assert.False(first.Present);
        Assert.False(second.Present);
        Assert.Equal(0, first.Reads);
        Assert.Empty(shell.ReadPaths);
        Assert.Empty(shell.Hydrated);
    }

    // ---- the cap ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_big_desktop_stops_at_2000_opens_and_the_next_probe_carries_on_from_the_cache()
    {
        var (shell, probe) = Machine();
        shell.SeedMany(Desktop, 2500);

        var first = probe.Probe();
        var second = probe.Probe();
        var third = probe.Probe();

        Assert.Equal(new DesktopShortcutProbeResult(false, Capped: true, Reads: 2000, Failed: false), first);
        Assert.Equal(new DesktopShortcutProbeResult(false, Capped: false, Reads: 500, Failed: false), second);
        Assert.Equal(new DesktopShortcutProbeResult(false, Capped: false, Reads: 0, Failed: false), third);
        Assert.Equal(2500, shell.ReadPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // no link was opened twice
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_nth_links_are_opened_first_so_the_cap_reaches_the_likeliest_ones_first()
    {
        var (shell, probe) = Machine();
        shell.SeedMany(Desktop, 2600);
        shell.Seed(At("nth.lnk"), Made());

        var result = probe.Probe();

        Assert.True(result.Capped);
        Assert.True(result.Present);
        Assert.Equal(At("nth.lnk"), shell.ReadPaths[0]);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Slow_reads_stop_the_probe_after_3_seconds()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(by => clock.Advance(by));
        shell.SeedMany(Desktop, 50);
        shell.Delay(FakeShellLinks.Call.ReadLink, TimeSpan.FromSeconds(1));
        var probe = new DesktopShortcutProbe(shell, Root, clock);

        var result = probe.Probe();

        Assert.True(result.Capped);
        Assert.Equal(3, result.Reads);
        Assert.Equal(TimeSpan.FromSeconds(3), clock.Elapsed);
    }

    // ---- errors and read-only ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_listing_that_fails_reports_failed_and_keeps_the_last_answer_and_the_next_probe_recovers()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        Assert.True(probe.Probe().Present);

        shell.Fail(FakeShellLinks.Call.EnumerateLinks, unchecked((int)0x80070005), times: 2);
        var failed = probe.Probe();
        var recovered = probe.Probe();

        Assert.True(failed.Failed);
        Assert.True(failed.Present); // what was known before
        Assert.False(recovered.Failed);
        Assert.True(recovered.Present);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_link_that_cannot_be_read_is_skipped_and_tried_again_next_time()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        shell.Fail(FakeShellLinks.Call.ReadLink, unchecked((int)0x80070020), times: 1, path: At("nth.lnk"));

        var first = probe.Probe();
        shell.ResetCounters();
        var second = probe.Probe();

        Assert.False(first.Present);
        Assert.False(first.Failed);
        Assert.True(second.Present);
        Assert.Equal(1, second.Reads);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_probe_never_writes_deletes_or_notifies()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        shell.Seed(At("nth - Shortcut.lnk"), Users());
        shell.SeedMany(Desktop, 30);

        for (var i = 0; i < 3; i++) probe.Probe();
        probe.Invalidate();
        probe.Probe();

        Assert.Equal(0, shell.Changes);
        Assert.Empty(shell.Notifications);
        Assert.Equal(0, shell.CallCount(FakeShellLinks.Call.WriteLink));
        Assert.Equal(0, shell.CallCount(FakeShellLinks.Call.TryEnterWriter));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_probe_from_many_threads_agrees_and_never_opens_a_link_twice()
    {
        var (shell, probe) = Machine();
        shell.Seed(At("nth.lnk"), Made());
        shell.SeedMany(Desktop, 60);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(probe.Probe)));

        Assert.All(results, r => Assert.True(r.Present));
        Assert.Equal(61, shell.Reads);
    }
}
