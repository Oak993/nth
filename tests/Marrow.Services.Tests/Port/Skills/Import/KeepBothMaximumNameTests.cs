using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.Import;

// RR-45 / OD-0118: repeated Keep both must finish, including at the 64-character limit.
public sealed class KeepBothMaximumNameTests
{
    [Theory]
    [InlineData(ImportMode.Copy)]
    [InlineData(ImportMode.Link)]
    [Trait("Feature", "SK-18")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Maximum_names_keep_the_suffix_and_remain_distinct_after_reopen(ImportMode mode)
    {
        using var home = new ForeignHome();
        var name = new string('a', 57) + "-123456";
        home.Skill(".codex/skills", name, name, "Fixture description.");
        var source = Assert.Single(new ForeignSkillScanner(home.Home).Scan().Skills);
        var before = ForeignHome.Fingerprint(home.Home);
        var library = new ImportedSkillsLibrary(home.SkillsRoot, TimeProvider.System);
        await Task.Run(() =>
        {
            for (var count = 0; count < 12; count++)
            {
                var result = library.Import([source], mode, _ => DuplicateChoice.KeepBoth);
                Assert.Empty(result.Failed);
                Assert.Single(result.Imported);
            }
        }).WaitAsync(TimeSpan.FromSeconds(5));

        var reopened = new ImportedSkillsLibrary(home.SkillsRoot, TimeProvider.System);
        var skills = reopened.Snapshot();
        Assert.Equal(12, skills.Count);
        Assert.Equal(12, skills.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(skills, s => s.Name == new string('a', 57) + "-codex");
        Assert.Contains(skills, s => s.Name == new string('a', 55) + "-codex-10");
        Assert.All(skills, s =>
        {
            Assert.True(SkillName.IsValid(s.Name));
            Assert.Equal(s.Name, ForeignFrontmatter.Parse(Encoding.UTF8.GetString(s.Files["SKILL.md"])).Name);
        });
        Assert.Equal(before, ForeignHome.Fingerprint(home.Home));
    }
}
