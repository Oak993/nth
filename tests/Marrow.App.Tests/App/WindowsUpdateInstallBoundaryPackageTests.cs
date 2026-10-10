using System.IO.Compression;
using Marrow.App.Port;
using Xunit;

namespace Marrow.App.Tests.App;

// UE-1 (OD-0062; closes UNK-0009): Verify and ReadVersion on a Velopack full package, the Windows release asset. Velopack's UpdateManager has
// checked the feed's checksum when the download ends; the boundary checks it is Marrow's package (the nuspec id NthApp) and reads its version.
public sealed class WindowsUpdateInstallBoundaryPackageTests
{
    private static string Package(string id, string version, string name = "NthApp-1.8.0-full.nupkg")
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("aut-pkg-").FullName, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("NthApp.nuspec").Open());
        writer.Write($"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{version}</version></metadata></package>");
        return path;
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void ReadVersion_gives_the_version_the_package_names()
    {
        Assert.Equal("1.8.0", new WindowsUpdateInstallBoundary().ReadVersion(Package("NthApp", "1.8.0")));
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void ReadVersion_of_a_file_that_is_not_a_package_is_null_so_the_install_reports_an_unknown_version()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("aut-pkg-").FullName, "x.nupkg");
        File.WriteAllText(path, "not a zip");
        Assert.Null(new WindowsUpdateInstallBoundary().ReadVersion(path));
        Assert.Null(new WindowsUpdateInstallBoundary().ReadVersion(Path.ChangeExtension(path, ".exe")));
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void Verify_accepts_marrows_package_without_an_Authenticode_signature()
    {
        new WindowsUpdateInstallBoundary().Verify(Package("NthApp", "1.8.0")); // must not throw: nothing is signed (ED-235)
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void Verify_refuses_a_package_of_another_app_and_a_file_that_is_not_a_package()
    {
        var other = Assert.Throws<UpdateInstallError>(() => new WindowsUpdateInstallBoundary().Verify(Package("SomethingElse", "1.8.0")));
        Assert.StartsWith("The downloaded app is not signed by Nth. ", other.ErrorDescription, StringComparison.Ordinal);
        var junk = Path.Combine(Directory.CreateTempSubdirectory("aut-pkg-").FullName, "y.nupkg");
        File.WriteAllText(junk, "not a zip");
        Assert.Throws<UpdateInstallError>(() => new WindowsUpdateInstallBoundary().Verify(junk));
    }
}
