using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, unit U0 (ED-NEW-inst-u0-1). The fixed parts of the shell-link
// contract that units U2, U5, U6 and U8 build on: the shortcut's exact name and description, the property keys (the
// AppUserModelID and toast activator keys as propkey.h defines them, nth's own marker key), what counts as "made by nth",
// what counts as a cloud placeholder, the one-writer mutex and the hook's scan cap. That the AppUserModelID constant is
// the one nth gives its own process is checked where that ID lives (Marrow.App.Tests, ShellLinkAppUserModelIdTests).
public sealed class ShellLinkContractTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_shortcut_is_named_nth_lnk_and_describes_itself_as_nth()
    {
        Assert.Equal("nth.lnk", ShellLinkKeys.LinkFileName);
        Assert.Equal("nth", ShellLinkKeys.Description);
        Assert.Equal(1, ShellLinkKeys.ShowNormal); // SW_SHOWNORMAL
        Assert.Equal(1, new ShellLink(@"C:\x.exe").ShowCommand);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_property_keys_are_the_shells_own_and_nths_marker_has_a_fixed_key()
    {
        // propkey.h: PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5; ..._ToastActivatorCLSID = same format id, 26
        Assert.Equal(new Guid("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), ShellLinkKeys.AppUserModelFormat);
        Assert.Equal(5, ShellLinkKeys.AppUserModelIdPid);
        Assert.Equal(26, ShellLinkKeys.ToastActivatorClsidPid);
        // nth's own format id never changes: every shortcut nth ever made is found by it
        Assert.Equal(new Guid("c514cba9-8e86-4c38-94e1-87e24d26bdd3"), ShellLinkKeys.NthFormat);
        Assert.Equal(2, ShellLinkKeys.CreatedByPid);
        Assert.Equal("nth-setup/1", ShellLinkKeys.CreatedByValue);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("nth-setup/1", true)]
    [InlineData("nth-setup/2", true)]
    [InlineData("nth-setup/", true)]
    [InlineData("NTH-SETUP/1", false)]
    [InlineData("nth-setup", false)]
    [InlineData("velopack", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_nths_marker_makes_a_shortcut_nths(string? marker, bool made) =>
        Assert.Equal(made, new ShellLink(@"C:\x.exe", CreatedBy: marker).IsMadeByNth);

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(0x00000020, false)] // archive
    [InlineData(0x00001000, false)] // offline alone is not a recall placeholder
    [InlineData(0x00040000, true)]  // FILE_ATTRIBUTE_RECALL_ON_OPEN
    [InlineData(0x00400000, true)]  // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    [InlineData(0x00401020, true)]
    public void A_cloud_placeholder_is_one_with_a_recall_attribute(int attributes, bool placeholder) =>
        Assert.Equal(placeholder, new ShellLinkFile(@"C:\d\x.lnk", (FileAttributes)attributes, DateTime.UnixEpoch).IsCloudPlaceholder);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_change_with_no_field_is_empty_and_any_one_field_makes_it_a_change()
    {
        Assert.True(new ShellLinkChange().IsEmpty);
        Assert.False(new ShellLinkChange(Target: "t").IsEmpty);
        Assert.False(new ShellLinkChange(WorkingDirectory: "w").IsEmpty);
        Assert.False(new ShellLinkChange(IconPath: "i").IsEmpty);
        Assert.False(new ShellLinkChange(IconIndex: 0).IsEmpty);
        Assert.False(new ShellLinkChange(AppUserModelId: "a").IsEmpty);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void One_writer_at_a_time_and_a_capped_scan_inside_a_hook()
    {
        Assert.Equal(@"Local\nth-desktop-shortcut", ShellLinkKeys.WriterMutexName);
        Assert.Equal(TimeSpan.FromSeconds(2), ShellLinkKeys.WriterWait);
        Assert.Equal(2000, ShellLinkKeys.ScanCapLinks);
        Assert.Equal(TimeSpan.FromSeconds(3), ShellLinkKeys.ScanCapTime);
    }
}
