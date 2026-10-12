using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers"), unit U9 (ED-NEW-inst-u9-1). A folder a running setup is using
// is marked by one open file, so no other sweep (a second user session's setup, nth) can take it from under the run. Windows
// closes the handle when the holder ends, however it ends, so a crash leaves a plain folder for the next sweep.
public sealed class FolderClaimTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_claim_makes_the_folder_and_holds_one_file_in_it()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");

        using var claim = FolderClaim.Take(folder);

        Assert.Equal(folder, claim.Folder);
        Assert.True(File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_claim_on_the_same_folder_fails_while_the_first_is_held_and_works_after_it_is_let_go()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        var first = FolderClaim.Take(folder);

        Assert.Throws<IOException>(() => FolderClaim.Take(folder));
        first.Dispose();
        using var second = FolderClaim.Take(folder);

        Assert.True(File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Letting_go_removes_the_claim_file_and_nothing_else_and_can_be_done_twice()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        var engine = area.Put(Path.Combine(folder, "nth Setup Engine.exe"));
        var claim = FolderClaim.Take(folder);

        claim.Dispose();
        claim.Dispose();

        Assert.False(File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)));
        Assert.True(File.Exists(engine));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_claim_over_a_claim_file_a_dead_run_left_behind_works()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        area.Put(Path.Combine(folder, SetupFolders.ClaimFileName));

        using var claim = FolderClaim.Take(folder);

        Assert.True(File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("1.0.0")]
    public void A_claim_needs_a_full_path_so_it_is_never_made_in_the_working_folder(string folder) =>
        Assert.ThrowsAny<ArgumentException>(() => FolderClaim.Take(folder));
}
