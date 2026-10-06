using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class SkillsOwnRootLinkReviewTests
{
    [Theory, Trait("Feature", "SK-11f"), Trait("Rule", "R-TEST-9")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_link_at_the_configured_root_is_not_listed_or_read(bool read)
    {
        using var scratch = new StorageScratchFolder();
        Directory.CreateDirectory(scratch.Root);
        var target = Path.Combine(scratch.Root, "target");
        var folder = Path.Combine(target, "a");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), "OUTSIDE ROOT SENTINEL");
        var link = Path.Combine(scratch.Root, "configured-root");
        TestLinks.Directory(scratch.Root, link, target);
        try
        {
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            var disk = new DiskUserSkillFiles();
            Assert.Equal("OUTSIDE ROOT SENTINEL", disk.ReadText(target, Path.Combine(folder, "SKILL.md")));
            if (read) Assert.Null(disk.ReadText(link, Path.Combine(link, "a", "SKILL.md")));
            else Assert.Empty(disk.SkillFolders(link));
        }
        finally { Directory.Delete(link); }
    }
}
