using System.IO.Compression;
using Marrow.Core.Port.SelfUpdate;

namespace Marrow.Core.Tests.Port.SelfUpdate;

// UE-1 (closes UNK-0009): the Windows release asset is Velopack's full .nupkg; its nuspec names the app and the version.
public sealed class VelopackPackageTests
{
    private static string Package(string nuspec, string entryName = "NthApp.nuspec")
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ue1-pkg-").FullName, "NthApp-1.8.0-full.nupkg");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open())) writer.Write(nuspec);
        using (var writer = new StreamWriter(zip.CreateEntry("lib/app/Marrow.App.exe").Open())) writer.Write("MZ");
        return path;
    }

    private const string Nuspec = "<?xml version=\"1.0\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata>"
        + "<id>NthApp</id><version>1.8.0</version><title>nth</title></metadata></package>";

    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public void The_nuspec_gives_the_id_and_the_version() =>
        Assert.Equal(("NthApp", "1.8.0"), VelopackPackage.ReadIdentity(Package(Nuspec)));

    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_nuspec_without_a_namespace_reads_the_same() =>
        Assert.Equal(("NthApp", "2.0.1-beta.1"), VelopackPackage.ReadIdentity(Package("<package><metadata><id>NthApp</id><version> 2.0.1-beta.1 </version></metadata></package>")));

    [Theory]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("<package><metadata><id>NthApp</id></metadata></package>")]            // no version
    [InlineData("<package><metadata><version>1.0.0</version></metadata></package>")]       // no id
    [InlineData("<package><metadata><id></id><version>1.0.0</version></metadata></package>")]
    [InlineData("this is not xml")]
    public void A_nuspec_without_both_facts_is_not_a_package(string nuspec) =>
        Assert.Null(VelopackPackage.ReadIdentity(Package(nuspec)));

    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public void A_nuspec_in_a_folder_does_not_count_and_a_file_that_is_not_a_zip_or_is_missing_is_not_a_package()
    {
        Assert.Null(VelopackPackage.ReadIdentity(Package(Nuspec, "lib/NthApp.nuspec")));
        var text = Path.Combine(Directory.CreateTempSubdirectory("ue1-pkg-").FullName, "x.nupkg");
        File.WriteAllText(text, "not a zip");
        Assert.Null(VelopackPackage.ReadIdentity(text));
        Assert.Null(VelopackPackage.ReadIdentity(text + ".missing"));
    }
}
