using System.Runtime.InteropServices;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a and 10, unit U0 (ED-NEW-inst-u0-1). FakeShellLinks is the machine
// every desktop-shortcut test of units U2, U5, U6 and U8 runs on, so its own behaviour is pinned here: the long non-ASCII
// OneDrive profile, whole links that read back, changes that keep what they do not name, listings that never open a link
// (so a cloud placeholder stays one until it is read), HRESULT failures and delays on any call, counts of reads, writes,
// deletes and notifications, and a one-writer lock. Nothing here touches the real Desktop or Start menu.
public sealed class FakeShellLinksTests
{
    private const int AccessDenied = unchecked((int)0x80070005);

    private static readonly ShellLink Full = new(
        @"%LOCALAPPDATA%\Programs\nth\current\nth.exe",
        Arguments: "--from-desktop",
        WorkingDirectory: @"%LOCALAPPDATA%\Programs\nth\current",
        IconPath: @"%LOCALAPPDATA%\Programs\nth\current\nth.exe",
        IconIndex: 0,
        Description: "nth",
        AppUserModelId: ShellLinkKeys.NthAppUserModelId,
        ToastActivatorClsid: new Guid("5D2C3F33-8E5A-4A8B-9C4F-7B1E2A3C4D5E"),
        CreatedBy: ShellLinkKeys.CreatedByValue);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_fake_machine_has_a_long_non_ASCII_profile_and_a_OneDrive_desktop()
    {
        var shell = new FakeShellLinks();

        Assert.Equal(@"C:\Users\Ana-María Østergård-Fernández\OneDrive\Desktop", shell.KnownFolder(ShellKnownFolder.Desktop));
        Assert.Equal(@"C:\Users\Ana-María Østergård-Fernández\AppData\Roaming\Microsoft\Windows\Start Menu\Programs", shell.KnownFolder(ShellKnownFolder.Programs));
        Assert.Equal(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local", shell.KnownFolder(ShellKnownFolder.LocalAppData));
        Assert.Equal(@"C:\Users\Ana-María Østergård-Fernández", shell.KnownFolder(ShellKnownFolder.Profile));
        Assert.Equal(FakeShellLinks.DefaultRoot + @"\current\nth.exe", shell.ExpandEnvironment(@"%LOCALAPPDATA%\Programs\nth\current\nth.exe"));
        Assert.Equal(FakeShellLinks.DefaultRoot, shell.ExpandEnvironment(@"%localappdata%\Programs\nth"));
        Assert.Equal(@"%NOT_SET%\x", shell.ExpandEnvironment(@"%NOT_SET%\x"));
        Assert.NotNull(shell.FolderWriteTimeUtc(FakeShellLinks.ProfileDesktop));

        shell.Desktop = FakeShellLinks.ProfileDesktop;
        Assert.Equal(FakeShellLinks.ProfileDesktop, shell.KnownFolder(ShellKnownFolder.Desktop));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_written_link_reads_back_whole_its_raw_path_kept_and_every_step_is_counted()
    {
        var shell = new FakeShellLinks();
        var before = shell.FolderWriteTimeUtc(FakeShellLinks.OneDriveDesktop);

        shell.WriteLink(FakeShellLinks.DesktopLink, Full);
        var listed = Assert.Single(shell.EnumerateLinks(FakeShellLinks.OneDriveDesktop));
        var read = shell.ReadLink(FakeShellLinks.DesktopLink);

        Assert.Equal(Full, read);
        Assert.StartsWith("%LOCALAPPDATA%", read.Target, StringComparison.Ordinal);
        Assert.True(read.IsMadeByNth);
        Assert.Equal(FakeShellLinks.DesktopLink, listed.Path);
        Assert.False(listed.IsCloudPlaceholder);
        Assert.True(shell.FolderWriteTimeUtc(FakeShellLinks.OneDriveDesktop) > before);
        Assert.True(shell.FileExists(FakeShellLinks.DesktopLink));
        Assert.Equal((1, 1, 0), (shell.Writes, shell.Reads, shell.Deletes));
        Assert.Equal(new[] { FakeShellLinks.DesktopLink }, shell.ReadPaths);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_change_sets_only_its_fields_and_keeps_the_toast_activator_and_the_marker()
    {
        var shell = new FakeShellLinks();
        shell.Seed(FakeShellLinks.StartLink, Full with { AppUserModelId = "velopack.NthApp", CreatedBy = null });

        shell.UpdateLink(FakeShellLinks.StartLink, new ShellLinkChange(AppUserModelId: ShellLinkKeys.NthAppUserModelId));
        shell.UpdateLink(FakeShellLinks.StartLink, new ShellLinkChange());

        var after = shell.Peek(FakeShellLinks.StartLink)!;
        Assert.Equal(Full with { CreatedBy = null }, after);
        Assert.Equal(Full.ToastActivatorClsid, after.ToastActivatorClsid);
        Assert.Equal(1, shell.Writes); // an empty change writes nothing
        Assert.Equal(0, shell.Reads);  // Peek is not a read

        shell.UpdateLink(FakeShellLinks.StartLink, new ShellLinkChange(Target: @"C:\Other\nth.exe", WorkingDirectory: @"C:\Other", IconPath: @"C:\Other\nth.exe", IconIndex: 2));
        Assert.Equal(
            Full with { CreatedBy = null, Target = @"C:\Other\nth.exe", WorkingDirectory = @"C:\Other", IconPath = @"C:\Other\nth.exe", IconIndex = 2 },
            shell.Peek(FakeShellLinks.StartLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Listing_never_opens_a_link_so_a_placeholder_stays_one_until_it_is_read()
    {
        var shell = new FakeShellLinks();
        var path = FakeShellLinks.OneDriveDesktop + @"\Synced.lnk";
        shell.SeedPlaceholder(path, Full);

        var listed = Assert.Single(shell.EnumerateLinks(FakeShellLinks.OneDriveDesktop));
        Assert.True(listed.IsCloudPlaceholder);
        Assert.Equal(0, shell.Reads);
        Assert.Empty(shell.Hydrated);

        shell.ReadLink(path);

        Assert.Equal(new[] { path }, shell.Hydrated);
        Assert.False(Assert.Single(shell.EnumerateLinks(FakeShellLinks.OneDriveDesktop)).IsCloudPlaceholder);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Listing_matches_the_pattern_in_name_order_in_that_folder_only()
    {
        var shell = new FakeShellLinks();
        var desktop = FakeShellLinks.OneDriveDesktop;
        shell.Seed(desktop + @"\nth.lnk", Full);
        shell.Seed(desktop + @"\NTH (2).lnk", Full);
        shell.Seed(desktop + @"\Notes.lnk", Full);
        shell.Seed(desktop + @"\Tools\nth.lnk", Full);
        shell.AddFile(desktop + @"\nth.txt");

        Assert.Equal(
            new[] { desktop + @"\NTH (2).lnk", desktop + @"\nth.lnk" },
            shell.EnumerateLinks(desktop + @"\", "nth*.lnk").Select(f => f.Path));
        Assert.Equal(
            new[] { desktop + @"\Notes.lnk", desktop + @"\NTH (2).lnk", desktop + @"\nth.lnk" },
            shell.EnumerateLinks(desktop).Select(f => f.Path));
        Assert.Equal(new[] { desktop + @"\nth.lnk" }, shell.EnumerateLinks(desktop, "nth.lnk").Select(f => f.Path));
        Assert.Empty(shell.EnumerateLinks(@"C:\Nowhere"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Any_call_can_fail_with_a_chosen_HRESULT_for_a_number_of_calls_or_one_path()
    {
        var shell = new FakeShellLinks();
        shell.Seed(FakeShellLinks.StartLink, Full);
        shell.Fail(FakeShellLinks.Call.WriteLink, AccessDenied, times: 1);
        shell.Fail(FakeShellLinks.Call.ReadLink, unchecked((int)0x80004005), path: FakeShellLinks.StartLink);

        Assert.Equal(AccessDenied, Assert.Throws<COMException>(() => shell.WriteLink(FakeShellLinks.DesktopLink, Full)).HResult);
        shell.WriteLink(FakeShellLinks.DesktopLink, Full); // only once
        Assert.Equal(unchecked((int)0x80004005), Assert.Throws<COMException>(() => shell.ReadLink(FakeShellLinks.StartLink)).HResult);
        Assert.Equal(Full, shell.ReadLink(FakeShellLinks.DesktopLink)); // only that path
        Assert.Equal(2, shell.CallCount(FakeShellLinks.Call.WriteLink));
        Assert.Equal(1, shell.Writes);

        shell.ClearFaults();
        Assert.Equal(Full, shell.ReadLink(FakeShellLinks.StartLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Writing_into_a_missing_folder_over_a_read_only_link_or_reading_a_missing_one_fails_as_Windows_does()
    {
        var shell = new FakeShellLinks();
        shell.Seed(FakeShellLinks.DesktopLink, Full, FileAttributes.Archive | FileAttributes.ReadOnly);

        Assert.Equal(unchecked((int)0x80070003), Assert.Throws<DirectoryNotFoundException>(() => shell.WriteLink(@"C:\Missing\nth.lnk", Full)).HResult);
        Assert.Equal(AccessDenied, Assert.Throws<UnauthorizedAccessException>(() => shell.WriteLink(FakeShellLinks.DesktopLink, Full)).HResult);
        Assert.Equal(AccessDenied, Assert.Throws<UnauthorizedAccessException>(() => shell.UpdateLink(FakeShellLinks.DesktopLink, new ShellLinkChange(AppUserModelId: "x"))).HResult);
        Assert.Equal(unchecked((int)0x80070002), Assert.Throws<FileNotFoundException>(() => shell.ReadLink(FakeShellLinks.StartLink)).HResult);
        Assert.Equal(0, shell.Changes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_delayed_call_waits_on_the_clock_the_test_gives()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(d => clock.Advance(d));
        shell.SeedMany(FakeShellLinks.OneDriveDesktop, 5);
        shell.Delay(FakeShellLinks.Call.ReadLink, TimeSpan.FromMilliseconds(10));

        foreach (var link in shell.EnumerateLinks(FakeShellLinks.OneDriveDesktop)) shell.ReadLink(link.Path);

        Assert.Equal(TimeSpan.FromMilliseconds(50), clock.Elapsed);
        Assert.Equal(5, shell.Reads);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Ten_thousand_links_can_be_seeded_and_listed_without_a_read()
    {
        var shell = new FakeShellLinks();

        shell.SeedMany(FakeShellLinks.OneDriveDesktop, 10_000);
        var all = shell.EnumerateLinks(FakeShellLinks.OneDriveDesktop).ToList();

        Assert.Equal(10_000, all.Count);
        Assert.Equal(FakeShellLinks.OneDriveDesktop + @"\link-00001.lnk", all[0].Path);
        Assert.Equal(0, shell.Reads);
        Assert.Equal(0, shell.Writes); // seeding is the machine as found, not a write
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_writer_lock_admits_one_writer_at_a_time_and_releases_once()
    {
        var shell = new FakeShellLinks();

        var first = shell.TryEnterWriter(TimeSpan.Zero);
        Assert.NotNull(first);
        Assert.Null(shell.TryEnterWriter(TimeSpan.FromMilliseconds(20)));
        Assert.Equal(1, shell.WriterTimeouts);

        first!.Dispose();
        first.Dispose(); // a second dispose must not let two writers in
        var second = shell.TryEnterWriter(TimeSpan.Zero);
        Assert.NotNull(second);
        Assert.Null(shell.TryEnterWriter(TimeSpan.Zero));
        second!.Dispose();
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Deletes_notifications_and_renames_are_recorded_and_a_rename_keeps_every_field()
    {
        var shell = new FakeShellLinks();
        var renamed = FakeShellLinks.OneDriveDesktop + @"\My nth.lnk";
        shell.Seed(FakeShellLinks.DesktopLink, Full);

        shell.Rename(FakeShellLinks.DesktopLink, renamed);
        Assert.Null(shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(Full, shell.Peek(renamed));

        shell.DeleteLink(renamed);
        shell.DeleteLink(renamed); // already gone: not an error, not counted
        shell.Notify(ShellChange.Deleted, renamed);

        Assert.Equal(1, shell.Deletes);
        Assert.Equal(1, shell.Changes);
        Assert.Equal(new[] { (ShellChange.Deleted, renamed) }, shell.Notifications);
        Assert.Empty(shell.LinksIn(FakeShellLinks.OneDriveDesktop));

        shell.IconsHidden = true;
        Assert.True(shell.DesktopIconsHidden());
        shell.ResetCounters();
        Assert.Equal((0, 0, 0), (shell.Deletes, shell.Changes, shell.Notifications.Count));
    }
}
