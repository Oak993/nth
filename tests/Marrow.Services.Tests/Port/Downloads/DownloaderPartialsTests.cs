using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (section 3.2): "partial files older than 7 days are deleted at app start". Ages are measured on the downloader's clock (the manual one here).
[Trait("Spec", "DL-3")]
public sealed class DownloaderPartialsTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Partials_older_than_seven_days_are_deleted_and_younger_ones_and_other_files_stay()
    {
        await using var rig = new DownloadRig();
        Directory.CreateDirectory(rig.PartFolder);
        var now = rig.Runtime.Time.GetUtcNow().UtcDateTime;
        Touch(rig, "old00000.part", now - TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        Touch(rig, "edge0000.part", now - TimeSpan.FromDays(7));
        Touch(rig, "young000.part", now - TimeSpan.FromDays(6));
        Touch(rig, "ancient.bin", now - TimeSpan.FromDays(60));
        var downloader = rig.Create();

        var deleted = downloader.DeleteStalePartials();

        Assert.Equal(1, deleted);
        Assert.Equal(["ancient.bin", "edge0000.part", "young000.part"], Directory.GetFiles(rig.PartFolder).Select(Path.GetFileName).Order());
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_age_is_an_argument_and_a_missing_folder_is_nothing_to_do()
    {
        await using var rig = new DownloadRig();
        Assert.Equal(0, rig.Create().DeleteStalePartials());
        Directory.CreateDirectory(rig.PartFolder);
        var now = rig.Runtime.Time.GetUtcNow().UtcDateTime;
        Touch(rig, "aaaaaaaa.part", now - TimeSpan.FromHours(3));

        Assert.Equal(0, rig.Create().DeleteStalePartials(TimeSpan.FromHours(4)));
        Assert.Equal(1, rig.Create().DeleteStalePartials(TimeSpan.FromHours(2)));
    }

    private static void Touch(DownloadRig rig, string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(rig.PartFolder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}
