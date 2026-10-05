using System.Globalization;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Catalogue;

// Unit SK-2: "every version, hash, size ... change" (DL-2). Versions are pins (a tool's version, a repository's commit, a per-skill commit, a package's version);
// hashes are the SHA-256 of every artifact and of every file; sizes are the plan's approximate size, every artifact's size and the total of an item's files.
// ONDEMAND-PLAN.md 3.7 asks the report for "versions, hashes, licence changes, sizes" and the download size of an update.
[Trait("Spec", "DL-2")]
public sealed class CatalogueChangeReportHashAndSizeTests
{
    private static CatalogueChangeReport Compare(Func<SkillsCatalogue, SkillsCatalogue> change) => CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), change(ChangeReportFixtures.Previous()));

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_tool_version_change_is_a_version_change_with_old_and_new()
    {
        var report = Compare(c => ChangeReportFixtures.Mutate(c, "X1", i => i with { Pin = "1.1.0" }));
        var change = Assert.Single(report.ChangesFor("X1"));
        Assert.Equal((ChangeCategory.Version, "pin", "1.0.0", "1.1.0"), (change.Category, change.Field, change.Old, change.New));
        Assert.Equal("pin: 1.0.0 -> 1.1.0", change.Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_repository_pin_change_keeps_the_full_commits_in_the_data_and_shows_twelve_characters()
    {
        var report = Compare(c => ChangeReportFixtures.Mutate(c, "X3", i => i with { Pin = ChangeReportFixtures.Commit2 }));
        var change = ChangeReportFixtures.Only(report, "X3", ChangeCategory.Version, "pin");
        Assert.Equal(ChangeReportFixtures.Commit1, change.Old);
        Assert.Equal(ChangeReportFixtures.Commit2, change.New);
        Assert.Equal("pin: 0123456789ab -> 89abcdef0123", change.Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pin_that_appears_or_goes_away_reads_none_on_the_other_side()
    {
        var gained = CatalogueChangeReport.Compare(ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => i with { Pin = null }), ChangeReportFixtures.Previous());
        Assert.Equal("pin: (none) -> 1.0.0", ChangeReportFixtures.Only(gained, "X1", ChangeCategory.Version, "pin").Message);
        var lost = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => i with { Pin = null }));
        Assert.Equal("pin: 1.0.0 -> (none)", ChangeReportFixtures.Only(lost, "X1", ChangeCategory.Version, "pin").Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_per_skill_pin_a_package_version_and_a_package_added_or_removed_are_version_changes()
    {
        var previous = ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { Skills = i.Skills.Select(s => s with { Pin = ChangeReportFixtures.Commit1 }).ToList() });
        var next = ChangeReportFixtures.Mutate(ChangeReportFixtures.Mutate(previous, "X3", i => i with { Skills = [i.Skills[0] with { Pin = ChangeReportFixtures.Commit2 }, i.Skills[1]] }),
            "X2", i => i with { Packages = [new CataloguePackage { Name = "fixturemod", Version = "1.1", Licence = "MIT" }, new CataloguePackage { Name = "extra", Version = "0.3", Licence = null }] });
        var report = CatalogueChangeReport.Compare(previous, next);
        Assert.Equal("skills[alpha-skill].pin: 0123456789ab -> 89abcdef0123", ChangeReportFixtures.Only(report, "X3", ChangeCategory.Version, "skills[alpha-skill].pin").Message);
        Assert.DoesNotContain(report.ChangesFor("X3"), c => c.Field == "skills[beta].pin");
        Assert.Equal("packages[fixturemod].version: 1.0 -> 1.1", ChangeReportFixtures.Only(report, "X2", ChangeCategory.Version, "packages[fixturemod].version").Message);
        Assert.Equal("packages[extra]: added (0.3)", ChangeReportFixtures.Only(report, "X2", ChangeCategory.Version, "packages[extra]").Message);
        var removed = CatalogueChangeReport.Compare(next, previous);
        Assert.Equal("packages[extra]: removed (was 0.3)", ChangeReportFixtures.Only(removed, "X2", ChangeCategory.Version, "packages[extra]").Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_artifact_hash_change_lists_the_old_and_new_digest_and_a_new_url_is_a_source_change()
    {
        var report = Compare(c => ChangeReportFixtures.Mutate(c, "X1", i => ChangeReportFixtures.ChangeArtifact(i, "tool.zip", a => a with { Sha256 = CatalogueFixtures.Sha("new"), Url = "https://github.com/example/fixturetool/releases/download/1.0.1/tool.zip" })));
        var hash = ChangeReportFixtures.Only(report, "X1", ChangeCategory.Hash, "artifacts[tool.zip].sha256");
        Assert.Equal(CatalogueFixtures.Sha("tool"), hash.Old);
        Assert.Equal(CatalogueFixtures.Sha("new"), hash.New);
        Assert.Equal($"artifacts[tool.zip].sha256: {CatalogueFixtures.Sha("tool")[..12]} -> {CatalogueFixtures.Sha("new")[..12]}", hash.Message);
        var source = ChangeReportFixtures.Only(report, "X1", ChangeCategory.Source, "artifacts[tool.zip].url");
        Assert.Equal("https://github.com/example/fixturetool/releases/download/1.0.0/tool.zip", source.Old);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_artifact_added_or_removed_and_a_changed_source_url_of_the_item_are_listed()
    {
        var extra = new CatalogueArtifact { Name = "extra.zip", Url = "https://github.com/example/fixturetool/releases/download/1.0.0/extra.zip", Size = 10, Sha256 = CatalogueFixtures.Sha("extra"), Sha256Hint = null };
        var report = Compare(c => ChangeReportFixtures.Mutate(c, "X1", i => i with { Artifacts = [.. i.Artifacts, extra], SourceUrl = "https://github.com/example/other/releases" }));
        Assert.Equal($"artifacts[extra.zip]: added ({CatalogueFixtures.Sha("extra")[..12]})", ChangeReportFixtures.Only(report, "X1", ChangeCategory.Hash, "artifacts[extra.zip]").Message);
        Assert.Equal("sourceUrl: https://github.com/example/fixturetool/releases -> https://github.com/example/other/releases", ChangeReportFixtures.Only(report, "X1", ChangeCategory.Source, "sourceUrl").Message);
        var removed = CatalogueChangeReport.Compare(CatalogueFixtures.Change(ChangeReportFixtures.Previous(), "X1", i => i with { Artifacts = [.. i.Artifacts, extra] }), ChangeReportFixtures.Previous());
        Assert.StartsWith("artifacts[extra.zip]: removed (was ", ChangeReportFixtures.Only(removed, "X1", ChangeCategory.Hash, "artifacts[extra.zip]").Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_changed_added_and_removed_file_is_listed_with_its_path_in_path_order()
    {
        var report = Compare(c => ChangeReportFixtures.Mutate(c, "X3", i => i with
        {
            Files =
            [
                new CatalogueFile { Path = "skills/alpha-skill/SKILL.md", Size = 10, Sha256 = CatalogueFixtures.Sha("alpha2") },
                new CatalogueFile { Path = "skills/gamma/SKILL.md", Size = 12, Sha256 = CatalogueFixtures.Sha("gamma") },
            ],
        }));
        var files = report.ChangesFor("X3").Where(c => c.Category == ChangeCategory.Hash).ToList();
        Assert.Equal(["files[skills/alpha-skill/SKILL.md]", "files[skills/beta/SKILL.md]", "files[skills/gamma/SKILL.md]"], files.Select(f => f.Field));
        Assert.Equal($"files[skills/alpha-skill/SKILL.md]: {CatalogueFixtures.Sha("alpha")[..12]} -> {CatalogueFixtures.Sha("alpha2")[..12]}", files[0].Message);
        Assert.Equal($"files[skills/beta/SKILL.md]: removed (was {CatalogueFixtures.Sha("beta")[..12]})", files[1].Message);
        Assert.Equal($"files[skills/gamma/SKILL.md]: added ({CatalogueFixtures.Sha("gamma")[..12]})", files[2].Message);
        Assert.Equal(CatalogueFixtures.Sha("alpha"), files[0].Old);
        Assert.Null(files[2].Old);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_unchanged_file_list_in_a_different_order_is_no_change()
    {
        var report = Compare(c => ChangeReportFixtures.Mutate(c, "X3", i => i with { Files = i.Files.Reverse().ToList() }));
        Assert.Empty(report.Items);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_approximate_size_an_artifact_size_and_the_total_of_the_files_are_size_changes()
    {
        var report = Compare(c => ChangeReportFixtures.Mutate(ChangeReportFixtures.Mutate(ChangeReportFixtures.Mutate(c, "X1", i => ChangeReportFixtures.ChangeArtifact(i with { ApproxSizeMb = 1.5 }, "tool.zip", a => a with { Size = 4096 })),
            "X3", i => ChangeReportFixtures.ChangeFile(i, "skills/beta/SKILL.md", f => f with { Size = 111 })), "X2", i => i with { ApproxSizeMb = null }));
        Assert.Equal("approxSizeMb: 1 -> 1.5", ChangeReportFixtures.Only(report, "X1", ChangeCategory.Size, "approxSizeMb").Message);
        Assert.Equal("artifacts[tool.zip].size: 1,024 -> 4,096", ChangeReportFixtures.Only(report, "X1", ChangeCategory.Size, "artifacts[tool.zip].size").Message);
        Assert.Equal("files.totalBytes: 21 -> 121", ChangeReportFixtures.Only(report, "X3", ChangeCategory.Size, "files.totalBytes").Message);
        Assert.Equal("approxSizeMb: 2 -> (none)", ChangeReportFixtures.Only(report, "X2", ChangeCategory.Size, "approxSizeMb").Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_files_total_that_shrinks_or_appears_or_goes_away_is_a_size_change_as_much_as_one_that_grows()
    {
        var shrunk = Compare(c => ChangeReportFixtures.Mutate(c, "X3", i => ChangeReportFixtures.ChangeFile(i, "skills/beta/SKILL.md", f => f with { Size = 1 })));
        Assert.Equal("files.totalBytes: 21 -> 11", ChangeReportFixtures.Only(shrunk, "X3", ChangeCategory.Size, "files.totalBytes").Message);
        var emptied = Compare(c => ChangeReportFixtures.Mutate(c, "X3", i => i with { Files = [] }));
        Assert.Equal("files.totalBytes: 21 -> (none)", ChangeReportFixtures.Only(emptied, "X3", ChangeCategory.Size, "files.totalBytes").Message);
        var filled = CatalogueChangeReport.Compare(ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X3", i => i with { Files = [] }), ChangeReportFixtures.Previous());
        Assert.Equal("files.totalBytes: (none) -> 21", ChangeReportFixtures.Only(filled, "X3", ChangeCategory.Size, "files.totalBytes").Message);
        var smaller = Compare(c => ChangeReportFixtures.Mutate(c, "X1", i => ChangeReportFixtures.ChangeArtifact(i, "tool.zip", a => a with { Size = 24 })));
        Assert.Equal("artifacts[tool.zip].size: 1,024 -> 24", ChangeReportFixtures.Only(smaller, "X1", ChangeCategory.Size, "artifacts[tool.zip].size").Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_download_size_of_an_item_is_its_artifacts_or_failing_that_its_files_and_the_change_is_carried_before_and_after()
    {
        var report = Compare(c => ChangeReportFixtures.Mutate(ChangeReportFixtures.Mutate(c, "X1", i => ChangeReportFixtures.ChangeArtifact(i, "tool.zip", a => a with { Size = 4096 })),
            "X3", i => ChangeReportFixtures.ChangeFile(i, "skills/beta/SKILL.md", f => f with { Size = 111 })));
        var tool = report.Items.Single(i => i.ItemId == "X1");
        Assert.Equal((1024L, 4096L), (tool.DownloadBytesBefore, tool.DownloadBytesAfter));
        var skills = report.Items.Single(i => i.ItemId == "X3");
        Assert.Equal((21L, 121L), (skills.DownloadBytesBefore, skills.DownloadBytesAfter));
        var unknown = CatalogueChangeReport.Compare(ChangeReportFixtures.Previous(), ChangeReportFixtures.Mutate(ChangeReportFixtures.Previous(), "X1", i => ChangeReportFixtures.ChangeArtifact(i, "tool.zip", a => a with { Size = null })));
        var none = unknown.Items.Single(i => i.ItemId == "X1");
        Assert.Equal(1024L, none.DownloadBytesBefore);
        Assert.Null(none.DownloadBytesAfter);
        Assert.Equal("artifacts[tool.zip].size: 1,024 -> (none)", ChangeReportFixtures.Only(unknown, "X1", ChangeCategory.Size, "artifacts[tool.zip].size").Message);
    }

    [Fact]
    [Trait("Feature", "SK-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Sizes_are_written_with_invariant_digit_groups_whatever_the_culture_of_the_machine()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var report = Compare(c => ChangeReportFixtures.Mutate(c, "X1", i => ChangeReportFixtures.ChangeArtifact(i with { ApproxSizeMb = 1.5 }, "tool.zip", a => a with { Size = 1_234_567 })));
            Assert.Equal("artifacts[tool.zip].size: 1,024 -> 1,234,567", ChangeReportFixtures.Only(report, "X1", ChangeCategory.Size, "artifacts[tool.zip].size").Message);
            Assert.Equal("approxSizeMb: 1 -> 1.5", ChangeReportFixtures.Only(report, "X1", ChangeCategory.Size, "approxSizeMb").Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
