using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Native libraries", "Leftovers"); VELOPACK_CHECK.md findings 3 and 7
// (ED-NEW-inst-fix-5). The uninstaller's copy now unpacks Skia, HarfBuzz and ANGLE beside itself in %TEMP%\nth Uninstall, and a
// power loss can leave a ".partial" write in a runtime build; the sweeps take both as the setup's own, and nothing else.
public sealed class SweepLibrariesTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_uninstallers_copy_leaves_with_the_libraries_it_unpacked_beside_itself()
    {
        area.PutInUninstall("nth Uninstall.exe");
        area.PutInUninstall("libSkiaSharp.dll");
        area.PutInUninstall("libHarfBuzzSharp.dll");
        area.PutInUninstall("av_libglesv2.dll");
        area.PutInUninstall("av_libglesv2.dll.partial");
        area.PutInUninstall("setup.log");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(1, result.Removed);
        Assert.False(Directory.Exists(area.UninstallTemp));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_runtime_build_with_a_cut_short_write_still_goes()
    {
        area.PutInSetup("runtime", "4f9c2a0011223344", "libSkiaSharp.dll");
        area.PutInSetup("runtime", "4f9c2a0011223344", "libHarfBuzzSharp.dll.partial");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(1, result.Removed);
        Assert.False(Directory.Exists(SetupFolders.RuntimeFolder(area.LocalAppData, "4f9c2a0011223344")));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("vcruntime140.dll")]
    [InlineData("libSkiaSharp.dll.old")]
    [InlineData("libSkiaSharp.partial")]
    [InlineData("LIBSKIASHARP.DLL.PARTIAL")]
    public void Any_other_file_beside_the_copy_keeps_its_folder(string other)
    {
        area.PutInUninstall("nth Uninstall.exe");
        area.PutInUninstall("libSkiaSharp.dll");
        var kept = area.PutInUninstall(other);

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(0, result.Removed);
        Assert.True(File.Exists(kept));
    }
}
