using Avalonia.Media;
using Marrow.Setup.Platform;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Platform;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Window details"; unit U1 (ED-NEW-inst-u1-1). The window follows
// Windows' Animation effects, contrast theme, light or dark apps and Text size while it is open: every settings message
// makes it read them again, and only a real change is passed on.
public sealed class SystemSettingsTests
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_refresh_passes_on_only_a_real_change()
    {
        var source = new FakeLookSource(SystemLook.Default);
        var settings = new SystemSettings(source);
        var seen = new List<SystemLook>();
        settings.Changed += seen.Add;

        settings.Refresh();
        Assert.Empty(seen);

        source.Look = SystemLook.Default with { Animations = false };
        settings.Refresh();
        settings.Refresh();

        Assert.Single(seen);
        Assert.True(settings.Current.Calm);
        Assert.Equal(4, source.Reads); // once when made, once per refresh
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Settings_theme_and_colour_messages_make_the_window_read_again()
    {
        Assert.True(SystemSettings.IsSettingMessage(0x001A)); // WM_SETTINGCHANGE (Animation effects, contrast, light or dark)
        Assert.True(SystemSettings.IsSettingMessage(0x0015)); // WM_SYSCOLORCHANGE
        Assert.True(SystemSettings.IsSettingMessage(0x031A)); // WM_THEMECHANGED
        Assert.True(SystemSettings.IsSettingMessage(0x031E)); // WM_DWMCOMPOSITIONCHANGED
        Assert.False(SystemSettings.IsSettingMessage(0x0200)); // WM_MOUSEMOVE
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Text_size_is_read_as_a_scale_from_100_to_225_percent()
    {
        Assert.Equal(1.0, WindowsSystemLookSource.TextScale(100));
        Assert.Equal(1.5, WindowsSystemLookSource.TextScale(150));
        Assert.Equal(2.25, WindowsSystemLookSource.TextScale(225));
        Assert.Equal(1.0, WindowsSystemLookSource.TextScale(0));
        Assert.Equal(2.25, WindowsSystemLookSource.TextScale(400));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Windows_colours_are_read_from_COLORREF_and_Windows_11_starts_at_build_22000()
    {
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), WindowsSystemLookSource.FromColorRef(0x00332211));
        Assert.False((SystemLook.Default with { WindowsBuild = 19045 }).IsWindows11);
        Assert.True((SystemLook.Default with { WindowsBuild = 22000 }).IsWindows11);
        Assert.False(SystemLook.Default.Calm);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void The_real_source_reads_this_PC_without_changing_anything()
    {
        var look = new WindowsSystemLookSource().Read();

        Assert.InRange(look.TextScale, 1.0, 2.25);
        Assert.True(look.WindowsBuild >= 10240);
    }
}
