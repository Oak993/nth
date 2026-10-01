using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The five short phrases a landing becomes (App/Views/Chat/HydraPanel.swift:526-538 HydraStatusText.landed), tested in the
/// order the Swift tests them: no landing, empty, patch, error, conflicts, plain.
/// </summary>
public sealed class HydraStatusTextLandedTests
{
    private static HydraHeadInfo Head(HydraLanding? landing) =>
        new HydraHeadInfo(0, "", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) { Landing = landing };

    private static HydraLanding.File File(string path) => new() { Path = path, Additions = 1, Deletions = 0 };

    private static HydraLanding.File[] Files(int count) => Enumerable.Range(0, count).Select(i => File($"f{i}.txt")).ToArray();

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // HydraPanel.swift:528-529 guard let landing = info.landing else { return nil }
    public void A_head_with_no_copy_of_its_own_has_no_landing_phrase()
    {
        Assert.Null(HydraStatusText.Landed(Head(null)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // HydraPanel.swift:530 if landing.isEmpty { return "changed nothing" } (Hydra.swift:473 no files, no patch, no error)
    public void An_empty_landing_changed_nothing_even_with_conflicts_listed()
    {
        Assert.Equal("changed nothing", HydraStatusText.Landed(Head(new HydraLanding())));
        // isEmpty does not look at the conflicts (Hydra.swift:473), and it is tested first.
        Assert.Equal("changed nothing", HydraStatusText.Landed(Head(new HydraLanding { Conflicts = new[] { "a.txt" } })));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // HydraPanel.swift:532 if landing.patchPath != nil { return "kept as a patch" }, tested before the error
    public void A_patch_path_reads_kept_as_a_patch_before_an_error_or_conflicts_are_considered()
    {
        Assert.Equal("kept as a patch", HydraStatusText.Landed(Head(new HydraLanding { PatchPath = "x.patch" })));
        Assert.Equal("kept as a patch", HydraStatusText.Landed(Head(new HydraLanding
        {
            Files = Files(2), PatchPath = "x.patch", Error = "boom", Conflicts = new[] { "f0.txt" },
        })));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // HydraPanel.swift:533 if landing.error != nil { return "did not land" }, tested before the conflicts
    public void An_error_reads_did_not_land_before_conflicts_are_considered()
    {
        Assert.Equal("did not land", HydraStatusText.Landed(Head(new HydraLanding { Error = "boom" })));
        Assert.Equal("did not land", HydraStatusText.Landed(Head(new HydraLanding
        {
            Files = Files(2), Error = "boom", Conflicts = new[] { "f0.txt" },
        })));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // HydraPanel.swift:531, :534-536 "landed \(files), " + 1 conflict / N conflicts; files is landing.files.count, not the conflicted subset
    public void Conflicts_put_the_whole_file_count_first_and_the_conflict_count_second()
    {
        Assert.Equal("landed 1 file, 1 conflict", HydraStatusText.Landed(Head(new HydraLanding
        {
            Files = Files(1), Conflicts = new[] { "f0.txt" },
        })));
        Assert.Equal("landed 3 files, 1 conflict", HydraStatusText.Landed(Head(new HydraLanding
        {
            Files = Files(3), Conflicts = new[] { "f1.txt" },
        })));
        Assert.Equal("landed 1 file, 2 conflicts", HydraStatusText.Landed(Head(new HydraLanding
        {
            Files = Files(1), Conflicts = new[] { "f0.txt", "elsewhere.txt" },
        })));
        Assert.Equal("landed 4 files, 2 conflicts", HydraStatusText.Landed(Head(new HydraLanding
        {
            Files = Files(4), Conflicts = new[] { "f0.txt", "f1.txt" },
        })));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // HydraPanel.swift:531, :537 return "landed \(files)": "landed 1 file" or "landed N files"
    public void A_clean_landing_reads_landed_one_file_or_landed_n_files()
    {
        Assert.Equal("landed 1 file", HydraStatusText.Landed(Head(new HydraLanding { Files = Files(1) })));
        Assert.Equal("landed 2 files", HydraStatusText.Landed(Head(new HydraLanding { Files = Files(2) })));
        Assert.Equal("landed 12 files", HydraStatusText.Landed(Head(new HydraLanding { Files = Files(12) })));
    }
}
